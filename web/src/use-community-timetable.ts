import { useEffect, useRef, useState } from "react";
import * as api from "./api";
import { useApp } from "./store";
import { createCommunityTimetableLoader, type CommunityTimetableState } from "./community-timetable";
import type { TimetablePayload } from "./types";
const transientKey = "zapara.community.timetables.v1";
function readTransient(): Record<string, TimetablePayload> { try { return JSON.parse(localStorage.getItem(transientKey) || "{}"); } catch { return {}; } }
export function useCommunityTimetable(groupName: string | null) {
    const app = useApp();
    const [state, setState] = useState<CommunityTimetableState>({ groupName, groupId: null, payload: null, loading: !!groupName, error: "" });
    const loader = useRef<ReturnType<typeof createCommunityTimetableLoader> | null>(null);
    useEffect(() => {
        const source = createCommunityTimetableLoader(groupName, {
            catalog: app.catalog ?? api.readCache().groups ?? Object.values(readTransient())[0] ?? null,
            loadGroups: signal => api.loadGroups(signal),
            loadTimetable: (id,signal) => api.loadTimetable(id,signal),
            cached: id => readTransient()[id] ?? api.readCache().lessons[id] ?? null,
            save: (id,payload) => localStorage.setItem(transientKey, JSON.stringify({ ...readTransient(), [id]: payload }))
        }, setState);
        loader.current = source; void source.reload();
        return () => { source.dispose(); if(loader.current === source) loader.current = null; };
    }, [groupName, app.catalog]);
    return { ...(state.groupName === groupName ? state : { groupName, groupId: null, payload: null, loading: !!groupName, error: "" }), reload: () => void loader.current?.reload() };
}
