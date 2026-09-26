import { draftScopeFromKey, emptyDraftValue, purgeGroupDrafts, reconcileGroupDraftScopes, registerDraftKey, scopeLease, scopeLeaseValid, type GroupDraftScope } from "./draft-revocation.ts";
import { useEffect, useRef, useState, type SetStateAction } from "react";
export type DraftStorage = Pick<Storage, "getItem" | "setItem" | "removeItem">;
export function draftKey(owner: string | null | undefined, community: string, topic: string, kind: string, objectId = "") { return `zapara.draft.v2:${owner || "guest"}:${community}:${topic}:${kind}:${objectId}`; }
export function readStoredDraft<T>(storage: DraftStorage, key: string, initial: () => T): T {
    try {
        const value = storage.getItem(key);
        return value === null ? initial() : JSON.parse(value) as T;
    }
    catch {
        return initial();
    }
}
export function clearStoredDraft<T>(storage: DraftStorage, key: string, expected: T, current: T): boolean {
    const actual = readStoredDraft(storage, key, () => current);
    if (JSON.stringify(actual) !== JSON.stringify(expected))
        return false;
    storage.removeItem(key);
    return true;
}
const changeEvent = "zapara-draft-write";
export function useStoredDraft<T>(key: string, initial: () => T) {
    const scope = draftScopeFromKey(key);
    const leaseRef = useRef({ key, lease: scope ? scopeLease(sessionStorage, scope) : "" });
    const renderLease = leaseRef.current.lease;
    const valid = () => !scope || leaseRef.current.key === key && scopeLeaseValid(sessionStorage, scope, renderLease);
    registerDraftKey(sessionStorage, key);
    const initialRef = useRef(initial);
    initialRef.current = initial;
    const [value, setValue] = useState<T>(() => readStoredDraft(sessionStorage, key, initial));
    const current = useRef(value);
    current.current = value;
    useEffect(() => { const approve = (event: Event) => { const allowed = (event as CustomEvent<GroupDraftScope>).detail; if (!scope || scope.owner !== allowed.owner || scope.community !== allowed.community || allowed.topic !== undefined && scope.topic !== allowed.topic)
        return; const fresh = scopeLease(sessionStorage, scope); if (fresh === leaseRef.current.lease)
        return; leaseRef.current = { key, lease: fresh }; const next = readStoredDraft(sessionStorage, key, initialRef.current); current.current = next; setValue(next); }; window.addEventListener("zapara-group-draft-approved", approve); return () => window.removeEventListener("zapara-group-draft-approved", approve); }, [key]);
    const notify = (next: T) => window.dispatchEvent(new CustomEvent(changeEvent, { detail: { key, value: next } }));
    useEffect(() => { if (leaseRef.current.key !== key) {
        leaseRef.current = { key, lease: scope ? scopeLease(sessionStorage, scope) : "" };
        const next = readStoredDraft(sessionStorage, key, initialRef.current);
        current.current = next;
        setValue(next);
    } }, [key]);
    useEffect(() => {
        const update = (event: Event) => {
            const detail = (event as CustomEvent<{
                key: string;
                value?: T;
                purged?: boolean;
            }>).detail;
            if (detail.key !== key)
                return;
            const next = detail.purged ? emptyDraftValue(current.current) : detail.value as T;
            current.current = next;
            setValue(next);
        };
        window.addEventListener(changeEvent, update);
        return () => window.removeEventListener(changeEvent, update);
    }, [key]);
    function set(next: SetStateAction<T>) { if (!valid())
        return; const actual = readStoredDraft(sessionStorage, key, () => current.current); const updated = typeof next === "function" ? (next as (current: T) => T)(actual) : next; sessionStorage.setItem(key, JSON.stringify(updated)); current.current = updated; setValue(updated); notify(updated); }
    function clear(expected: T, replacement?: T) {
        if (!valid())
            return false;
        if (!clearStoredDraft(sessionStorage, key, expected, current.current))
            return false;
        const next = replacement === undefined ? initialRef.current() : replacement;
        notify(next);
        current.current = next;
        setValue(next);
        return true;
    }
    function field<K extends keyof T>(name: K, next: SetStateAction<T[K]>) { set(actual => ({ ...actual, [name]: typeof next === "function" ? (next as (value: T[K]) => T[K])(actual[name]) : next })); }
    function adopt(next: T) {
        if (!valid())
            return false;
        if (sessionStorage.getItem(key) !== null)
            return false;
        current.current = next;
        setValue(next);
        return true;
    }
    return [value, set, clear, field, adopt] as const;
}
export function draftEpochMatches(current: number | undefined, sent: number) { return (current ?? 0) === sent; }
export function attachDiscussionContext(current: Record<string, string>, key: string, caption: string, replace = false): Record<string, string> { return !replace && Object.prototype.hasOwnProperty.call(current, key) ? current : { ...current, [key]: caption }; }
export function clearSentDiscussionContext(current: Record<string, string>, key: string, sent: string) { return current[key] === sent ? { ...current, [key]: "" } : current; }
export function currentDraftEpoch(storage: DraftStorage, key: string): number { const values = readStoredDraft<Record<string, number>>(storage, "zapara.group.draft-epochs", () => ({})); const value = values[key]; return typeof value === "number" && Number.isSafeInteger(value) && value >= 0 ? value : 0; }
export function advanceDraftEpoch(storage: DraftStorage, key: string): number { const values = readStoredDraft<Record<string, number>>(storage, "zapara.group.draft-epochs", () => ({})); const next = currentDraftEpoch(storage, key) + 1; storage.setItem("zapara.group.draft-epochs", JSON.stringify({ ...values, [key]: next })); return next; }
export function revokeGroupDrafts(scope: GroupDraftScope | {
    owner: string;
}) { purgeGroupDrafts(sessionStorage, scope, key => { const value = key.startsWith("zapara.draft.v2:") ? undefined : readStoredDraft(sessionStorage, key, () => ({})); window.dispatchEvent(new CustomEvent("zapara-draft-write", { detail: { key, value, purged: key.startsWith("zapara.draft.v2:") } })); }); }
export function approveGroupDrafts(scope: GroupDraftScope) { window.dispatchEvent(new CustomEvent("zapara-group-draft-approved", { detail: scope })); }
export function reconcileGroupDrafts(scope: {
    owner: string;
    community: string;
}, topics: (string | null)[]) { reconcileGroupDraftScopes(sessionStorage, scope, topics, key => { const specialized = key.startsWith("zapara.draft.v2:"); const value = specialized ? undefined : readStoredDraft(sessionStorage, key, () => ({})); window.dispatchEvent(new CustomEvent("zapara-draft-write", { detail: { key, value, purged: specialized } })); }); }
