import type { Group, GroupsPayload, TimetablePayload } from "./types";
export type CommunityTimetableState = { groupName: string | null; groupId: string | null; payload: TimetablePayload | null; loading: boolean; error: string };
export function resolveAcademicGroup(name: string, groups: Group[]): Group | null {
    const key = (value: string) => value.trim().toLocaleUpperCase("ru-RU").replace(/[ \t\r\n]+/g, "");
    const matches = groups.filter(group => key(group.name) === key(name));
    return matches.length === 1 ? matches[0] : null;
}
export function timetableSubjects(payload: TimetablePayload | null): string[] {
    return [...new Set(payload?.lessons.map(lesson => lesson.subjectRaw).filter(Boolean) ?? [])].sort((a,b) => a.localeCompare(b,"ru"));
}
export function createCommunityTimetableLoader(groupName: string | null, dependencies: {
    catalog: GroupsPayload | null;
    loadGroups: (signal: AbortSignal) => Promise<GroupsPayload>;
    loadTimetable: (groupId: string, signal: AbortSignal) => Promise<TimetablePayload>;
    cached: (groupId: string) => TimetablePayload | null;
    save: (groupId: string, payload: TimetablePayload) => void;
}, publish: (state: CommunityTimetableState) => void) {
    let disposed = false, generation = 0, pending: AbortController | null = null;
    let current: CommunityTimetableState = { groupName, groupId: null, payload: null, loading: false, error: "" };
    const put = (next: CommunityTimetableState) => { current = next; publish(next); };
    async function reload() {
        if (disposed) return;
        pending?.abort(); const controller = new AbortController(); pending = controller; const ticket = ++generation;
        const valid = () => !disposed && ticket === generation && !controller.signal.aborted;
        if (!groupName) { put({ ...current, loading: false, error: "Учебная группа сообщества не указана" }); return; }
        let group = resolveAcademicGroup(groupName, dependencies.catalog?.groups ?? []);
        const cachedValue = group ? dependencies.cached(group.id) : null;
        const cached = cachedValue?.group.id === group?.id ? cachedValue : null;
        put({ groupName, groupId: group?.id ?? null, payload: current.payload?.group.id === group?.id ? current.payload : cached, loading: true, error: "" });
        try {
            if (!group) { const catalog = await dependencies.loadGroups(controller.signal); if (!valid()) return; group = resolveAcademicGroup(groupName, catalog.groups); }
            if (!group) throw new Error("unknown-group");
            const previousValue = current.payload?.group.id === group.id ? current.payload : dependencies.cached(group.id);
            const previous = previousValue?.group.id === group.id ? previousValue : null;
            put({ ...current, groupId: group.id, payload: previous, loading: true });
            const payload = await dependencies.loadTimetable(group.id, controller.signal);
            if (!valid()) return;
            if (payload.group.id !== group.id) throw new Error("wrong-group");
            dependencies.save(group.id, payload);
            put({ groupName, groupId: group.id, payload, loading: false, error: "" });
        } catch (error) {
            if (!valid()) return;
            put({ ...current, loading: false, error: error instanceof Error && error.message === "unknown-group" ? "Учебная группа не найдена в каталоге" : current.payload ? "Расписание не обновилось. Показана сохранённая копия группы." : "Расписание группы не загрузилось. Попробуйте ещё раз." });
        }
    }
    return { reload, dispose() { disposed = true; generation++; pending?.abort(); } };
}
