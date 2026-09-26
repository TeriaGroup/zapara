import { createContext, ReactNode, useContext, useEffect, useMemo, useRef, useState } from "react";
import * as api from "./api";
import { usePrivateHomework } from "./private-sync";
import { resolveStoredGroup } from "./groupChoice";

import type { FriendItem, GroupsPayload, HomeworkItem, Lesson, Session, TimetablePayload } from "./types";

type State = {
  theme: "light" | "dark" | "system";
  setTheme: (theme: "light" | "dark" | "system") => void;
  animations: boolean;
  setAnimations: (value: boolean) => void;
  invert: boolean;
  setInvert: (value: boolean) => void;
  groupId: string;
  setGroupId: (id: string) => void;
  catalog: GroupsPayload | null;
  lessons: Lesson[];
  timetableAvailable: boolean;
  timetableLoading: boolean;
  timetableFailed: boolean;
  notice: string;
  loading: boolean;
  refresh: () => void;
  session: Session | null;
  refreshSession: () => Promise<void>;
  privateHomework: ReturnType<typeof usePrivateHomework>;
  homework: HomeworkItem[];
  saveHomework: (item: HomeworkItem) => void;
  friends: FriendItem[];
  saveFriends: (items: FriendItem[]) => void;
  intersectionStrictness: number;
  setIntersectionStrictness: (value: number) => void;
  showAbsentFriends: boolean;
  setShowAbsentFriends: (value: boolean) => void;
  date: Date;
  setDate: (date: Date) => void;
  subgroups: Record<string, Record<string, string>>;
  pickSubgroup: (streamId: string, optionId: string) => void;
};

const Ctx = createContext<State | null>(null);
const groupKey = "zapara.group";
const invertKey = "zapara.invert";

const friendsKey = "zapara.friends";
const intersectionStrictnessKey = "zapara.intersectionStrictness";
const showAbsentFriendsKey = "zapara.showAbsentFriends";
const subgroupKey = "zapara.subgroups";

function readList<T>(key: string): T[] {
  try { return JSON.parse(localStorage.getItem(key) || "[]") as T[]; } catch { return []; }
}

