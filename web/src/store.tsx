import { createContext, ReactNode, useContext, useEffect, useMemo, useRef, useState } from "react";
import * as api from "./api";
import { resolveStoredGroup } from "./groupChoice";
import { openingDate } from "./parity";
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
  const [groupId, setGroupState] = useState(() => localStorage.getItem(groupKey) ?? "");
  const groupReady = useRef(localStorage.getItem(groupKey) !== null);
  const [catalog, setCatalog] = useState<GroupsPayload | null>(api.readCache().groups ?? null);
  const [bundle, setBundle] = useState<TimetablePayload | null>(groupId ? api.readCache().lessons[groupId] ?? null : null);
  const [notice, setNotice] = useState("");
  const [loading, setLoading] = useState(false);
  const [tick, setTick] = useState(0);
  const [session, setSession] = useState<Session | null>(null);
  const [homework, setHomework] = useState<HomeworkItem[]>(() => readList(homeworkKey));
  const [friends, setFriends] = useState<FriendItem[]>(() => readList(friendsKey));
  const [date, setDateState] = useState(() => openingDate(new Date(), []));
  const dateMoved = useRef(false);
  const setDate = (value: Date) => { dateMoved.current = true; setDateState(value); };
  const [subgroups, setSubgroups] = useState<Record<string, Record<string, string>>>(() => {
    try { return JSON.parse(localStorage.getItem(subgroupKey) || "{}"); } catch { return {}; }
  });

  useEffect(() => { document.documentElement.dataset.theme = theme; localStorage.setItem("zapara.theme", theme); }, [theme]);
  useEffect(() => { localStorage.setItem(invertKey, invert ? "1" : "0"); }, [invert]);
  useEffect(() => { if (groupReady.current) localStorage.setItem(groupKey, groupId); }, [groupId]);
  const chooseGroup = (id: string) => {
    groupReady.current = true;
    localStorage.setItem(groupKey, id);
    setGroupState(id);
  };
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
      setGroupState(() => {
        if (groupReady.current) return resolveStoredGroup(localStorage.getItem(groupKey), payload.groups);
        const next = resolveStoredGroup(null, payload.groups);
        groupReady.current = true;
        localStorage.setItem(groupKey, next);
        return next;
      });
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
  useEffect(() => {
    if (dateMoved.current) return;
    const period = bundle?.period;
    setDateState(openingDate(new Date(), bundle?.lessons || [], subgroups[groupId] || {}, period ? { start: period.start, weekCount: period.weekCount, invert } : undefined));
  }, [bundle, subgroups, groupId, invert]);

  const value = useMemo<State>(() => ({
    theme, setTheme: setThemeState, invert, setInvert: setInvertState,
    groupId, setGroupId: chooseGroup, catalog, lessons: bundle?.lessons || [],
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
  if (!value) throw new Error("Контекст «Расписание военмех» не найден");
  return value;
}
