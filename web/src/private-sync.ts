import { useEffect, useRef, useState } from "react";
import * as api from "./api.ts";
import { canonicalUtc, syncSubjectKey } from "./utc.ts";
import { normalizeIntersectionStrictness } from "./intersectionStrictness.ts";
import type { HomeworkItem } from "./types";
export type SyncHomeworkValue = {
    subjectRaw: string;
    subjectKey: string;
    text: string;
    targetNthOccurrence: number;
    createdAtUtc: string;
    legacyCreatedLocalDate: string | null;
};
export type SyncSettingsValue = {
    selectedGroupId: string | null;
    parityInvert: boolean;
    notifyTime1: string | null;
    notifyTime2: string | null;
    strictness: number;
    alwaysShow: boolean;
};
export type SyncRecord = {
    entityType: string;
    entityId: string;
    revision: number;
    tombstone: boolean;
    changedAt: string;
    value: SyncHomeworkValue | SyncSettingsValue | {
        done: boolean;
        doneAtUtc: string | null;
    } | null;
};
type Pending = {
    opId: string;
    type: "homework" | "completion" | "settings";
    id: string;
    revision: number;
    action?: "upsert" | "delete";
    deletedItem?: HomeworkItem;
    value: SyncHomeworkValue | SyncSettingsValue | {
        done: boolean;
        doneAtUtc: string | null;
    } | null;
    conflict?: SyncRecord | null;
};
type Profile = {
    owner: string;
    items: HomeworkItem[];
    records: SyncRecord[];
    pending: Pending[];
    settingsIntent?: Partial<SyncSettingsValue>;
    readFailed?: boolean;
};
const guestKey = "zapara.homework";
const accountKey = (owner: string) => `zapara.private-homework.${owner}`;
function read(owner: string): Profile {
    try {
        if (owner === "guest")
            return { owner, items: JSON.parse(localStorage.getItem(guestKey) || "[]"), records: [], pending: [] };
        const raw = JSON.parse(localStorage.getItem(accountKey(owner)) || "{}");
        return { owner, items: raw.items ?? [], records: raw.records ?? [], pending: raw.pending ?? [], settingsIntent: raw.settingsIntent ?? undefined };
    }
    catch {
        return { owner, items: [], records: [], pending: [], readFailed: true };
    }
}
function write(profile: Profile) { localStorage.setItem(profile.owner === "guest" ? guestKey : accountKey(profile.owner), JSON.stringify(profile.owner === "guest" ? profile.items : profile)); }
export function homeworkSyncValue(item: HomeworkItem): SyncHomeworkValue { return { subjectRaw: item.subject, subjectKey: syncSubjectKey(item.subject), text: item.text, targetNthOccurrence: Math.max(1, Math.min(10, item.targetNthOccurrence ?? 1)), createdAtUtc: canonicalUtc(item.created), legacyCreatedLocalDate: item.legacyCreatedLocalDate ?? null }; }
export function projectHomework(records: SyncRecord[], local: HomeworkItem[]): HomeworkItem[] {
    return records.filter(record => record.entityType === "homework" && !record.tombstone && record.value).map(record => {
        const value = record.value as SyncHomeworkValue;
        const completion = records.find(row => row.entityType === "completion" && row.entityId === record.entityId && !row.tombstone)?.value as {
            done: boolean;
        } | undefined;
        return { ...local.find(row => row.id === record.entityId), id: record.entityId, subject: value.subjectRaw, text: value.text, created: value.createdAtUtc, targetNthOccurrence: value.targetNthOccurrence, legacyCreatedLocalDate: value.legacyCreatedLocalDate, done: completion?.done ?? false };
    });
}
export function usePrivateHomework(userId: string | null) {
    const owner = userId ?? "guest";
    const [profile, setProfile] = useState<Profile>(() => read(owner));
    const current = useRef(profile);
    const expectedOwner = useRef(owner);
    expectedOwner.current = owner;
    const [status, setStatus] = useState("");
    const [tick, setTick] = useState(0);
    const running = useRef<object | null>(null);
    const mutationsInFlight = useRef(new Set<string>());
    const cursor = useRef<{
        owner: string;
        epoch: string;
        sequence: number;
    } | null>(null);
    const commit = (next: Profile) => {
        if (next.owner !== expectedOwner.current)
            return false;
        if (current.current.owner === next.owner && current.current.readFailed) throw new Error("Не удалось прочитать сохранённые данные. Повторите загрузку перед изменениями.");
        write(next);
        current.current = next;
        setProfile(next);
        return true;
    };
    useEffect(() => { const next = read(owner); current.current = next; setProfile(next); setStatus(owner === "guest" ? "Гостевые данные на устройстве" : "Синхронизация ожидается"); }, [owner]);
    useEffect(() => {
        if (owner === "guest")
            return;
        let stop = false;
        const runToken = {};
        async function pull() {
            if (running.current === runToken)
                return;
            running.current = runToken;
            try {
                let epoch: string;
                let records: SyncRecord[];
                let nextCursor: {
                    owner: string;
                    epoch: string;
                    sequence: number;
                };
                if (cursor.current?.owner === owner) {
                    epoch = cursor.current.epoch;
                    records = [...current.current.records];
                    let more = true;
                    let after = cursor.current.sequence;
                    while (more) {
                        const page = await api.privateChanges(epoch, after);
                        if (stop || expectedOwner.current !== owner)
                            return;
                        for (const change of page.changes)
                            records = [...records.filter(row => row.entityType !== change.record.entityType || row.entityId !== change.record.entityId), change.record];
                        if (page.hasMore && page.nextAfterSequence <= after)
                            throw new Error("invalid cursor");
                        if (page.metadata.syncEpoch !== epoch)
                            throw new Error("invalid sync epoch");
                        after = page.nextAfterSequence;
                        more = page.hasMore;
                    }
                    nextCursor = { owner, epoch, sequence: after };
                }
                else {
                    const manifest = await api.beginSyncSnapshot();
                    epoch = manifest.syncEpoch;
                    let ordinal = 0;
                    let more = true;
                    records = [];
                    while (more) {
                        const page = await api.syncSnapshotPage(manifest.manifestId, ordinal);
                        if (stop || expectedOwner.current !== owner)
                            return;
                        records.push(...page.items.map(item => item.record));
                        if (page.hasMore && page.nextAfterOrdinal <= ordinal)
                            throw new Error("invalid cursor");
                        ordinal = page.nextAfterOrdinal;
                        more = page.hasMore;
                    }
                    nextCursor = { owner, epoch, sequence: manifest.highWater };
                }
                if (stop || expectedOwner.current !== owner)
                    return;
                let local = current.current;
                let pendingQueue = local.pending;
                if (local.settingsIntent && Object.keys(local.settingsIntent).length > 0) {
                    const settingsId = "00000000-0000-0000-0000-000000000001";
                    const pendingSettings = pendingQueue.find(row => row.type === "settings");
                    const serverSettings = records.find(row => row.entityType === "settings" && !row.tombstone);
                    const defaults: SyncSettingsValue = { selectedGroupId: null, parityInvert: false, notifyTime1: null, notifyTime2: null, strictness: 50, alwaysShow: false };
                    const value = { ...defaults, ...(pendingSettings?.value ?? serverSettings?.value), ...local.settingsIntent } as SyncSettingsValue;
                    value.strictness = normalizeIntersectionStrictness(value.strictness);
                    pendingQueue = [...pendingQueue.filter(row => row.type !== "settings"), {
                        opId: crypto.randomUUID(), id: settingsId, type: "settings", revision: pendingSettings?.revision ?? serverSettings?.revision ?? 0, value,
                    }];
                }
                // Pending edits remain visible until acknowledged or explicitly resolved.
                const ids = new Set(pendingQueue.map(row => row.id));
                if (!commit({ ...local, records, pending: pendingQueue, settingsIntent: undefined,
                    items: [...projectHomework(records, local.items).filter(row => !ids.has(row.id)), ...local.items.filter(row => ids.has(row.id))] }))
                    return;
                cursor.current = nextCursor;
                const queue = [...current.current.pending];
                for (const pending of queue) {
                    if (pending.conflict !== undefined)
                        continue;
                    if (stop || expectedOwner.current !== owner)
                        return;
                    if (!current.current.pending.some(row => row.opId === pending.opId)) continue;
                    const flight = `${owner}:${pending.opId}`;
                    mutationsInFlight.current.add(flight);
                    let result: Awaited<ReturnType<typeof api.mutatePrivate>>;
                    try { result = await api.mutatePrivate(epoch, pending.opId, pending.type, pending.id, pending.revision, pending.value, owner, pending.action); }
                    finally { mutationsInFlight.current.delete(flight); }
                    if (stop || expectedOwner.current !== owner)
                        return;
                    local = current.current;
                    const same = local.pending.find(row => row.opId === pending.opId);
                    if (!same)
                        continue;
                    if (result.status === 409) {
                        commit({ ...local, pending: local.pending.map(row => row.opId === pending.opId ? { ...row, conflict: result.serverRecord } : row) });
                        continue;
                    }
                    if (result.serverRecord) {
                        const updated = [...local.records.filter(row => row.entityType !== pending.type || row.entityId !== pending.id), result.serverRecord];
                        commit({ ...local, records: updated, pending: local.pending.filter(row => row.opId !== pending.opId) });
                    }
                }
                setStatus(current.current.pending.some(row => row.conflict !== undefined) ? "Есть конфликты. Выберите, какую версию сохранить." : current.current.pending.length ? "Есть несохранённые изменения" : "Личная домашка синхронизирована");
            }
            catch (error) {
                if (error instanceof Error && error.message === "410")
                    cursor.current = null;
                if (!stop)
                    setStatus("Нет связи с синхронизацией. Локальные изменения сохранены.");
            }
            finally {
                if (running.current === runToken) running.current = null;
            }
        }
        void pull();
        const timer = window.setInterval(() => void pull(), 10000);
        const wake = () => { if (!document.hidden) void pull(); };
        window.addEventListener("online", wake);
        window.addEventListener("focus", wake);
        document.addEventListener("visibilitychange", wake);
        return () => { stop = true; window.clearInterval(timer); window.removeEventListener("online", wake); window.removeEventListener("focus", wake); document.removeEventListener("visibilitychange", wake); };
    }, [owner, tick]);
    function save(item: HomeworkItem) {
        const local = current.current;
        if (local.owner !== owner || expectedOwner.current !== owner)
            throw new Error("Профиль изменился. Откройте задание заново.");
        const previous = local.items.find(row => row.id === item.id);
        const items = previous ? local.items.map(row => row.id === item.id ? item : row) : [item, ...local.items];
        let pending = local.pending;
        if (owner !== "guest") {
            const homework = homeworkSyncValue(item);
            const old = previous ? homeworkSyncValue(previous) : null;
            const updates: {
                type: "homework" | "completion";
                value: Pending["value"];
            }[] = [];
            if (!old || JSON.stringify(old) !== JSON.stringify(homework))
                updates.push({ type: "homework", value: homework });
            if (!previous || previous.done !== item.done)
                updates.push({ type: "completion", value: { done: item.done, doneAtUtc: item.done ? canonicalUtc(new Date()) : null } });
            for (const update of updates) {
                const existing = pending.find(row => row.type === update.type && row.id === item.id);
                const revision = existing?.revision ?? local.records.find(row => row.entityType === update.type && row.entityId === item.id)?.revision ?? 0;
                pending = [...pending.filter(row => row.type !== update.type || row.id !== item.id), { opId: crypto.randomUUID(), id: item.id, type: update.type, revision, value: update.value }];
            }
        }
        if (!commit({ ...local, items, pending })) return;
        if (owner !== "guest" && pending.length) setStatus("Есть несохранённые изменения");
        setTick(value => value + 1);
    }
    function remove(id: string) {
        const local=current.current;
        if(local.owner!==owner||expectedOwner.current!==owner)throw new Error("Профиль изменился.");
        if(!local.items.some(row=>row.id===id))throw new Error("Задание уже удалено.");
        let pending=local.pending;
        if(owner!=="guest") {
            const record=local.records.find(row=>row.entityType==="homework"&&row.entityId===id&&!row.tombstone);
            const related = pending.filter(row=>row.id===id);
            if(related.some(row=>mutationsInFlight.current.has(`${owner}:${row.opId}`)))throw new Error("Задание сейчас синхронизируется. Дождитесь ответа и повторите удаление.");
            pending=pending.filter(row=>row.id!==id);
            if(record?.revision) pending=[...pending,{opId:crypto.randomUUID(),type:"homework",id,revision:record.revision,value:null,action:"delete",deletedItem:local.items.find(row=>row.id===id)}];
            else if(!related.some(row=>row.type==="homework"&&row.revision===0&&row.action!=="delete"))throw new Error("Сначала подтвердите синхронизацию этого задания.");
        }
        if(!commit({...local,items:local.items.filter(row=>row.id!==id),pending}))throw new Error("Профиль изменился.");
        setStatus(owner==="guest"?"Задание удалено на устройстве":pending.some(row=>row.id===id&&row.action==="delete")?"Удаление ожидает синхронизации":"Локальное задание удалено до отправки на сервер");setTick(value=>value+1);
    }
    function retryRead() {
        if(expectedOwner.current!==owner)return;
        const next=read(owner);
        if(next.readFailed){setStatus("Не удалось прочитать сохранённые задания. Доступные данные оставлены.");return;}
        current.current=next;setProfile(next);setTick(value=>value+1);
    }
    function resolve(opId: string, choice: "local" | "server", expectedRevision?: number | null) {
        const local = current.current;
        if(local.owner!==owner||expectedOwner.current!==owner)return;
        const pending = local.pending.find(row => row.opId === opId);
        if (!pending)
            return;
        if(expectedRevision!==undefined&&expectedRevision!==(pending.conflict?.revision??null)){setStatus("Конфликт обновился. Сверьте показанные версии заново.");return;}
        const records = pending.conflict ? [...local.records.filter(row => row.entityType !== pending.type || row.entityId !== pending.id), pending.conflict] : local.records;
        const queue = choice === "server" ? local.pending.filter(row => row.opId !== opId) : local.pending.map(row => row.opId === opId ? { ...row, opId: crypto.randomUUID(), revision: pending.conflict?.revision ?? 0, conflict: undefined } : row);
        const ids = new Set(queue.map(row => row.id));
        const localWithDeleted=pending.deletedItem&&!local.items.some(row=>row.id===pending.id)?[...local.items,pending.deletedItem]:local.items;
        if (!commit({ ...local, records, pending: queue, items: [...projectHomework(records, localWithDeleted).filter(row => !ids.has(row.id)), ...local.items.filter(row => ids.has(row.id))] })) return;
        setStatus(queue.length ? "Есть несохранённые изменения" : "Синхронизация ожидается");
        setTick(value => value + 1);
    }
    function saveSettings(patch: Partial<SyncSettingsValue>) {
        const local = current.current;
        if (owner === "guest" || local.owner !== owner)
            return false;
        if (cursor.current?.owner !== owner) {
            if (!commit({ ...local, settingsIntent: { ...local.settingsIntent, ...patch } })) return false;
            setStatus("Изменения настроек ожидают синхронизации");
            setTick(value => value + 1);
            return true;
        }
        const id = "00000000-0000-0000-0000-000000000001";
        const pending = local.pending.find(row => row.type === "settings");
        const record = local.records.find(row => row.entityType === "settings" && !row.tombstone);
        const defaults: SyncSettingsValue = { selectedGroupId: null, parityInvert: false, notifyTime1: null, notifyTime2: null, strictness: 50, alwaysShow: false };
        const value = { ...defaults, ...(pending?.value ?? record?.value), ...patch } as SyncSettingsValue;
        value.strictness = normalizeIntersectionStrictness(value.strictness);
        if (!commit({ ...local, pending: [...local.pending.filter(row => row.type !== "settings"), { opId: crypto.randomUUID(), id, type: "settings", revision: pending?.revision ?? record?.revision ?? 0, value }] })) return false;
        setStatus("Есть несохранённые изменения");
        setTick(value => value + 1);
        return true;
    }
    function importGuest() {
        if (owner === "guest")
            return;
        const guest = read("guest");
        for (const item of guest.items)
            save({ ...item, id: crypto.randomUUID() });
    }
    const settingsBase = profile.pending.find(row => row.type === "settings")?.value ?? profile.records.find(row => row.entityType === "settings" && !row.tombstone)?.value;
    const settings = profile.owner === owner && settingsBase
        ? profile.settingsIntent ? { ...settingsBase, ...profile.settingsIntent } as SyncSettingsValue : settingsBase as SyncSettingsValue
        : undefined;
    return { items: profile.owner === owner ? profile.items : [], save, remove, retryRead, readFailed: profile.owner === owner && !!profile.readFailed,
        status: profile.owner === owner ? status : owner === "guest" ? "Гостевые данные на устройстве" : "Синхронизация ожидается",
        saveSettings, settings,
        ready: profile.owner === owner && !profile.readFailed && (owner === "guest" || cursor.current?.owner === owner), pending: profile.owner === owner ? profile.pending : [], resolve, refresh: () => setTick(value => value + 1), importGuest };
}