export function Provider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<"light" | "dark" | "system">(() => { const saved = localStorage.getItem("zapara.theme"); return saved === "light" || saved === "dark" ? saved : "system"; });
  const [animations, setAnimations] = useState(localStorage.getItem("zapara.animations") !== "0");
  const [invert, setInvertState] = useState(localStorage.getItem(invertKey) === "1");
  const [groupId, setGroupState] = useState(() => localStorage.getItem(groupKey) ?? "");
  const groupReady = useRef(localStorage.getItem(groupKey) !== null);
  const [catalog, setCatalog] = useState<GroupsPayload | null>(api.readCache().groups ?? null);
  const [bundle, setBundle] = useState<{ groupId: string; payload: TimetablePayload } | null>(() => {
    const cached = groupId ? api.readCache().lessons[groupId] : null;
    return cached ? { groupId, payload: cached } : null;
  });
  const [timetableStatus, setTimetableStatus] = useState<{ groupId: string; loading: boolean; failed: boolean }>({ groupId, loading: !!groupId, failed: false });
  const [notice, setNotice] = useState("");
  const [loading, setLoading] = useState(false);
  const [tick, setTick] = useState(0);
  const [session, setSession] = useState<Session | null>(null);
  const privateHomework = usePrivateHomework(session?.authenticated ? session.user?.userId ?? null : null);
  const homework = privateHomework.items;
  const [friends, setFriends] = useState<FriendItem[]>(() => readList(friendsKey));
  const [intersectionStrictness, setIntersectionStrictness] = useState(() => {
    const stored = Number(localStorage.getItem(intersectionStrictnessKey));
    return [25, 50, 75, 100].includes(stored) ? stored : 25;
  });
  const [showAbsentFriends, setShowAbsentFriends] = useState(localStorage.getItem(showAbsentFriendsKey) === "1");
  const [date, setDateState] = useState(() => { const now = new Date(); return new Date(now.getFullYear(), now.getMonth(), now.getDate()); });
  const setDate = (value: Date) => setDateState(new Date(value.getFullYear(), value.getMonth(), value.getDate()));
  const [subgroups, setSubgroups] = useState<Record<string, Record<string, string>>>(() => {
    try { return JSON.parse(localStorage.getItem(subgroupKey) || "{}"); } catch { return {}; }
  });

  useEffect(() => {
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const update = () => { document.documentElement.dataset.theme = theme === "system" ? media.matches ? "dark" : "light" : theme; };
    update(); localStorage.setItem("zapara.theme", theme);
    media.addEventListener("change", update);
    return () => media.removeEventListener("change", update);
  }, [theme]);
  useEffect(() => { localStorage.setItem("zapara.animations", animations ? "1" : "0"); document.documentElement.dataset.motion = animations ? "on" : "off"; }, [animations]);
  useEffect(() => { localStorage.setItem(invertKey, invert ? "1" : "0"); }, [invert]);
  useEffect(() => { if (groupReady.current) localStorage.setItem(groupKey, groupId); }, [groupId]);
  const chooseGroup = (id: string) => {
    groupReady.current = true;
    localStorage.setItem(groupKey, id);
    const cached = id ? api.readCache().lessons[id] : null;
    setBundle(cached ? { groupId: id, payload: cached } : null);
    setTimetableStatus({ groupId: id, loading: !!id, failed: false });
    setNotice(catalog?.meta.stale ? "Расписание может быть устаревшим. Показана сохранённая копия." : "");
    setGroupState(id);
    if (session?.authenticated) privateHomework.saveSettings({ selectedGroupId: id || null });
  };

  useEffect(() => { localStorage.setItem(friendsKey, JSON.stringify(friends)); }, [friends]);
  useEffect(() => { localStorage.setItem(intersectionStrictnessKey, String(intersectionStrictness)); }, [intersectionStrictness]);
  useEffect(() => { localStorage.setItem(showAbsentFriendsKey, showAbsentFriends ? "1" : "0"); }, [showAbsentFriends]);
  useEffect(() => { localStorage.setItem(subgroupKey, JSON.stringify(subgroups)); }, [subgroups]);

  useEffect(() => {
    let stop = false;
    setLoading(true);
    api.loadGroups().then(payload => {
      if (stop) return;
      setCatalog(payload);
      const cache = api.readCache();
      cache.groups = payload;
      api.writeCache(cache);
      const next = resolveStoredGroup(groupReady.current ? localStorage.getItem(groupKey) : null, payload.groups);
      const cached = next ? api.readCache().lessons[next] : null;
      setBundle(cached ? { groupId: next, payload: cached } : null);
      setTimetableStatus({ groupId: next, loading: !!next, failed: false });
      if (!groupReady.current) {
        groupReady.current = true;
        localStorage.setItem(groupKey, next);
      }
      setGroupState(next);
      setNotice(payload.meta.stale ? "Расписание может быть устаревшим. Показана сохранённая копия." : "");
    }).catch(() => {
      if (stop) return;
      setNotice(catalog ? "Расписание не обновилось. Доступна сохранённая копия." : "Нет сети и сохранённой копии.");
    }).finally(() => { if (!stop) setLoading(false); });
    return () => { stop = true; };
  }, [tick]);

  useEffect(() => {
    if (!groupId) {
      setBundle(null);
      setTimetableStatus({ groupId: "", loading: false, failed: false });
      return;
    }
    let stop = false;
    const cached = api.readCache().lessons[groupId];
    setBundle(cached ? { groupId, payload: cached } : null);
    setTimetableStatus({ groupId, loading: true, failed: false });
    api.loadTimetable(groupId).then(payload => {
      if (stop) return;
      setBundle({ groupId, payload });
      setTimetableStatus({ groupId, loading: false, failed: false });
      const cache = api.readCache();
      cache.lessons[groupId] = payload;
      if (payload.period) cache.groups = { period: payload.period, meta: payload.meta, groups: cache.groups?.groups || catalog?.groups || [] };
      api.writeCache(cache);
    }).catch(() => {
      if (stop) return;
      setTimetableStatus({ groupId, loading: false, failed: true });
      if (cached) setNotice("Расписание не обновилось. Доступна сохранённая копия.");
    });
    return () => { stop = true; };
  }, [groupId, tick]);

  useEffect(() => { void api.session().then(setSession).catch(() => setSession(null)); }, []);

  useEffect(() => {
    const settings = privateHomework.settings;
    if (!session?.authenticated || !settings) return;
    if ((settings.selectedGroupId || "") !== groupId) setGroupState(settings.selectedGroupId || "");
    setInvertState(settings.parityInvert); setIntersectionStrictness(settings.strictness); setShowAbsentFriends(settings.alwaysShow);
  }, [privateHomework.settings, session?.authenticated]);

  const value = useMemo<State>(() => ({
    theme, setTheme: setThemeState, animations, setAnimations, invert, setInvert: value => { setInvertState(value); if (session?.authenticated) privateHomework.saveSettings({ parityInvert: value }); },
    groupId, setGroupId: chooseGroup, catalog, lessons: bundle?.groupId === groupId ? bundle.payload.lessons : [],
    timetableAvailable: bundle?.groupId === groupId,
    timetableLoading: !!groupId && (timetableStatus.groupId !== groupId || timetableStatus.loading),
    timetableFailed: timetableStatus.groupId === groupId && timetableStatus.failed,
    notice, loading, refresh: () => setTick(n => n + 1),
    session, refreshSession: async () => setSession(await api.session()),
    privateHomework, homework, saveHomework: privateHomework.save,
    friends, saveFriends: setFriends,
    intersectionStrictness, setIntersectionStrictness: value => { setIntersectionStrictness(value); if (session?.authenticated) privateHomework.saveSettings({ strictness: value }); }, showAbsentFriends, setShowAbsentFriends: value => { setShowAbsentFriends(value); if (session?.authenticated) privateHomework.saveSettings({ alwaysShow: value }); },
    date, setDate,
    subgroups,
    pickSubgroup: (streamId, optionId) => setSubgroups(current => {
      const group = { ...(current[groupId] || {}) };
      if (group[streamId] === optionId) delete group[streamId];
      else group[streamId] = optionId;
      return { ...current, [groupId]: group };
    }),
  }), [theme, animations, invert, groupId, catalog, bundle, timetableStatus, notice, loading, session, privateHomework, homework,
    friends, intersectionStrictness, showAbsentFriends, date, subgroups]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useApp() {
  const value = useContext(Ctx);
  if (!value) throw new Error("Контекст «Расписание военмех» не найден");
  return value;
}
