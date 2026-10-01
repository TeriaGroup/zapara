import { createContext, ReactNode, useContext, useEffect, useMemo, useRef, useState } from "react";
import * as api from "./api";
import { usePrivateHomework } from "./private-sync";
import { resolveStoredGroup } from "./groupChoice";
import { normalizeIntersectionStrictness } from "./intersectionStrictness";
import { createSessionRefresher } from "./session-refresh";
import { subgroupIndex } from "./subgroups";
import { subgroupUndoCurrent, type SubgroupUndo } from "./next-workflows";

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
  sessionLoaded: boolean;
  sessionStatus: string;
  refreshSession: () => Promise<void>;
  acceptProfileName: (user: NonNullable<Session["user"]>) => void;
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
  undoSubgroup: () => void;
  canUndoSubgroup: boolean;
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
  const [sessionLoaded, setSessionLoaded] = useState(false);
  const [sessionStatus, setSessionStatus] = useState("Проверяем аккаунт…");
  const sessionRefresher = useRef<(() => Promise<void>) | null>(null);
  if (!sessionRefresher.current) sessionRefresher.current = createSessionRefresher(api.authGeneration, api.session,
    value => { setSession(value); setSessionLoaded(true); setSessionStatus(""); },
    () => { setSessionLoaded(true); setSessionStatus("Не удалось обновить аккаунт. Показаны последние данные."); });
  const privateHomework = usePrivateHomework(session?.authenticated ? session.user?.userId ?? null : null);
  const homework = privateHomework.items;
  const [friends, setFriends] = useState<FriendItem[]>(() => readList(friendsKey));
  const [intersectionStrictness, setIntersectionStrictness] = useState(() => {
    return normalizeIntersectionStrictness(localStorage.getItem(intersectionStrictnessKey));
  });
  const [showAbsentFriends, setShowAbsentFriends] = useState(localStorage.getItem(showAbsentFriendsKey) === "1");
  const guestPreferences = useRef({ groupId, invert, strictness: intersectionStrictness, showAbsentFriends });
  const sessionIdentity = session?.authenticated && session.user ? `${session.user.userId}:${session.familyId || ""}` : "guest";
  const previousIdentity = useRef<string | null>(null);
  const [date, setDateState] = useState(() => { const now = new Date(); return new Date(now.getFullYear(), now.getMonth(), now.getDate()); });
  const setDate = (value: Date) => setDateState(new Date(value.getFullYear(), value.getMonth(), value.getDate()));
  const [subgroups, setSubgroups] = useState<Record<string, Record<string, string>>>(() => {
    try { return JSON.parse(localStorage.getItem(subgroupKey) || "{}"); } catch { return {}; }
  });
  const subgroupOwner = session?.authenticated ? session.user?.userId || "account" : "guest";
  const [subgroupLoadedOwner,setSubgroupLoadedOwner]=useState("guest");
  const subgroupRef=useRef(subgroups); subgroupRef.current=subgroups;
  const subgroupScope=useRef({identity:sessionIdentity,owner:subgroupOwner,groupId,epoch:0,lessons:bundle?.groupId===groupId?bundle.payload.lessons:[]});
  const subgroupEpoch=subgroupScope.current.epoch+(subgroupScope.current.identity!==sessionIdentity||subgroupScope.current.groupId!==groupId?1:0);
  subgroupScope.current={identity:sessionIdentity,owner:subgroupOwner,groupId,epoch:subgroupEpoch,lessons:bundle?.groupId===groupId?bundle.payload.lessons:[]};
  const subgroupScopeId=`${sessionIdentity}:${subgroupEpoch}`;
  const [subgroupUndo,setSubgroupUndo]=useState<SubgroupUndo|null>(null);
  useEffect(()=>{let choices:Record<string,Record<string,string>>={};try{choices=JSON.parse(localStorage.getItem(subgroupOwner==="guest"?subgroupKey:`${subgroupKey}.${subgroupOwner}`)||"{}");}catch{} subgroupRef.current=choices;setSubgroups(choices);setSubgroupLoadedOwner(subgroupOwner);setSubgroupUndo(null);},[subgroupOwner]);
  const activeSubgroups=subgroupLoadedOwner===subgroupOwner?subgroups:{};
  const canUndoSubgroup=!!subgroupUndo&&subgroupUndoCurrent(subgroupUndo,subgroupScopeId,groupId,activeSubgroups[groupId]||{});
  function persistSubgroup(stream:string, option:string|undefined, undo=false) {
    const live=subgroupScope.current;
    if(live.epoch!==subgroupEpoch||live.identity!==sessionIdentity||live.owner!==subgroupLoadedOwner||live.groupId!==groupId)return;
    const currentStream=subgroupIndex(live.lessons).streams.find(row=>row.id===stream);
    if(!currentStream || (option!==undefined&&!currentStream.options.some(row=>row.id===option)))return;
    const previous=subgroupRef.current[groupId]?.[stream];
    const group={...(subgroupRef.current[groupId]||{})};if(option===undefined)delete group[stream];else group[stream]=option;
    const next={...subgroupRef.current,[groupId]:group};
    try { localStorage.setItem(subgroupOwner==="guest"?subgroupKey:`${subgroupKey}.${subgroupOwner}`,JSON.stringify(next)); }
    catch { setNotice("Выбор подгруппы не сохранён. Повторите попытку.");return; }
    subgroupRef.current=next;setSubgroups(next);
    setSubgroupUndo(undo?null:{scope:subgroupScopeId,group:groupId,stream,before:previous,after:option});
  }

  useEffect(() => {
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const update = () => { document.documentElement.dataset.theme = theme === "system" ? media.matches ? "dark" : "light" : theme; };
    update(); localStorage.setItem("zapara.theme", theme);
    media.addEventListener("change", update);
    return () => media.removeEventListener("change", update);
  }, [theme]);
  useEffect(() => { localStorage.setItem("zapara.animations", animations ? "1" : "0"); document.documentElement.dataset.motion = animations ? "on" : "off"; }, [animations]);
  useEffect(() => { if (sessionLoaded && !session?.authenticated && previousIdentity.current === sessionIdentity) localStorage.setItem(invertKey, invert ? "1" : "0"); }, [invert, sessionLoaded, sessionIdentity]);
  useEffect(() => { if (sessionLoaded && !session?.authenticated && previousIdentity.current === sessionIdentity && groupReady.current) localStorage.setItem(groupKey, groupId); }, [groupId, sessionLoaded, sessionIdentity]);
  const chooseGroup = (id: string) => {
    groupReady.current = true;
    if (!session?.authenticated) { guestPreferences.current.groupId = id; localStorage.setItem(groupKey, id); }
    const cached = id ? api.readCache().lessons[id] : null;
    setBundle(cached ? { groupId: id, payload: cached } : null);
    setTimetableStatus({ groupId: id, loading: !!id, failed: false });
    setNotice(catalog?.meta.stale ? "Расписание может быть устаревшим. Показана сохранённая копия." : "");
    setGroupState(id);
    if (session?.authenticated) privateHomework.saveSettings({ selectedGroupId: id || null });
  };

  useEffect(() => { if (sessionLoaded && !session?.authenticated && previousIdentity.current === sessionIdentity) localStorage.setItem(intersectionStrictnessKey, String(intersectionStrictness)); }, [intersectionStrictness, sessionLoaded, sessionIdentity]);
  useEffect(() => { if (sessionLoaded && !session?.authenticated && previousIdentity.current === sessionIdentity) localStorage.setItem(showAbsentFriendsKey, showAbsentFriends ? "1" : "0"); }, [showAbsentFriends, sessionLoaded, sessionIdentity]);

  useEffect(() => {
    let stop = false;
    setLoading(true);
    api.loadGroups().then(payload => {
      if (stop) return;
      setCatalog(payload);
      const cache = api.readCache();
      cache.groups = payload;
      api.writeCache(cache);
      const next = resolveStoredGroup(session?.authenticated ? groupId : groupReady.current ? localStorage.getItem(groupKey) : null, payload.groups);
      const cached = next ? api.readCache().lessons[next] : null;
      setBundle(cached ? { groupId: next, payload: cached } : null);
      setTimetableStatus({ groupId: next, loading: !!next, failed: false });
      if (!groupReady.current && !session?.authenticated) {
        groupReady.current = true;
        localStorage.setItem(groupKey, next);
        guestPreferences.current.groupId = next;
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

  useEffect(() => {
    const refresh = () => { void sessionRefresher.current?.().catch(() => undefined); };
    const visibleRefresh = () => { if (!document.hidden) refresh(); };
    refresh();
    const timer = window.setInterval(visibleRefresh, 60_000);
    window.addEventListener("focus", visibleRefresh);
    window.addEventListener("online", visibleRefresh);
    document.addEventListener("visibilitychange", visibleRefresh);
    return () => { window.clearInterval(timer); window.removeEventListener("focus", visibleRefresh); window.removeEventListener("online", visibleRefresh); document.removeEventListener("visibilitychange", visibleRefresh); };
  }, []);

  useEffect(() => {
    if (!sessionLoaded || previousIdentity.current === sessionIdentity) return;
    if (previousIdentity.current === "guest" && sessionIdentity !== "guest")
      guestPreferences.current = { groupId, invert, strictness: intersectionStrictness, showAbsentFriends };
    if (previousIdentity.current && previousIdentity.current !== "guest") {
      const guest = guestPreferences.current;
      setGroupState(guest.groupId);
      setInvertState(guest.invert);
      setIntersectionStrictness(guest.strictness);
      setShowAbsentFriends(guest.showAbsentFriends);
    }
    previousIdentity.current = sessionIdentity;
  }, [sessionLoaded, sessionIdentity]);

  useEffect(() => {
    const settings = privateHomework.settings;
    if (!session?.authenticated || !settings) return;
    if ((settings.selectedGroupId || "") !== groupId) setGroupState(settings.selectedGroupId || "");
    setInvertState(settings.parityInvert); setIntersectionStrictness(normalizeIntersectionStrictness(settings.strictness)); setShowAbsentFriends(settings.alwaysShow);
  }, [privateHomework.settings, sessionIdentity]);

  const value = useMemo<State>(() => ({
    theme, setTheme: setThemeState, animations, setAnimations, invert, setInvert: value => { setInvertState(value); if (session?.authenticated) privateHomework.saveSettings({ parityInvert: value }); else guestPreferences.current.invert = value; },
    groupId, setGroupId: chooseGroup, catalog, lessons: bundle?.groupId === groupId ? bundle.payload.lessons : [],
    timetableAvailable: bundle?.groupId === groupId,
    timetableLoading: !!groupId && (timetableStatus.groupId !== groupId || timetableStatus.loading),
    timetableFailed: timetableStatus.groupId === groupId && timetableStatus.failed,
    notice, loading, refresh: () => setTick(n => n + 1),
    session, sessionLoaded, sessionStatus, refreshSession: () => sessionRefresher.current!(),
    acceptProfileName: user => setSession(current=>current?.authenticated&&current.user?.userId===user.userId?{...current,user}:current),
    privateHomework, homework, saveHomework: privateHomework.save,
    friends, saveFriends: items => { if(subgroupScope.current.identity!==sessionIdentity)throw new Error("Профиль изменился.");localStorage.setItem(friendsKey,JSON.stringify(items));setFriends(items); },
    intersectionStrictness, setIntersectionStrictness: value => { const strictness = normalizeIntersectionStrictness(value); setIntersectionStrictness(strictness); if (session?.authenticated) privateHomework.saveSettings({ strictness }); else guestPreferences.current.strictness = strictness; }, showAbsentFriends, setShowAbsentFriends: value => { setShowAbsentFriends(value); if (session?.authenticated) privateHomework.saveSettings({ alwaysShow: value }); else guestPreferences.current.showAbsentFriends = value; },
    date, setDate,
    subgroups: activeSubgroups, canUndoSubgroup,
    pickSubgroup: (streamId, optionId) => persistSubgroup(streamId,subgroupRef.current[groupId]?.[streamId]===optionId?undefined:optionId),
    undoSubgroup: () => {if(subgroupUndo&&subgroupUndoCurrent(subgroupUndo,`${subgroupScope.current.identity}:${subgroupScope.current.epoch}`,subgroupScope.current.groupId,subgroupRef.current[groupId]||{}))persistSubgroup(subgroupUndo.stream,subgroupUndo.before,true);},
  }), [theme, animations, invert, groupId, catalog, bundle, timetableStatus, notice, loading, session, sessionLoaded, sessionStatus, privateHomework, homework,
    friends, intersectionStrictness, showAbsentFriends, date, subgroups, subgroupLoadedOwner, subgroupUndo, canUndoSubgroup]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useApp() {
  const value = useContext(Ctx);
  if (!value) throw new Error("Контекст «Расписание военмех» не найден");
  return value;
}
