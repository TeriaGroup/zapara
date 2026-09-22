import { createContext, ReactNode, useContext, useEffect, useMemo, useState } from "react";
import * as api from "./api";
import { smartDate } from "./parity";
import type { FriendItem, GroupsPayload, HomeworkItem, Lesson, Session, TimetablePayload } from "./types";

type State = {
  theme: "light" | "dark";
  setTheme: (theme: "light" | "dark") => void;
  invert: boolean;
  setInvert: (value: boolean) => void;
  groupId: string;
  setGroupId: (id: string) => void;
  catalog: GroupsPayload | null;
  lessons: Lesson[];
  notice: string;
  loading: boolean;
  refresh: () => void;
  session: Session | null;
  refreshSession: () => Promise<void>;
  homework: HomeworkItem[];
  saveHomework: (item: HomeworkItem) => void;
  friends: FriendItem[];
  saveFriends: (items: FriendItem[]) => void;
  date: Date;
  setDate: (date: Date) => void;
  subgroups: Record<string, Record<string, string>>;
  pickSubgroup: (streamId: string, optionId: string) => void;
};

const Ctx = createContext<State | null>(null);
const groupKey = "zapara.group";
const invertKey = "zapara.invert";
const homeworkKey = "zapara.homework";
const friendsKey = "zapara.friends";
const subgroupKey = "zapara.subgroups";

function readList<T>(key: string): T[] {
  try { return JSON.parse(localStorage.getItem(key) || "[]") as T[]; } catch { return []; }
}

export function Provider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<"light" | "dark">((document.documentElement.dataset.theme as "light" | "dark") || "dark");
  const [invert, setInvertState] = useState(localStorage.getItem(invertKey) === "1");
  const [groupId, setGroupState] = useState(localStorage.getItem(groupKey) || "");
  const [catalog, setCatalog] = useState<GroupsPayload | null>(api.readCache().groups ?? null);
  const [bundle, setBundle] = useState<TimetablePayload | null>(groupId ? api.readCache().lessons[groupId] ?? null : null);
  const [notice, setNotice] = useState("");
  const [loading, setLoading] = useState(false);
  const [tick, setTick] = useState(0);
  const [session, setSession] = useState<Session | null>(null);
  const [homework, setHomework] = useState<HomeworkItem[]>(() => readList(homeworkKey));
  const [friends, setFriends] = useState<FriendItem[]>(() => readList(friendsKey));
  const [date, setDate] = useState(() => smartDate());
  const [subgroups, setSubgroups] = useState<Record<string, Record<string, string>>>(() => {
    try { return JSON.parse(localStorage.getItem(subgroupKey) || "{}"); } catch { return {}; }
  });

  useEffect(() => { document.documentElement.dataset.theme = theme; localStorage.setItem("zapara.theme", theme); }, [theme]);
  useEffect(() => { localStorage.setItem(invertKey, invert ? "1" : "0"); }, [invert]);
  useEffect(() => { if (groupId) localStorage.setItem(groupKey, groupId); }, [groupId]);
  useEffect(() => { localStorage.setItem(homeworkKey, JSON.stringify(homework)); }, [homework]);
  useEffect(() => { localStorage.setItem(friendsKey, JSON.stringify(friends)); }, [friends]);
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
      setGroupState(current => current && payload.groups.some(group => group.id === current) ? current : payload.groups[0]?.id || "");
      setNotice(payload.meta.stale ? "Расписание может быть устаревшим. Показана сохранённая копия." : "");
    }).catch(() => {
      if (stop) return;
      setNotice(catalog ? "Расписание не обновилось. Доступна сохранённая копия." : "Нет сети и сохранённой копии.");
    }).finally(() => { if (!stop) setLoading(false); });
    return () => { stop = true; };
  }, [tick]);

  useEffect(() => {
    if (!groupId) return;
    let stop = false;
    const cached = api.readCache().lessons[groupId];
    if (cached) setBundle(cached);
    api.loadTimetable(groupId).then(payload => {
      if (stop) return;
      setBundle(payload);
      const cache = api.readCache();
      cache.lessons[groupId] = payload;
      if (payload.period) cache.groups = { period: payload.period, meta: payload.meta, groups: cache.groups?.groups || catalog?.groups || [] };
      api.writeCache(cache);
    }).catch(() => {
      if (!stop && cached) setNotice("Расписание не обновилось. Доступна сохранённая копия.");
    });
    return () => { stop = true; };
  }, [groupId, tick]);

  useEffect(() => { void api.session().then(setSession).catch(() => setSession(null)); }, []);

  const value = useMemo<State>(() => ({
    theme, setTheme: setThemeState, invert, setInvert: setInvertState,
    groupId, setGroupId: setGroupState, catalog, lessons: bundle?.lessons || [],
    notice, loading, refresh: () => setTick(n => n + 1),
    session, refreshSession: async () => setSession(await api.session()),
    homework, saveHomework: item => setHomework(list => {
      const next = list.some(row => row.id === item.id) ? list.map(row => row.id === item.id ? item : row) : [item, ...list];
      return next;
    }),
    friends, saveFriends: setFriends, date, setDate,
    subgroups,
    pickSubgroup: (streamId, optionId) => setSubgroups(current => {
      const group = { ...(current[groupId] || {}) };
      if (group[streamId] === optionId) delete group[streamId];
      else group[streamId] = optionId;
      return { ...current, [groupId]: group };
    }),
  }), [theme, invert, groupId, catalog, bundle, notice, loading, session, homework, friends, date, subgroups]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useApp() {
  const value = useContext(Ctx);
  if (!value) throw new Error("Контекст Запары не найден");
  return value;
}
