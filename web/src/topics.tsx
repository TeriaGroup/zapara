import { FormEvent, useEffect, useRef, useState, type SetStateAction } from "react";
import * as api from "./api";
import { applyAccessPreset, detectAccessPreset, globalPower, groupOnlyPowers, preservedPinMetadata, topicAction, topicRules } from "./topic-policy";
import { approveGroupDrafts, draftKey, readStoredDraft, reconcileGroupDrafts, revokeGroupDrafts, useStoredDraft } from "./draft-store";
import { registerGroupConversation, scopeLease, scopeLeaseValid } from "./draft-revocation";
import { createTopicAuthority } from "./topic-authority";
import { hasPendingCreation, submitCreationOnce, usePendingCreation } from "./creation-pending";
import { useApp } from "./store";
import { noteSearch } from "./ux300";
import { orderedTopics, topicPreview } from "./channels";
import { unreadBadgeDescription, unreadBadgeText } from "./groupBrowse";
import { useCommunityTimetable } from "./use-community-timetable";
import { timetableSubjects } from "./community-timetable";
import { Icon } from "./icons";
import type { AccessRule, GroupSpace, GroupTopic, TopicAccess } from "./types";
export const topicTemplates = [
    { template: "chat", kind: "chat", title: "Чат", icon: "💬", description: "Сообщения и вложения" },
    { template: "announcements", kind: "chat", title: "Объявления", icon: "📌", description: "Важные сообщения группы" },
    { template: "polls", kind: "ballots", title: "Опросы", icon: "🗳️", description: "Голосования и решения" },
    { template: "forms", kind: "forms", title: "Анкеты", icon: "✏️", description: "Вопросы и ответы" },
    { template: "subject", kind: "chat", title: "Предмет", icon: "📚", description: "Переписка и данные предмета" },
    { template: "materials", kind: "materials", title: "Материалы", icon: "📎", description: "Файлы и ссылки" },
    { template: "homework", kind: "homework", title: "Домашка", icon: "📒", description: "Общие задания" },
    { template: "schedule", kind: "schedule", title: "Расписание", icon: "📅", description: "Учебные занятия" },
];
export const powerTitles: Record<string, string> = { read: "Просматривать", post: "Публиковать", media: "Прикреплять файлы", vote: "Голосовать", formsRespond: "Заполнять анкеты", ballots: "Создавать опросы", forms: "Создавать анкеты", close: "Завершать опросы", pin: "Закреплять", moderate: "Модерировать", homework: "Добавлять домашку", mentionAll: "Упоминать всех", joins: "Принимать заявки", exclude: "Исключать", channels: "Управлять каналами", access: "Управлять доступом", roles: "Менять роли", grants: "Назначать роли" };
export function TopicMark({ topic }: {
    topic: GroupTopic;
}) {
    return <span className="topic-icon">{topic.icon === "💬" ? <Icon name="chat"/> : topic.icon === "📌" ? <Icon name="pin"/> : topic.icon === "🗳️" ? <Icon name="ballot"/> : topic.icon === "📚" ? <Icon name="homework"/> : topic.icon}</span>;
}
function failure(error: unknown) { return error instanceof Error && error.message === "access-refresh-failed" ? "Доступ изменился. Актуальные настройки не загрузились. Ваши правила сохранены; повторите проверку последствий." : error instanceof Error && error.message === "409" ? "Настройки изменились. Загружена актуальная версия; ваши несохранённые поля оставлены в форме. Проверьте их перед повторным сохранением." : error instanceof Error && error.message === "403" ? "Доступ к каналу изменился" : "Изменение не сохранено. Ввод оставлен в форме."; }
const emptyChannelFields = () => ({ title: "", icon: "💬", template: "chat", description: "", categoryId: "", subject: "", position: 0, accent: "default" as import("./types").ChannelAccent, pinned: false, writePolicy: "all" as "all" | "managers" });
const channelKindChoices = [["all", "Все"], ["chat", "Чаты"], ["ballots", "Голосования"], ["forms", "Анкеты"], ["materials", "Материалы"], ["homework", "Домашка"], ["schedule", "Расписание"]] as const;
export function GroupTopics({ communityId, groupName, onOpen, onError }: {
    communityId: string;
    groupName: string | null;
    onOpen: (topic: GroupTopic, canManage: boolean) => void;
    onError: (text: string) => void;
}) {
    const app = useApp();
    const owner = app.session?.user?.userId || "guest", scopeId = `${owner}:${communityId}`;
    const scopeRef = useRef(scopeId);
    scopeRef.current = scopeId;
    const mounted = useRef(false);
    useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
    const [stateScope, setStateScope] = useState(scopeId);
    const scopeReady = stateScope === scopeId;
    const creationKey = draftKey(owner, communityId, "new", "topic-create");
    const creationPending = usePendingCreation(creationKey);
    const authorityRef = useRef<ReturnType<typeof createTopicAuthority> | null>(null);
    const allRef = useRef<GroupTopic[]>([]);
    const previewSequence = useRef(0), accessSequence = useRef(0);
    const previewTarget = useRef<{
        userId: string | null;
        roleId: string | null;
    } | null>(null);
    const alive = () => mounted.current && scopeRef.current === scopeId;
    const lease = () => ({ scope: scopeId, ticket: authorityRef.current?.ticket() ?? 0 });
    const valid = (request: ReturnType<typeof lease>) => alive() && request.scope === scopeRef.current && !!authorityRef.current?.valid(request.ticket);
    const [creationDraft, , clearCreationDraft, setCreationField] = useStoredDraft(draftKey(app.session?.user?.userId, communityId, "new", "topic-create"), () => ({ fields: emptyChannelFields(), initialRules: [] as AccessRule[], postSelectionMode: null as "selected" | null }));
    const wasCreating = useRef(creationPending);
    useEffect(() => { if (wasCreating.current && !creationPending && editingRef.current === "new" && sessionStorage.getItem(creationKey) === null) {
        setFieldsState(emptyChannelFields());
        setEditing(null);
    } wasCreating.current = creationPending; }, [creationPending]);
    const timetable = useCommunityTimetable(groupName);
    const [spaceValue, setSpace] = useState<GroupSpace | null>(null);
    const space = scopeReady ? spaceValue : null;
    const [legacy, setLegacy] = useState(false);
    const [loading, setLoading] = useState(true);
    const [query, setQuery] = useState("");
    const [kind,setKind]=useState("all");
    const [unread, setUnread] = useState(false);
    const [searchOpen,setSearchOpen]=useState(false);
    const [toolsOpen,setToolsOpen]=useState(false);
    const [busy, setBusy] = useState(false);
    const [manage, setManage] = useState(false);
    const [archive, setArchive] = useState<GroupTopic[] | null>(null);
    const [editingMode, setEditingMode] = useState<"full" | "access">("full");
    const editingModeRef = useRef(editingMode);
    editingModeRef.current = editingMode;
    const [collapsed, setCollapsed] = useState<Record<string, boolean>>(() => {
        try {
            return JSON.parse(localStorage.getItem(`zapara.topic.categories.${communityId}`) || "{}");
        }
        catch {
            return {};
        }
    });
    const [editing, setEditing] = useState<GroupTopic | "new" | null>(null);
    const [fields, setFieldsState] = useState(emptyChannelFields);
    const fieldsRef = useRef(fields);
    fieldsRef.current = fields;
    const editingRef = useRef(editing);
    editingRef.current = editing;
    function setFields(next: SetStateAction<ReturnType<typeof emptyChannelFields>>) {
        const value = typeof next === "function" ? next(fieldsRef.current) : next;
        fieldsRef.current = value;
        setFieldsState(value);
        if (editingRef.current === "new")
            setCreationField("fields", value);
    }
    const [access, setAccess] = useState<TopicAccess | null>(null);
    const [accessPreview, setAccessPreview] = useState<{
        signature: string;
        value: api.AccessPreview;
    } | null>(null);
    const [participants, setParticipants] = useState<import("./types").Classmate[]>([]);
    const [postSelectionMode, setPostSelectionMode] = useState<"selected" | null>(null);
    const [role, setRole] = useState("");
    const [rules, setRules] = useState<AccessRule[]>([]);
    const [categoryTitle, setCategoryTitle] = useState("");
    const [categoryEdit, setCategoryEdit] = useState<{id:string;title:string;position:string;revision:number}|null>(null);
    const [preview, setPreview] = useState<{
        topics: GroupTopic[];
        roleId: string;
    } | null>(null);
    const epoch = useRef(0);
    const outgoingRules = topicRules(rules);
    const accessSignature = JSON.stringify({ topicId: access?.topicId, revision: access?.revision, rules: outgoingRules });
    const preparedAccess = accessPreview?.signature === accessSignature ? accessPreview.value : null;
    const personName = (id: string) => participants.find(person => person.userId === id)?.displayName || participants.find(person => person.userId === id)?.username || `Участник ${id.slice(0, 8)}`;
    useEffect(() => { let stopped = false; void api.groupHome(communityId).then(home => { if (stopped || scopeRef.current !== scopeId)
        return; if (home.groupChat?.conversationId)
        registerGroupConversation(sessionStorage, owner, communityId, home.groupChat.conversationId); setParticipants(home.classmates); }).catch(() => undefined); return () => { stopped = true; }; }, [scopeId]);
    const canManage = !!space && globalPower(space.desk, "channels");
    const canAccess = !!space && globalPower(space.desk, "access");
    const activeTopic = scopeReady && editing && editing !== "new" ? allRef.current.find(topic => topic.topicId === editing.topicId) ?? null : null;
    const allowed = (topic: GroupTopic | null, power: string) => topicAction(topic, power, space?.desk ?? null, legacy);
    const canEditAccess = allowed(activeTopic, "access");
    const canEditChannel = editing === "new" ? canManage : allowed(activeTopic, "channels");
    const canEditPolicy = editing === "new" || canEditAccess;
    const canEditPin = editing === "new" || allowed(activeTopic, "pin");
    const readPreset = detectAccessPreset(rules, "read"), detectedPostPreset = detectAccessPreset(rules, "post");
    const postPreset = postSelectionMode === "selected" && detectedPostPreset.mode === "headman" ? { ...detectedPostPreset, mode: "selected" as const } : detectedPostPreset;
    const canCreateAccess = canAccess && !legacy;
    const initialRules = topicRules(creationDraft.initialRules).filter(rule => rule.state !== "inherit");
    const creationTypeBlocked = !topicTemplates.some(template => template.template === fields.template) || (legacy && !["chat", "polls"].includes(fields.template));
    const creationBlocked = !!initialRules.length && !canCreateAccess || creationTypeBlocked;
    const createReadPreset = detectAccessPreset(initialRules, "read"), createDetectedPost = detectAccessPreset(initialRules, "post");
    const createPostPreset = creationDraft.postSelectionMode === "selected" && createDetectedPost.mode === "headman" ? { ...createDetectedPost, mode: "selected" as const } : createDetectedPost;
    const subjects = timetableSubjects(timetable.payload);
    function clearEditor() { editingRef.current = null; accessSequence.current++; setEditing(null); setFieldsState(emptyChannelFields()); setAccess(null); setAccessPreview(null); setRules([]); setRole(""); }
    function clearForbidden(error: unknown, purge = true) { if (purge)
        revokeGroupDrafts(error instanceof Error && error.message === "401" ? { owner } : { owner, community: communityId }); if (!alive())
        return; allRef.current = []; previewSequence.current++; previewTarget.current = null; setSpace(null); setArchive(null); setPreview(null); setParticipants([]); setCategoryTitle(""); setCategoryEdit(null); setQuery(""); clearEditor(); }
    function loadPreview(target: {
        userId: string | null;
        roleId: string | null;
    }) { const request = lease(), sequence = ++previewSequence.current; previewTarget.current = target; void api.previewPermissions(communityId, target).then(value => { if (!valid(request) || sequence !== previewSequence.current)
        return; const ids = new Set(allRef.current.map(topic => topic.topicId)); setPreview({ topics: value.topics.filter(topic => ids.has(topic.topicId)), roleId: target.roleId ?? "" }); }).catch(error => { if (valid(request) && sequence === previewSequence.current) {
        setPreview(null);
        previewTarget.current = null;
        onError(failure(error));
    } }); }
    useEffect(() => {
        const captured = { owner, community: communityId };
        const capturedLease = scopeLease(sessionStorage, captured);
        setStateScope(scopeId);
        setSpace(null);
        setArchive(null);
        setPreview(null);
        previewTarget.current = null;
        allRef.current = [];
        clearEditor();
        const source = createTopicAuthority(() => api.groupSpace(communityId), () => api.archivedTopics(communityId), async () => { const [page, desk] = await Promise.all([api.topics(communityId), api.groupDesk(communityId)]); return { topics: page.topics, categories: [], desk, capabilities: { maxRoles: 12, maxRolesPerMember: 3, maxTopics: 24, powers: [], templates: ["chat", "polls"] } }; }, value => {
            if (!alive())
                return;
            const permitted = new Set(value.all.map(topic => topic.topicId));
            for (const previous of allRef.current) {
                if (!permitted.has(previous.topicId))
                    revokeGroupDrafts({ owner, community: communityId, topic: previous.topicId ?? "general" });
            }
            reconcileGroupDrafts({ owner, community: communityId }, value.all.map(topic => topic.topicId));
            allRef.current = value.all;
            approveGroupDrafts({ owner, community: communityId });
            setSpace(value.space);
            setLegacy(value.legacy);
            setLoading(false);
            setArchive(current => current === null ? null : value.archived);
            const editor = editingRef.current;
            if (editor && editor !== "new") {
                const fresh = value.all.find(topic => topic.topicId === editor.topicId);
                if (!fresh || !topicAction(fresh, editingModeRef.current === "access" ? "access" : "channels", value.space.desk, value.legacy))
                    clearEditor();
            }
            setAccessPreview(current => { if (!current)
                return null; const fresh = value.all.find(topic => topic.topicId === current.value.topicId); return fresh && topicAction(fresh, "access", value.space.desk, value.legacy) && fresh.revision === current.value.revision ? current : null; });
            if (previewTarget.current) {
                setPreview(current => current ? { ...current, topics: [] } : null);
                if (globalPower(value.space.desk, "access"))
                    loadPreview(previewTarget.current);
                else {
                    previewTarget.current = null;
                    previewSequence.current++;
                    setPreview(null);
                }
            }
        }, error => { if (!alive())
            return; setLoading(false); if (error instanceof Error && ["401", "403", "404"].includes(error.message))
            clearForbidden(error, false); onError(failure(error)); }, error => { if (scopeLeaseValid(sessionStorage, captured, capturedLease))
            revokeGroupDrafts(error instanceof Error && error.message === "401" ? { owner } : captured); });
        authorityRef.current = source;
        void source.refresh();
        const timer = window.setInterval(() => void source.refresh(), 4000);
        return () => { source.dispose(); if (authorityRef.current === source)
            authorityRef.current = null; window.clearInterval(timer); };
    }, [scopeId]);
    async function run(action: () => Promise<unknown>) {
        if (busy || preview || !alive())
            return;
        const operationScope = { owner, community: communityId };
        const operationLease = scopeLease(sessionStorage, operationScope);
        setBusy(true);
        epoch.current++;
        authorityRef.current?.invalidate();
        try {
            await action();
            if (!alive() || !scopeLeaseValid(sessionStorage, operationScope, operationLease))
                return;
            if (legacy) {
                const [page, desk] = await Promise.all([api.topics(communityId), api.groupDesk(communityId)]);
                setSpace(current => current ? { ...current, topics: page.topics, desk } : current);
            }
            else
                await authorityRef.current?.refresh();
        }
        catch (error) {
            if (!alive() || !scopeLeaseValid(sessionStorage, operationScope, operationLease))
                return;
            onError(failure(error));
            if (error instanceof Error && error.message === "409") {
                await authorityRef.current?.refresh();
                const fresh = authorityRef.current?.current()?.space ?? null;
                if (fresh) {
                    setSpace(fresh);
                    if (editing && editing !== "new") {
                        const topic = fresh.topics.find(row => row.topicId === editing.topicId);
                        if (topic)
                            setEditing(topic);
                    }
                }
            }
        }
        finally {
            if (alive())
                setBusy(false);
        }
    }
    function edit(topic: GroupTopic | "new", mode: "full" | "access" = "full") {
        if (preview || (topic === "new" ? !canManage : !allowed(topic, mode === "access" ? "access" : "channels")))
            return;
        setEditingMode(mode);
        setAccessPreview(null);
        setAccess(null);
        setPostSelectionMode(null);
        editingRef.current = topic;
        setEditing(topic);
        setFields(topic === "new" ? creationDraft.fields : { title: topic.title, icon: topic.icon, template: topic.template || topic.kind, description: topic.description, categoryId: topic.categoryId || "", subject: topic.subject || "", position: topic.position || 0, accent: topic.accent, pinned: topic.pinned, writePolicy: topic.writePolicy });
    }
    async function save(event: FormEvent) {
        event.preventDefault();
        if (!editing || busy || preview || !canEditChannel || !scopeReady || editingMode !== "full" || editing === "new" && hasPendingCreation(creationKey))
            return;
        if (editing === "new" && creationBlocked) {
            onError("Создание с выбранным типом или начальными правилами доступа сейчас недоступно. Черновик сохранён.");
            return;
        }
        const durableCreation = readStoredDraft(sessionStorage, creationKey, () => ({ fields: emptyChannelFields(), initialRules: [] as AccessRule[], postSelectionMode: null as "selected" | null }));
        if (editing === "new" && JSON.stringify(creationDraft) !== JSON.stringify(durableCreation)) {
            setFieldsState(durableCreation.fields);
            setEditing(null);
            return;
        }
        const submittedCreation = durableCreation;
        const template = topicTemplates.find(row => row.template === fields.template);
        if (!template || fields.title.trim().length < 2 || (fields.template === "subject" && !subjects.includes(fields.subject)))
            return;
        const metadata = { description: fields.description, accent: fields.accent, pinned: canEditPin ? fields.pinned : activeTopic?.pinned ?? fields.pinned, writePolicy: canEditPolicy ? fields.writePolicy : activeTopic?.writePolicy ?? fields.writePolicy, categoryId: fields.categoryId || null, position: fields.position, subject: fields.subject || null };
        await run(async () => {
            if (editing === "new") {
                const pending = submitCreationOnce(creationKey, async () => { const result = await api.createTopic(communityId, fields.title.trim(), fields.icon, template.kind, legacy ? { description: fields.description, accent: "default", pinned: fields.pinned, writePolicy: fields.writePolicy } : { ...metadata, template: fields.template, ...(initialRules.length ? { initialAccessRules: initialRules } : {}) }); clearCreationDraft(submittedCreation); return result; });
                if (!pending)
                    return;
                await pending;
            }
            else if (editing.topicId)
                await api.renameTopic(communityId, editing.topicId, fields.title.trim(), fields.icon, editing.kind, legacy ? { description: fields.description, accent: "default", pinned: fields.pinned, writePolicy: fields.writePolicy } : { ...metadata, template: editing.template ?? null, expectedRevision: editing.revision ?? 0 });
            if (alive())
                setEditing(null);
        });
    }
    function editorLive(topicId: string, stamp: string) { return alive() && editingRef.current !== "new" && editingRef.current?.topicId === topicId && scopeLeaseValid(sessionStorage, { owner, community: communityId, topic: topicId }, stamp); }
    function requestAccess(topic: GroupTopic) { const request = lease(), sequence = ++accessSequence.current; void api.topicAccess(communityId, topic.topicId!).then(value => { if (valid(request) && sequence === accessSequence.current && editingRef.current !== "new" && editingRef.current?.topicId === topic.topicId) {
        setAccess(value);
        setRules(topicRules(value.rules));
    } }).catch(error => { if (valid(request))
        onError(failure(error)); }); }
    function openAccess(topic: GroupTopic) {
        if (preview || busy || !allowed(topic, "access"))
            return;
        edit(topic, "access");
        requestAccess(topic);
    }
    function pin(topic: GroupTopic) {
        if (preview || busy || !allowed(topic, "pin"))
            return;
        void run(() => api.renameTopic(communityId, topic.topicId!, topic.title, topic.icon, topic.kind, preservedPinMetadata(topic, !topic.pinned, legacy)));
    }
    function toggleCategory(id: string) { setCollapsed(current => { const next = { ...current, [id]: !current[id] }; localStorage.setItem(`zapara.topic.categories.${communityId}`, JSON.stringify(next)); return next; }); }
    const topics = scopeReady ? (preview?.topics ?? archive ?? space?.topics ?? []) : [];
    const visible = orderedTopics(topics).filter(topic => (!unread || topic.unread > 0) && (kind==="all"||topic.kind===kind) && noteSearch(query,topic.title,topic.description,topic.subject||"",space?.categories.find(category=>category.categoryId===topic.categoryId)?.title||""));
    const filtered = !!query.trim() || kind !== "all" || unread;
    const activeFilterCount = Number(!!query.trim()) + Number(kind !== "all") + Number(unread);
    const categories = [...(space?.categories ?? [])].sort((a, b) => a.position - b.position);
    const buckets = [{ id: "", title: "Каналы" }, ...categories.map(row => ({ id: row.categoryId, title: row.title }))];
    return <section className={"topics stack" + (editing ? " topic-editing" : "") + (toolsOpen ? " tools-open" : "")}>
    <div className="topic-head"><h2>Каналы группы</h2><div className="row topic-head-actions"><button className="btn quiet" type="button" aria-expanded={searchOpen} onClick={()=>setSearchOpen(value=>!value)}>Поиск{activeFilterCount?` · ${activeFilterCount}`:""}</button><button className="btn quiet" type="button" aria-expanded={toolsOpen} onClick={()=>setToolsOpen(value=>!value)}>Действия</button></div></div>
    <div className={"topic-search-panel" + (searchOpen ? " open" : "")}>
      <label className="field">Поиск доступных каналов<input type="search" value={scopeReady ? query : ""} onChange={event => setQuery(event.target.value)}/></label>
      <label className="field topic-kind-select">Тип канала<select value={kind} onChange={event=>setKind(event.target.value)}>{channelKindChoices.map(([value,title])=><option key={value} value={value}>{title}</option>)}</select></label>
      <div className="topic-kind-rail" role="group" aria-label="Тип канала">{channelKindChoices.map(([value,title])=><button key={value} className={kind===value?"btn primary":"btn"} type="button" aria-pressed={kind===value} onClick={()=>setKind(value)}>{title}</button>)}<button className={unread?"btn primary":"btn"} type="button" aria-pressed={unread} onClick={()=>setUnread(value=>!value)}>Непрочитанные</button></div>
      <div className="row topic-unread-desktop"><button className={unread ? "btn primary" : "btn quiet"} type="button" aria-pressed={unread} onClick={() => setUnread(value => !value)}>Непрочитанные</button></div>
    </div>
    {filtered&&<div className="row topic-filter-status" role="status"><span className="muted">Фильтры каналов · {activeFilterCount}; показано {visible.length} из {topics.length}{query.trim()?` · «${query.trim()}»`:""}</span><button className="btn quiet" type="button" onClick={()=>{setQuery("");setKind("all");setUnread(false);}}>Все доступные каналы</button></div>}
    <div className={"topic-tools-panel" + (toolsOpen ? " open" : "")}><div className="row"><button className="btn quiet" type="button" disabled={legacy} title={legacy ? "Сервер пока не поддерживает архив" : undefined} onClick={() => {
            if (archive)
                setArchive(null);
            else {
                setArchive(authorityRef.current?.current()?.archived ?? []);
                void authorityRef.current?.refresh();
            }
        }}>{archive ? "К каналам" : "Архив"}</button>{canManage && !preview && <button className="btn quiet" type="button" onClick={() => setManage(value => !value)} aria-expanded={manage}>Управление</button>}</div></div>
    {legacy && <p className="banner">Сервер поддерживает чаты и голосования. Категории, новые типы, доступ по ролям и архив станут доступны после обновления сервера.</p>}
    {loading && <p role="status">Загружаем пространство…</p>}
    {!space && !loading && <button className="btn" type="button" onClick={() => void api.groupSpace(communityId).then(setSpace).catch(error => onError(failure(error)))}>Повторить</button>}
    {scopeReady && preview && <div className="banner row" role="status"><span>Просмотр от лица {space?.desk.roles.find(row => row.roleId === preview.roleId)?.name || "участника"}. Запись выключена.</span><button className="btn" type="button" onClick={() => { previewSequence.current++; previewTarget.current = null; setPreview(null); }}>Выйти из просмотра</button></div>}
    {canAccess && !legacy && !preview && <details className="topic-preview-picker"><summary>Просмотр от лица роли или человека</summary><label className="field">Роль<select defaultValue="" onChange={event => {
                const roleId = event.target.value;
                if (roleId)
                    loadPreview({ roleId, userId: null });
            }}><option value="">Выберите роль</option>{space?.desk.roles.map(row => <option value={row.roleId} key={row.roleId}>{row.name}</option>)}</select></label><PreviewPerson communityId={communityId} onSelectUser={userId => loadPreview({ userId, roleId: null })} onError={onError}/></details>}
    {manage && canManage && !preview && <div className="card stack topic-management-panel"><button className="btn primary" type="button" disabled={busy || (space?.topics.length ?? 0) >= (space?.capabilities.maxTopics ?? 24)} onClick={() => edit("new")}>Создать канал</button><form className="row" onSubmit={event => { event.preventDefault(); void run(async () => { await api.saveCategory(communityId, null, categoryTitle, categories.length); setCategoryTitle(""); }); }}><input aria-label="Название категории" placeholder="Новая категория" value={categoryTitle} maxLength={40} onChange={event => setCategoryTitle(event.target.value)}/><button className="btn" disabled={legacy || busy || categoryTitle.trim().length < 2}>Добавить категорию</button></form>
      {categories.map(row => <div className="row" key={row.categoryId}><span>{row.title} · порядок {row.position}</span><button className="btn quiet" disabled={busy} type="button" onClick={() => {const old=space?.categories.find(item=>item.categoryId===categoryEdit?.id);if(categoryEdit&&old&&(categoryEdit.title!==old.title||categoryEdit.position!==String(old.position))&&!window.confirm("Отбросить правки текущей категории и открыть другую?"))return;setCategoryEdit({id:row.categoryId,title:row.title,position:String(row.position),revision:row.revision});}}>Изменить категорию</button><button className="btn quiet" type="button" disabled={busy} onClick={() => { const count=allRef.current.filter(topic=>topic.categoryId===row.categoryId).length; if(window.confirm(`Убрать категорию «${row.title}»? Каналы (${count}) сохранятся без категории.`)) void run(()=>api.deleteCategory(communityId,row.categoryId)); }}>Убрать категорию</button></div>)}
      {categoryEdit && <form className="stack card" onSubmit={event=>{event.preventDefault();const submitted=categoryEdit; if(!submitted.title.trim()||submitted.title.trim().length>40||submitted.position.trim()===""||!Number.isInteger(Number(submitted.position))||Number(submitted.position)<0||Number(submitted.position)>10000)return; void run(async()=>{await api.saveCategory(communityId,submitted.id,submitted.title.trim(),Number(submitted.position),submitted.revision);setCategoryEdit(current=>current===submitted?null:current);});}}><label className="field">Название категории<input required maxLength={40} disabled={busy} value={categoryEdit.title} onChange={event=>setCategoryEdit({...categoryEdit,title:event.target.value})}/></label><label className="field">Порядок категории<input type="number" required min={0} max={10000} step={1} disabled={busy} value={categoryEdit.position} onChange={event=>setCategoryEdit({...categoryEdit,position:event.target.value})}/></label><p className="muted">Меньший номер показывается выше. Черновик остаётся при ошибке сохранения.</p><div className="row"><button className="btn primary" disabled={busy}>Сохранить категорию</button><button className="btn quiet" type="button" disabled={busy} onClick={()=>{const old=space?.categories.find(row=>row.categoryId===categoryEdit.id);if(!old||old.title===categoryEdit.title&&String(old.position)===categoryEdit.position||window.confirm("Отбросить несохранённые изменения категории?"))setCategoryEdit(null);}}>Отмена</button></div></form>}

    </div>}
    {scopeReady && editing && !preview && <form className="card stack topic-editor" onSubmit={event => void save(event)}><div className="row"><h2>{editing === "new" ? "Создание канала" : editingMode === "access" ? "Доступ к каналу" : "Настройки канала"}</h2><button className="btn quiet" type="button" onClick={()=>{setEditing(null);setAccess(null);}}>К каналам</button></div>
      {editingMode === "access" && <button className="btn quiet" type="button" onClick={() => { setEditing(null); setAccess(null); setAccessPreview(null); }}>Закрыть доступ</button>}
      {editingMode === "full" && <>
      {editing === "new" && <div className="template-grid">{topicTemplates.filter(template => !legacy || ["chat", "polls"].includes(template.template)).map(template => <button className={fields.template === template.template ? "channel-kind selected" : "channel-kind"} type="button" key={template.template} aria-pressed={fields.template === template.template} onClick={() => setFields(current => ({ ...current, template: template.template, icon: template.icon, subject: ["subject", "homework", "schedule"].includes(template.template) ? current.subject : "", writePolicy: template.template === "announcements" ? "managers" : "all" }))}><b>{template.icon} {template.title}</b><small>{template.description}</small></button>)}</div>}
      <label className="field">Название<input value={fields.title} maxLength={40} required minLength={2} onChange={event => setFields(current => ({ ...current, title: event.target.value }))}/></label><label className="field">Значок<input value={fields.icon} maxLength={8} onChange={event => setFields(current => ({ ...current, icon: event.target.value }))}/></label><label className="field">Описание<textarea value={fields.description} maxLength={240} onChange={event => setFields(current => ({ ...current, description: event.target.value }))}/></label>
      <label className="field">Категория<select value={fields.categoryId} onChange={event => setFields(current => ({ ...current, categoryId: event.target.value }))}><option value="">Без категории</option>{categories.map(row => <option value={row.categoryId} key={row.categoryId}>{row.title}</option>)}</select></label>
      {(fields.template === "subject" || fields.template === "schedule" || fields.template === "homework") && <label className="field">Предмет<select required={fields.template === "subject"} value={fields.subject} onChange={event => setFields(current => ({ ...current, subject: event.target.value }))}><option value="">Все предметы</option>{subjects.map(row => <option key={row}>{row}</option>)}</select></label>}
      {(fields.template === "subject" || fields.template === "schedule" || fields.template === "homework") && <div className="row"><span className="muted">{timetable.error || (timetable.loading ? "Загружаем предметы группы…" : `Предметы группы ${groupName || "не указана"}`)}</span>{timetable.error && <button className="btn quiet" type="button" disabled={timetable.loading} onClick={timetable.reload}>Повторить загрузку предметов</button>}</div>}
      <label className="field">Порядок<input type="number" min="0" max="100" value={fields.position} onChange={event => setFields(current => ({ ...current, position: +event.target.value }))}/></label>
      {editing === "new" && <section className="card stack"><h2>Просмотр и публикация при создании</h2><p className="muted">Канал и начальные правила создаются одним запросом. Ограничения типа и политики публикации проверяет сервер.</p>{!canCreateAccess && <p className="muted">Для начальных правил нужны права управления доступом всей группы. На прежнем сервере эта возможность недоступна.</p>}{([["read", createReadPreset, "Кто видит новый канал"], ["post", createPostPreset, "Кто публикует в новом канале"]] as const).map(([power, preset, label]) => <div className="stack" key={power}><label className="field">{label}<select aria-label={label} value={preset.mode} disabled={!canCreateAccess} onChange={event => {
                            if (!canCreateAccess)
                                return;
                            if (power === "post")
                                setCreationField("postSelectionMode", event.target.value === "selected" ? "selected" : null);
                            setCreationField("initialRules", current => applyAccessPreset(current, power, { mode: event.target.value as typeof preset.mode, roles: preset.roles }));
                        }}><option value="all">{power === "read" ? "Все участники" : "Все с нужным правом"}</option><option value="selected">Выбранные роли</option>{power === "post" && <option value="headman">Только староста</option>}<option value="custom">По действующим базовым правилам</option></select></label>{preset.mode === "selected" && <><p className="muted">Без выбранных ролей обычные участники не получают это действие. Старосту защищает сервер.</p>{space?.desk.roles.map(item => <label className="check" key={item.roleId}><input type="checkbox" disabled={!canCreateAccess} aria-label={`${label}: ${item.name}`} checked={preset.roles.includes(item.roleId)} onChange={event => {
                                    if (!canCreateAccess)
                                        return;
                                    const roles = event.target.checked ? [...preset.roles, item.roleId] : preset.roles.filter(id => id !== item.roleId);
                                    setCreationField("initialRules", current => applyAccessPreset(current, power, { mode: "selected", roles }));
                                }}/>{item.name}</label>)}</>}</div>)}{creationTypeBlocked && <p role="alert">Тип сохранён в черновике, но недоступен на этом сервере. Выберите поддерживаемый тип.</p>}{initialRules.length > 0 && !canCreateAccess && <p role="alert">Начальные правила сохранены в черновике. Создание по ролям сейчас недоступно; публичный канал вместо закрытого не будет создан.</p>}{initialRules.length > 0 && <button className="btn quiet" type="button" onClick={() => {
                            if (window.confirm("Убрать начальные правила? Новый канал будет создан по действующим базовым правилам группы."))
                                setCreationField("initialRules", []);
                        }}>Убрать начальные правила</button>}</section>}
      <label className="check"><input type="checkbox" checked={fields.pinned} disabled={!canEditPin} onChange={event => setFields(current => ({ ...current, pinned: event.target.checked }))}/>Закрепить для группы</label><label className="field">Публикация<select value={fields.writePolicy} disabled={!canEditPolicy} onChange={event => setFields(current => ({ ...current, writePolicy: event.target.value as "all" | "managers" }))}><option value="all">Все с нужным правом</option><option value="managers">Управляющие каналами</option></select></label><p className="muted">Тип существующего канала сохраняется. Для другого типа создайте новый канал.</p><div className="row"><button className="btn primary" disabled={busy || !canEditChannel || editing === "new" && (creationBlocked || creationPending)}>{busy ? "Сохраняем…" : "Сохранить"}</button><button className="btn quiet" type="button" onClick={() => { setEditing(null); setAccess(null); }}>Отмена</button></div>
      </>}
      {!legacy && editing !== "new" && editing.topicId && <>{canEditChannel && <button className="btn" type="button" disabled={busy} onClick={() => {
                        if (!canEditChannel || preview)
                            return;
                        void run(async () => { const id = editing.topicId!, stamp = scopeLease(sessionStorage, { owner, community: communityId, topic: id }); await api.archiveTopic(communityId, id, !editing.archived, editing.revision ?? 0); if (editorLive(id, stamp)) {
                            setEditing(null);
                            setArchive(null);
                        } });
                    }}>{editing.archived ? "Восстановить канал" : "Архивировать канал"}</button>}{canEditAccess && <button className="btn" type="button" onClick={() => { if (activeTopic)
                requestAccess(activeTopic); }}>Доступ к каналу</button>}
      {access && canEditAccess && <section className="stack"><h2>Доступ и действия</h2>{([["read", readPreset, "Кто видит канал"], ["post", postPreset, "Кто публикует"]] as const).map(([power, preset, label]) => <div className="stack" key={power}><label className="field">{label}<select aria-label={label} value={preset.mode} onChange={event => {
                            if (power === "post")
                                setPostSelectionMode(event.target.value === "selected" ? "selected" : null);
                            setRules(current => applyAccessPreset(current, power, { mode: event.target.value as typeof preset.mode, roles: preset.roles }));
                        }}><option value="all">{power === "read" ? "Все участники" : "Все с нужным правом"}</option><option value="selected">Выбранные роли</option>{power === "post" && <option value="headman">Только староста</option>}<option value="custom">По расширенным правилам</option></select></label>{preset.mode === "selected" && <><p className="muted">Если роли не выбраны, обычные участники не получают это действие. Староста защищён сервером.</p>{space?.desk.roles.map(item => <label className="check" key={item.roleId}><input type="checkbox" aria-label={`${label}: ${item.name}`} checked={preset.roles.includes(item.roleId)} onChange={event => { const roles = event.target.checked ? [...preset.roles, item.roleId] : preset.roles.filter(id => id !== item.roleId); setRules(current => applyAccessPreset(current, power, { mode: "selected", roles })); }}/>{item.name}</label>)}</>}</div>)}<details open={readPreset.mode === "custom" || postPreset.mode === "custom"}><summary>Расширенные правила</summary><p className="muted">Староста сохраняет управление. Категории не меняют права. Настройки применяются сервером.</p><label className="field">Для кого<select value={role} onChange={event => setRole(event.target.value)}><option value="">Все участники</option>{space?.desk.roles.map(row => <option value={row.roleId} key={row.roleId}>{row.name}</option>)}</select></label>{(space?.capabilities.powers ?? []).filter(power => !groupOnlyPowers.has(power)).map(power => <label className="access-line" key={power}><span>{powerTitles[power] || power}</span><select aria-label={`${powerTitles[power] || power}: ${role || "все участники"}`} value={rules.find(row => row.roleId === (role || null) && row.power === power)?.state || "inherit"} onChange={event => {
                            if (power === "post")
                                setPostSelectionMode(null);
                            setRules(current => [...current.filter(row => row.roleId !== (role || null) || row.power !== power), { roleId: role || null, power, state: event.target.value as AccessRule["state"] }]);
                        }}><option value="inherit">Наследовать</option><option value="allow">Разрешить</option><option value="deny">Запретить</option></select></label>)}</details><p className="muted">Правил: {rules.filter(row => row.state !== "inherit").length}. Перед сохранением сервер покажет итоговые права и кого затронет изменение.</p><button className="btn" type="button" disabled={busy || !canEditAccess} onClick={() => {
                        const signature = accessSignature, request = lease(), sequence = ++accessSequence.current;
                        setBusy(true);
                        void api.previewTopicAccess(communityId, access.topicId, outgoingRules, access.revision).then(value => { if (valid(request) && sequence === accessSequence.current)
                            setAccessPreview({ signature, value }); }).catch(async (error) => {
                            if (error instanceof Error && error.message === "409")
                                await api.topicAccess(communityId, access.topicId).then(value => { if (valid(request) && sequence === accessSequence.current)
                                    setAccess(value); }).catch(() => undefined);
                            onError(failure(error));
                        }).finally(() => setBusy(false));
                    }}>{busy ? "Проверяем последствия…" : "Проверить последствия"}</button>
        {preparedAccess && <div className="card stack"><h2>Последствия по данным сервера</h2><p>Затронет участников: {preparedAccess.affectedCount}</p><p className="muted">Просмотр: {preparedAccess.beforeReaders.length} → {preparedAccess.afterReaders.length}</p><p>Получат просмотр: {preparedAccess.afterReaders.filter(id => !preparedAccess.beforeReaders.includes(id)).map(personName).join(", ") || "никто"}</p><p>Потеряют просмотр: {preparedAccess.beforeReaders.filter(id => !preparedAccess.afterReaders.includes(id)).map(personName).join(", ") || "никто"}</p>{preparedAccess.participants.map(person => <details key={person.userId}><summary>{personName(person.userId)}</summary><p>До: {person.beforePermissions.map(power => powerTitles[power] || power).join(", ") || "нет действий"}</p><p>После: {person.afterPermissions.map(power => powerTitles[power] || power).join(", ") || "нет действий"}</p>{Object.entries(person.sources).map(([power, source]) => <p className="muted" key={power}>{powerTitles[power] || power}: {source}</p>)}</details>)}<p className="muted">Окончательное разрешение проверяет сервер при сохранении. Новое изменение формы требует повторной проверки.</p></div>}
        <button className="btn primary" type="button" disabled={busy || !preparedAccess || !canEditAccess} onClick={() => {
                        if (!preparedAccess || !canEditAccess || preview)
                            return;
                        if (!window.confirm(`Сохранить доступ? Изменение затронет ${preparedAccess.affectedCount} участников. Просмотр: ${preparedAccess.beforeReaders.length} → ${preparedAccess.afterReaders.length}.`))
                            return;
                        void run(async () => {
                            const id = access.topicId, stamp = scopeLease(sessionStorage, { owner, community: communityId, topic: id });
                            try {
                                await api.saveTopicAccess(communityId, access.topicId, outgoingRules, access.revision);
                            }
                            catch (error) {
                                if (error instanceof Error && error.message === "409" && editorLive(id, stamp)) {
                                    setAccessPreview(null);
                                    const latest = await api.topicAccess(communityId, access.topicId).catch(() => null);
                                    if (latest && editorLive(id, stamp))
                                        setAccess(latest);
                                    else
                                        throw new Error("access-refresh-failed");
                                }
                                throw error;
                            }
                            if (editorLive(id, stamp)) {
                                setAccess(null);
                                setAccessPreview(null);
                            }
                        });
                    }}>Сохранить проверенный доступ</button></section>}</>}
    </form>}
    {!loading && visible.length === 0 && <p className="empty">{query || unread ? "Доступных каналов по запросу нет" : archive ? "Архив пуст" : "Каналов пока нет"}</p>}
    {buckets.map(bucket => {
      const rows = visible.filter(topic => (topic.categoryId || "") === bucket.id);
      return rows.length ? <div className="topic-list" key={bucket.id}>
        <button className="btn quiet topic-category" type="button" aria-expanded={!!query.trim() || !collapsed[bucket.id]} disabled={!!query.trim()} title={query.trim() ? "При поиске категории раскрыты. Очистите поиск, чтобы свернуть." : undefined} onClick={() => toggleCategory(bucket.id)}>{bucket.title}</button>
        {(!!query.trim() || !collapsed[bucket.id]) && rows.map(topic => <div className="topic" key={topic.topicId || "general"}>
          <button type="button" className="topic-open" disabled={!!preview} title={preview ? "В просмотре доступны только сведения о каналах. Выйдите, чтобы открыть канал." : undefined} onClick={() => onOpen(topic, canManage)}><TopicMark topic={topic}/><span className="topic-main"><b>{topic.title}{topic.pinned && <Icon name="pin" size={14}/>}</b><span className="preview">{topicPreview(topic)}</span></span><span className="topic-meta">{topic.lastAt && new Date(topic.lastAt).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })}{topic.unread > 0 && <span className="chip" aria-label={unreadBadgeDescription(topic.unread)}>{unreadBadgeText(topic.unread)}</span>}</span></button>
          {!preview && (allowed(topic, "channels") || allowed(topic, "access") || allowed(topic, "pin")) && <details className="topic-row-actions"><summary>Действия канала</summary><div className="row">
            {allowed(topic, "channels") && <button className="btn quiet" type="button" onClick={() => edit(topic)}>Настройки</button>}
            {allowed(topic, "access") && <button className="btn quiet" type="button" disabled={busy} onClick={() => openAccess(topic)}>Доступ</button>}
            {allowed(topic, "pin") && <button className="btn quiet" type="button" disabled={busy} onClick={() => pin(topic)}>{topic.pinned ? "Открепить" : "Закрепить"}</button>}
          </div></details>}
          {preview && <p className="muted">Действия: {(topic.permissions ?? []).map(power => powerTitles[power] || power).join(", ") || "только просмотр"}</p>}
        </div>)}
      </div> : null;
    })}
  </section>;
}
function PreviewPerson({ communityId, onSelectUser, onError }: {
    communityId: string;
    onSelectUser: (userId: string) => void;
    onError: (text: string) => void;
}) {
    const [people, setPeople] = useState<import("./types").Classmate[]>([]);
    useEffect(() => {
        let stop = false;
        void api.groupHome(communityId).then(home => {
            if (!stop)
                setPeople(home.classmates);
        }).catch(() => {
            if (!stop)
                onError("Участники для просмотра не загрузились");
        });
        return () => { stop = true; };
    }, [communityId]);
    return <label className="field">Человек<select defaultValue="" onChange={event => {
            const userId = event.target.value;
            if (userId)
                onSelectUser(userId);
        }}><option value="">Выберите участника</option>{people.map(person => <option value={person.userId} key={person.userId}>{person.displayName || person.username}</option>)}</select></label>;
}
