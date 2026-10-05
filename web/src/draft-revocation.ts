import type { DraftStorage } from "./draft-store";
export type GroupDraftScope = {
    owner: string;
    community: string;
    topic?: string;
};
const generationsKey = "zapara.group.revoke-generations", keysKey = "zapara.group.draft-keys", scopesKey = "zapara.group.chat-scopes", conversationsKey = "zapara.group.conversations";
const parse = <T>(storage: DraftStorage, key: string, fallback: T): T => { try {
    return JSON.parse(storage.getItem(key) || "null") ?? fallback;
}
catch {
    return fallback;
} };
const scopeId = (scope: GroupDraftScope) => `${scope.owner}:${scope.community}${scope.topic === undefined ? "" : ":" + scope.topic}`;
export function draftScopeFromKey(key: string): GroupDraftScope | null { if (!key.startsWith("zapara.draft.v2:"))
    return null; const [owner, community, topic] = key.slice("zapara.draft.v2:".length).split(":"); return owner && community && topic ? { owner, community, topic } : null; }
export function scopeLease(storage: DraftStorage, scope: GroupDraftScope): string { const rows = parse<Record<string, number>>(storage, generationsKey, {}); return JSON.stringify([rows[scope.owner] ?? 0, rows[`${scope.owner}:${scope.community}`] ?? 0, scope.topic === undefined ? 0 : rows[scopeId(scope)] ?? 0]); }
export const scopeLeaseValid = (storage: DraftStorage, scope: GroupDraftScope, lease: string) => scopeLease(storage, scope) === lease;
export function registerDraftKey(storage: DraftStorage, key: string) { if (!draftScopeFromKey(key))
    return; const keys = parse<string[]>(storage, keysKey, []); if (!keys.includes(key))
    storage.setItem(keysKey, JSON.stringify([...keys, key])); }
export function registerGroupConversation(storage: DraftStorage, owner: string, community: string, conversation: string) { const rows = parse<Record<string, string[]>>(storage, conversationsKey, {}), id = `${owner}:${community}`; if (!(rows[id] ?? []).includes(conversation))
    storage.setItem(conversationsKey, JSON.stringify({ ...rows, [id]: [...(rows[id] ?? []), conversation] })); }
export function registerGroupChatDraft(storage: DraftStorage, key: string, scope: GroupDraftScope) { const rows = parse<Record<string, GroupDraftScope>>(storage, scopesKey, {}); storage.setItem(scopesKey, JSON.stringify({ ...rows, [key]: scope })); const conversation = key.split(":")[1]; if (conversation)
    registerGroupConversation(storage, scope.owner, scope.community, conversation); }
export function purgeGroupDrafts(storage: DraftStorage, scope: GroupDraftScope | {
    owner: string;
}, notify?: (key: string) => void) {
    const rows = parse<Record<string, number>>(storage, generationsKey, {}), generationId = "community" in scope ? scopeId(scope) : scope.owner;
    storage.setItem(generationsKey, JSON.stringify({ ...rows, [generationId]: (rows[generationId] ?? 0) + 1 }));
    const matches = (candidate: GroupDraftScope) => candidate.owner === scope.owner && (!("community" in scope) || candidate.community === scope.community && (scope.topic === undefined || candidate.topic === scope.topic));
    const all = new Set(parse<string[]>(storage, keysKey, []));
    const enumerable = storage as DraftStorage & {
        length?: number;
        key?: (index: number) => string | null;
    };
    if (enumerable.key)
        for (let i = 0; i < (enumerable.length ?? 0); i++) {
            const key = enumerable.key(i);
            if (key)
                all.add(key);
        }
    for (const key of all) {
        const candidate = draftScopeFromKey(key);
        if (candidate && matches(candidate)) {
            storage.removeItem(key);
            notify?.(key);
        }
    }
    const registered = parse<Record<string, GroupDraftScope>>(storage, scopesKey, {}), conversations = parse<Record<string, string[]>>(storage, conversationsKey, {});
    for (const name of ["zapara.group.drafts", "zapara.group.contexts", "zapara.group.draft-epochs"]) {
        const values = parse<Record<string, unknown>>(storage, name, {});
        let changed = false;
        for (const key of Object.keys(values)) {
            const [owner, conversation, topic] = key.split(":");
            if (topic === "direct")
                continue;
            const known = registered[key];
            const belongs = known ? matches(known) : owner === scope.owner && (!("community" in scope) || (conversations[`${scope.owner}:${scope.community}`] ?? []).includes(conversation) && (scope.topic === undefined || topic === scope.topic));
            if (belongs) {
                delete values[key];
                delete registered[key];
                changed = true;
            }
        }
        if (changed) {
            storage.setItem(name, JSON.stringify(values));
            notify?.(name);
        }
    }
    storage.setItem(scopesKey, JSON.stringify(registered));
    if ("community" in scope && scope.topic === undefined)
        storage.removeItem(`zapara.group.selection.${scope.owner}:${scope.community}`);
}
export function emptyDraftValue<T>(value: T): T { if (Array.isArray(value))
    return [] as T; if (value && typeof value === "object")
    return Object.fromEntries(Object.entries(value).map(([key, entry]) => [key, emptyDraftValue(entry)])) as T; return (typeof value === "string" ? "" : typeof value === "boolean" ? false : typeof value === "number" ? 0 : null) as T; }
export function reconcileGroupDraftScopes(storage: DraftStorage, scope: {
    owner: string;
    community: string;
}, topics: (string | null)[], notify?: (key: string) => void) { const allowed = new Set(topics.map(topic => topic ?? "general")), registered = parse<Record<string, GroupDraftScope>>(storage, scopesKey, {}), ids = new Set<string>(); for (const candidate of Object.values(registered))
    if (candidate.owner === scope.owner && candidate.community === scope.community && candidate.topic && candidate.topic !== "new" && !allowed.has(candidate.topic))
        ids.add(candidate.topic); const keys = new Set(parse<string[]>(storage, keysKey, [])); const enumerable = storage as DraftStorage & {
    length?: number;
    key?: (index: number) => string | null;
}; if (enumerable.key)
    for (let i = 0; i < (enumerable.length ?? 0); i++) {
        const key = enumerable.key(i);
        if (key)
            keys.add(key);
    } for (const key of keys) {
    const candidate = draftScopeFromKey(key);
    if (candidate && candidate.owner === scope.owner && candidate.community === scope.community && candidate.topic && candidate.topic !== "new" && !allowed.has(candidate.topic))
        ids.add(candidate.topic);
} for (const topic of ids)
    purgeGroupDrafts(storage, { ...scope, topic }, notify); }
