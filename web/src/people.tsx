import { MediaPlayer, PhotoViewer } from "./media-player";
import { revealQuote } from "./quote-navigation";
import { FormEvent, Fragment, UIEvent, useCallback, useEffect, useRef, useState } from "react";
import * as api from "./api";
import { emojiGroups } from "./emoji";
import { cardLabel, homeworkCard, lessonFrom, placeFromLesson, scheduleCard, tasksCard } from "./cards";
import { dayTitle, lessonsOn, longDate } from "./parity";
import { visibleLessons } from "./subgroups";
import { CardView } from "./share";
import { Sticker, stickerPack, stickerTitle } from "./stickers";
import { useApp } from "./store";
import { Icon } from "./icons";
import { holdActions, runHold } from "./hold";
import { createSocialPoller, mergeSocialMessages, reconcileActiveFriend, sameSocialCluster } from "./socialChat";
import { Avatar } from "./avatar-view";
import { chatInboxTime } from "./chatInbox";
import { personalCopyText, searchPersonalHistory } from "./personal-history";
import { Link } from "react-router-dom";
import { emptyChatState, personalText, personalTextCount, personalTextLimit, personalTextValid, sendOnEnter } from "./personal-composer";
import { usePersonalComposer } from "./personal-composer-context";
import type { SocialFriend, SocialHome, SocialMessage } from "./types";
import { noteSearch } from "./ux300";
import { SearchField, focusElement } from "./ux300-controls";
import { Sheet } from "./sheet";

const voiceRecordingLimit = 4 * 1024 * 1024;
const circleRecordingLimit = 24 * 1024 * 1024;

function personName(username: string, displayName: string | null) {
  return displayName?.trim() || username;
}

function when(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "" : date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
}

function size(bytes: number | null) {
  if (!bytes) return "";
  if (bytes < 1024) return `${bytes} Б`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} КБ`;
  return `${(bytes / 1024 / 1024).toFixed(1)} МБ`;
}

function explain(error: unknown, fallback: string) {
  const status = error instanceof Error ? error.message : "";
  if (status === "404") return "Код не найден";
  if (status === "413") return "Файл слишком большой";
  if (status === "400") return "Проверьте данные и попробуйте ещё раз";
  return fallback;
}

export function PeoplePanel({ initialConversationId, onTitleChange }: { initialConversationId?: string; onTitleChange?: (title: string | null) => void } = {}) {
  const app = useApp();
  return <PeopleContent key={JSON.stringify([app.session?.authenticated, app.session?.user?.userId, app.session?.familyId])} initialConversationId={initialConversationId} onTitleChange={onTitleChange} />;
}

function PeopleContent({ initialConversationId, onTitleChange }: { initialConversationId?: string; onTitleChange?: (title: string | null) => void }) {
  const app = useApp();
  const [home, setHome] = useState<SocialHome | null>(null);
  const [active, setActive] = useState<SocialFriend | null>(null);
  const [code, setCode] = useState("");
  const [error, setError] = useState("");
  const [copied, setCopied] = useState(false);
  const [busy, setBusy] = useState(false);
  const [personQuery,setPersonQuery]=useState("");
  const [unavailableRoute,setUnavailableRoute]=useState(false);
  const actionPending = useRef(false);
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  const homeRevision = useRef(0);
  const activeIdRef = useRef<string | null>(null);
  const openedFromRoute = useRef<string | null>(null);
  const activeId = active?.conversationId ?? null;
  const activeTitle = active ? personName(active.username, active.displayName) : null;
  useEffect(() => { onTitleChange?.(activeTitle); }, [activeTitle, onTitleChange]);
  const chatError = useCallback((text: string) => {
    if (activeIdRef.current === activeId) setError(text);
  }, [activeId]);

  useEffect(() => {
    if (!app.session?.authenticated) { activeIdRef.current = null; openedFromRoute.current = null; setHome(null); setActive(null); return; }
    let stop = false;
    let pulling = false;
    const pull = async () => {
      if (pulling) return;
      pulling = true;
      const revision = homeRevision.current;
      try {
        const value = await api.socialHome();
        if (!stop && revision === homeRevision.current) {
          setHome(value);
          setActive(current => {
            const next = reconcileActiveFriend(current, value.friends);
            if (current && !next) activeIdRef.current = null;
            return next;
          });
          if (initialConversationId && openedFromRoute.current !== initialConversationId) {
            openedFromRoute.current = initialConversationId;
            const selected = value.friends.find(item => item.conversationId === initialConversationId);
            setUnavailableRoute(!selected);
            if (selected) { activeIdRef.current = selected.conversationId; setActive(selected); }
          }
          setError(current => current === "Переписка не открылась" ? "" : current);
        }
      } catch {
        if (!stop && revision === homeRevision.current) setError(current => current || "Переписка не открылась");
      } finally { pulling = false; }
    };
    void pull();
    const timer = window.setInterval(pull, 4000);
    return () => { stop = true; window.clearInterval(timer); };
  }, [app.session?.authenticated, app.session?.user?.userId, app.session?.familyId, initialConversationId]);

  async function invite(event: FormEvent) {
    event.preventDefault();
    if (!code.trim() || actionPending.current) return;
    actionPending.current = true;
    homeRevision.current += 1;
    setBusy(true);
    setError("");
    try {
      const value = await api.socialInvite(code.trim());
      if (!alive.current) return;
      setHome(value);
      homeRevision.current += 1;
      setCode("");
    } catch (reason) {
      if (alive.current) setError(explain(reason, "Не удалось добавить по коду"));
    } finally { actionPending.current = false; if (alive.current) setBusy(false); }
  }

  async function answer(friendshipId: string, accept: boolean) {
    if (actionPending.current) return;
    actionPending.current = true;
    setBusy(true);
    homeRevision.current += 1;
    setError("");
    try {
      const value = await (accept ? api.socialAccept(friendshipId) : api.socialDecline(friendshipId));
      if (!alive.current) return;
      setHome(value);
      homeRevision.current += 1;
    }
    catch { if (alive.current) setError(accept ? "Не удалось принять запрос. Попробуйте ещё раз." : "Не удалось отклонить запрос. Попробуйте ещё раз."); }
    finally { actionPending.current = false; if (alive.current) setBusy(false); }
  }

  async function copy() {
    if (!home?.code || !alive.current) return;
    try {
      await navigator.clipboard.writeText(home.code);
      if (!alive.current) return;
      setCopied(true);
      window.setTimeout(() => { if (alive.current) setCopied(false); }, 1600);
    } catch { if (alive.current) { setCopied(false); setError("Скопируйте код вручную"); } }
  }

  if (!app.session?.authenticated) {
    return <div className="card"><h2>Переписка</h2><p className="muted">Войдите в аккаунт, чтобы получить свой код и писать другим. Расписание и карты работают и без входа.</p><Link className="btn primary" to="/settings?section=account">Открыть настройки аккаунта</Link></div>;
  }

  return (
    <div className={"stack people-panel" + (initialConversationId ? " direct-route" : "") + (active ? " active-conversation" : "")}>
      {error && <div className="banner">{error}</div>}
      {unavailableRoute&&<div className="banner" role="status">Беседа по ссылке недоступна в этом аккаунте.<Link className="btn" to="/chat?all=1">К списку бесед</Link><button className="btn quiet" onClick={()=>{openedFromRoute.current=null;setUnavailableRoute(false);}}>Проверить снова</button></div>}
      {!!home?.incoming.length && (
        <div className="stack">
          <h2>Входящие запросы</h2>
          {home.incoming.map(item => (
            <article className="card row" key={item.friendshipId} style={{ justifyContent: "space-between" }}>
              <div className="row"><Avatar kind="user" id={null} name={personName(item.username, item.displayName)} /><div><b>{personName(item.username, item.displayName)}</b><div className="muted">@{item.username}</div></div></div>
              <div className="row">
                <button className="btn primary" type="button" disabled={busy} onClick={() => void answer(item.friendshipId, true)}>Принять</button>
                <button className="btn" type="button" disabled={busy} onClick={() => void answer(item.friendshipId, false)}>Отклонить</button>
              </div>
            </article>
          ))}
        </div>
      )}
      {!!home?.outgoing.length && (
        <div className="stack">
          <h2>Ожидают ответа</h2>
          {home.outgoing.map(item => (
            <article className="card row" key={item.friendshipId}><Avatar kind="user" id={null} name={personName(item.username, item.displayName)} /><div><b>{personName(item.username, item.displayName)}</b><div className="muted">Запрос отправлен · @{item.username}</div></div></article>
          ))}
        </div>
      )}
      <div className={"grid-2 split" + (active ? " focus" : "")}>
        <div className="split-list stack">
          <article className="card">
            <h2>Ваш код</h2>
            <div className="row" style={{ justifyContent: "space-between" }}>
              <span className="code">{home?.code || "……"}</span>
              <button className="btn" type="button" onClick={() => void copy()} disabled={!home?.code}>{copied ? "Скопирован" : "Скопировать"}</button>
            </div>
            <p className="muted">Код индивидуальный. Его можно продиктовать или отправить человеку, с которым хотите переписываться.</p>
          </article>
          <form className="card stack" onSubmit={event => void invite(event)}>
            <label className="field">Добавить по коду
              <input value={code} onChange={event => setCode(event.target.value.toUpperCase())} placeholder="ABCD2345" maxLength={16} autoCapitalize="characters" aria-label="Код друга" />
            </label>
            <button className="btn primary" type="submit" disabled={busy || code.trim().length < 8}>Добавить</button>
          </form>
          <div className="people">
          <SearchField label="Найти человека" value={personQuery} onChange={setPersonQuery}/>
          {(home?.friends || []).filter(friend=>noteSearch(personQuery,friend.displayName||"",friend.username)).map(friend => (
            <button className="person" key={friend.userId} type="button" onClick={() => { activeIdRef.current = friend.conversationId; setError(""); setActive(friend); }}>
              <Avatar kind="user" id={friend.userId} name={personName(friend.username, friend.displayName)} />
              <span className="person-main"><b>{personName(friend.username, friend.displayName)}</b><span className="muted preview-line">{friend.lastBody || "Нет сообщений"}</span></span>
              <span className="inbox-meta">{friend.lastAt && <time className="muted" dateTime={friend.lastAt}>{chatInboxTime(friend.lastAt)}</time>}{friend.unread > 0 && <span className="chip" aria-label={`Непрочитанных сообщений: ${friend.unread}`}>{friend.unread > 99 ? "99+" : friend.unread}</span>}</span>
            </button>
          ))}
          {home && home.friends.length === 0 && <div className="empty">Пока никого нет. Добавьте человека по коду.</div>}
          {home&&home.friends.length>0&&!home.friends.some(friend=>noteSearch(personQuery,friend.displayName||"",friend.username))&&<p role="status">Человек по запросу не найден. Очистите поиск, чтобы вернуться к списку.</p>}
          </div>
        </div>
        {active ? <section className="split-detail">{initialConversationId ? <Link className="btn back-only" to="/chat?all=1">К чатам</Link> : <button className="btn back-only" type="button" onClick={() => { activeIdRef.current = null; setError(""); setActive(null); }}>К списку</button>}<Chat key={active.conversationId} friend={active} self={app.session.user?.userId || ""} familyId={app.session.familyId} onError={chatError} /></section> : <section className="card chat split-detail"><h2>Чат</h2><p className="muted">Выберите человека в списке.</p></section>}
      </div>
    </div>
  );
}

const reactionChoices = [
  ["like", "👍"],
  ["heart", "❤️"],
  ["laugh", "😂"],
  ["wow", "😮"],
  ["sad", "😢"]
] as const;

const recentEmojiKey = "zapara.emoji.recent";

function readRecent() {
  try {
    const value = JSON.parse(localStorage.getItem(recentEmojiKey) || "[]") as unknown;
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string").slice(0, 16) : [];
  } catch { return []; }
}

function snippet(message: SocialMessage) {
  if (message.deleted) return "Сообщение удалено";
  if (message.kind === "sticker") return stickerTitle(message.body);
  if (message.kind === "voice") return "Голосовое";
  if (message.kind === "circle") return "Кружок";
  if (message.kind === "card") return cardLabel(message.body);
  if (message.kind === "image") return "Фото";
  if (message.kind === "file") return message.fileName || "Документ";
  return message.body || "Сообщение";
}

function reactionMark(code: string) {
  return reactionChoices.find(item => item[0] === code)?.[1] || code;
}

function EmojiPanel({ onPick }: { onPick: (emoji: string) => void }) {
  const [recent, setRecent] = useState(readRecent);
  function pick(emoji: string) {
    const next = [emoji, ...recent.filter(item => item !== emoji)].slice(0, 16);
    localStorage.setItem(recentEmojiKey, JSON.stringify(next));
    setRecent(next);
    onPick(emoji);
  }
  return (
    <div className="picker-wrap">
      {recent.length > 0 && <div className="picker">{recent.map(item => <button key={"recent-" + item} type="button" onClick={() => pick(item)}>{item}</button>)}</div>}
      {emojiGroups.map(group => (
        <div key={group.title}>
          <div className="muted">{group.title}</div>
          <div className="picker">{group.items.map(item => <button key={group.title + item} type="button" onClick={() => pick(item)}>{item}</button>)}</div>
        </div>
      ))}
    </div>
  );
}

function clock(ms: number) {
  const total = Math.max(0, Math.round(ms / 1000));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
}

function VoiceNote({src,duration}:{src:string;duration:number|null}) { return <MediaPlayer src={src} knownDuration={duration||0}/>; }
function CircleNote({src,duration}:{src:string;duration:number|null}) { return <MediaPlayer src={src} kind="circle" knownDuration={duration||0}/>; }

function StudyShelf({ onSend }: { onSend: (body: string | null) => void }) {
  const app = useApp();
  const period = app.catalog?.period;
  const group = app.catalog?.groups.find(item => item.id === app.groupId)?.name || "";
  const day = period ? lessonsOn(visibleLessons(app.lessons, app.subgroups[app.groupId] || {}), app.date, period.start, period.weekCount, app.invert) : [];
  const title = period ? longDate(app.date, period.start, period.weekCount, app.invert) : dayTitle(app.date);
  const undone = app.homework.filter(item => !item.done);
  const next = day[0];
  return (
    <div className="stack">
      <button className="btn" type="button" disabled={day.length === 0} onClick={() => onSend(scheduleCard(group, app.date, title, day))}>День · {dayTitle(app.date)}</button>
      <button className="btn" type="button" disabled={undone.length === 0} onClick={() => onSend(undone.length === 1 ? homeworkCard(undone[0]) : tasksCard(undone))}>Домашка{undone.length ? ` · ${undone.length}` : ""}</button>
      <button className="btn" type="button" disabled={!next} onClick={() => onSend(next ? (placeFromLesson(next) || lessonFrom(group, app.date, next)) : null)}>Куда идти</button>
      {day.map(lesson => (
        <button className="btn" type="button" key={lesson.timeStart + lesson.subjectRaw} onClick={() => onSend(lessonFrom(group, app.date, lesson))}>{lesson.timeStart} {lesson.subjectRaw}</button>
      ))}
      {day.length === 0 && <p className="muted">На выбранный день пар нет. День меняется в расписании.</p>}
    </div>
  );
}

function Chat({ friend, self, familyId, onError }: { friend: SocialFriend; self: string; familyId?: string | null; onError: (text: string) => void }) {
  const [messages, setMessages] = useState<SocialMessage[]>([]);
  const [historyQuery, setHistoryQuery] = useState("");
  const [historyKind,setHistoryKind]=useState("all");
  const [historyAuthor,setHistoryAuthor]=useState("all");
  const [historySearchOpen,setHistorySearchOpen]=useState(false);
  const [historyFiltersOpen,setHistoryFiltersOpen]=useState(false);
  const [matchId,setMatchId]=useState("");
  const [copyNotice, setCopyNotice] = useState("");
  const visibleMessages = searchPersonalHistory(messages, historyQuery).filter(message=>(historyKind==="all"||message.kind===historyKind)&&(historyAuthor==="all"||historyAuthor==="mine"&&message.senderId===self||historyAuthor==="other"&&message.senderId!==self));
  const historyFiltered = !!historyQuery.trim() || historyKind !== "all" || historyAuthor !== "all";
  const historyFilterCount = Number(!!historyQuery.trim()) + Number(historyKind !== "all") + Number(historyAuthor !== "all");
  function resetHistoryFilters(){setHistoryQuery("");setHistoryKind("all");setHistoryAuthor("all");}
  function match(direction:number){if(!visibleMessages.length)return;const at=visibleMessages.findIndex(row=>row.messageId===matchId);const next=at<0?(direction>0?0:visibleMessages.length-1):(at+direction+visibleMessages.length)%visibleMessages.length;setMatchId(visibleMessages[next].messageId);focusElement(`personal-message-${visibleMessages[next].messageId}`);}
  const [more, setMore] = useState(false);
  const [loadingEarlier, setLoadingEarlier] = useState(false);
  const [showLatestJump, setShowLatestJump] = useState(false);
  const { store: composerStore, refresh: refreshComposer, composer } = usePersonalComposer(friend.conversationId);
  const { text: draft, reply, editing, busy: sending } = composer;
  const draftCount = personalTextCount(draft);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [historyError, setHistoryError] = useState("");
  const [desktopKeyboard] = useState(() => window.matchMedia("(hover: hover) and (pointer: fine)").matches);
  const composingRef = useRef(false);
  function setDraft(value: string) { composerStore.text(friend.conversationId, value); refreshComposer(); }
  const holdTimer = useRef<number | null>(null);
  const heldOpen = useRef(false);
  const [captured,setCaptured]=useState<{file:File;kind:"voice"|"circle";duration:number;url:string}|null>(null);
  const capturedUrl=useRef<string|null>(null);
  useEffect(()=>()=>{if(captured){URL.revokeObjectURL(captured.url);if(capturedUrl.current===captured.url)capturedUrl.current=null;}},[captured]);
  useEffect(()=>()=>{if(capturedUrl.current){URL.revokeObjectURL(capturedUrl.current);capturedUrl.current=null;}},[]);
  const [recording, setRecording] = useState(false);
  const [circling, setCircling] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [panel, setPanel] = useState<null | "emoji" | "stickers" | "attach" | "study">(null);
  const [openMenu, setOpenMenu] = useState<string | null>(null);
  const [reactFor, setReactFor] = useState<string | null>(null);
  const logRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const stick = useRef(true);
  const photoRef = useRef<HTMLInputElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const recorder = useRef<MediaRecorder | null>(null);
  const circleRecorder = useRef<MediaRecorder | null>(null);
  const circleStream = useRef<MediaStream | null>(null);
  const circleChunks = useRef<Blob[]>([]);
  const circleCancel = useRef(false);
  const circleTick = useRef<number | null>(null);
  const previewRef = useRef<HTMLVideoElement>(null);
  const chunks = useRef<Blob[]>([]);
  const cancelVoice = useRef(false);
  const elapsedRef = useRef(0);
  const messagesRef = useRef<SocialMessage[]>([]);
  const pollerRef = useRef<ReturnType<typeof createSocialPoller> | null>(null);
  const aliveRef = useRef(false);
  const sendingRef = useRef(false);
  const earlierRef = useRef(false);
  const voiceStartingRef = useRef(false);
  const circleStartingRef = useRef(false);

  function addMessages(incoming: SocialMessage[], older = false) {
    const next = older
      ? mergeSocialMessages(incoming, messagesRef.current)
      : mergeSocialMessages(messagesRef.current, incoming);
    messagesRef.current = next;
    setMessages(next);
  }

  function reportError(value: string) {
    if (aliveRef.current) onError(value);
  }

  function beginSend(kind: "text" | "attachment" = "attachment") {
    if (!aliveRef.current || sendingRef.current) return null;
    const ticket = composerStore.begin(friend.conversationId, kind);
    if (!ticket) return null;
    sendingRef.current = true;
    refreshComposer();
    pollerRef.current?.changed();
    return ticket;
  }

  function endSend() {
    sendingRef.current = false;
    refreshComposer();
  }

  useEffect(() => {
    aliveRef.current = true;
    stick.current = true;
    setShowLatestJump(false);
    messagesRef.current = [];
    setMessages([]);
    setMore(false);
    setHistoryLoading(true);
    setHistoryError("");
    setPanel(null);
    setOpenMenu(null);
    setReactFor(null);
    const poller = createSocialPoller(
      before => api.socialMessages(friend.conversationId, before),
      () => messagesRef.current,
      (page, firstLoad) => {
        setHistoryLoading(false);
        setHistoryError("");
        if (firstLoad) setMore(page.hasMore);
        addMessages(page.messages);
      },
      () => { setHistoryLoading(false); setHistoryError("Сообщения не загрузились. Повторите обновление."); },
    );
    pollerRef.current = poller;
    void poller.poll();
    const timer = window.setInterval(() => void poller.poll(), 4000);
    return () => {
      aliveRef.current = false;
      poller.dispose();
      pollerRef.current = null;
      window.clearInterval(timer);
      if (holdTimer.current) window.clearTimeout(holdTimer.current);
      cancelVoice.current = true;
      if (recorder.current && recorder.current.state !== "inactive") recorder.current.stop();
      circleCancel.current = true;
      if (circleRecorder.current && circleRecorder.current.state !== "inactive") circleRecorder.current.stop();
      else circleStream.current?.getTracks().forEach(track => track.stop());
    };
  }, [friend.conversationId, familyId, onError]);

  useEffect(() => {
    const input = inputRef.current;
    if (!input) return;
    input.style.height = "auto";
    input.style.height = `${Math.min(144, Math.max(48, input.scrollHeight))}px`;
  }, [draft, recording, circling]);

  useEffect(() => {
    if (!historyQuery.trim() && stick.current && logRef.current) {
      logRef.current.scrollTop = logRef.current.scrollHeight;
      setShowLatestJump(false);
    } else setShowLatestJump(messages.length > 0);
  }, [messages, historyQuery]);

  useEffect(() => {
    if (!openMenu) return;
    const log = logRef.current;
    if (!log) return;
    stick.current = false;
    const reveal = () => {
      const menu = log.querySelector(".actions");
      if (!(menu instanceof HTMLElement)) return;
      const logBox = log.getBoundingClientRect();
      const menuBox = menu.getBoundingClientRect();
      if (menuBox.top < logBox.top) log.scrollTop -= logBox.top - menuBox.top + 8;
      else if (menuBox.bottom > logBox.bottom) log.scrollTop += menuBox.bottom - logBox.bottom + 8;
    };
    reveal();
    const frame = window.requestAnimationFrame(reveal);
    return () => window.cancelAnimationFrame(frame);
  }, [openMenu]);

  function onScroll(event: UIEvent<HTMLDivElement>) {
    const node = event.currentTarget;
    stick.current = node.scrollHeight - node.scrollTop - node.clientHeight < 80;
    setShowLatestJump(!stick.current && messagesRef.current.length > 0);
  }

  function jumpToLatest() {
    const node = logRef.current;
    if (!node) return;
    stick.current = true;
    node.scrollTop = node.scrollHeight;
    setShowLatestJump(false);
  }

  async function earlier() {
    const first = messagesRef.current[0];
    if (!first || !more || earlierRef.current) return;
    earlierRef.current = true;
    setLoadingEarlier(true);
    const node = logRef.current;
    const height = node?.scrollHeight ?? 0;
    const top = node?.scrollTop ?? 0;
    stick.current = false;
    try {
      const page = await api.socialMessages(friend.conversationId, first.messageId);
      if (!aliveRef.current) return;
      setMore(page.hasMore);
      addMessages(page.messages, true);
      requestAnimationFrame(() => { if (node?.isConnected) node.scrollTop = top + node.scrollHeight - height; });
    } catch { reportError("Старые сообщения не загрузились"); }
    finally {
      earlierRef.current = false;
      if (aliveRef.current) setLoadingEarlier(false);
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    const current = composerStore.read(friend.conversationId);
    const body = personalText(current.text);
    if (!personalTextValid(current.text)) return;
    const ticket = beginSend("text");
    if (!ticket) return;
    const target = ticket.state.editing;
    const replyTo = ticket.state.reply?.messageId;
    try {
      const message = target
        ? await api.socialEdit(friend.conversationId, target.messageId, body)
        : await api.socialText(friend.conversationId, body, replyTo);
      composerStore.finish(ticket, "", true);
      if (!aliveRef.current) return;
      pollerRef.current?.changed();
      addMessages([message]);
      stick.current = true;
    } catch {
      composerStore.finish(ticket, target ? "Ответ об изменении не получен. Текст сохранён; проверьте историю перед повтором." : "Ответ об отправке не получен. Текст сохранён; проверьте историю перед повтором.", false);
    } finally { endSend(); }
  }

  async function sendFile(file: File | undefined, kind: "image" | "file" | "voice" | "circle", durationMs?: number) {
    if (!file) return false;
    const ticket = beginSend();
    if (!ticket) return false;
    const replyTo = ticket.state.reply?.messageId;
    try {
      const message = await api.socialUpload(friend.conversationId, file, kind, { replyTo, durationMs });
      composerStore.finish(ticket, "", true);
      if (!aliveRef.current) return false;
      pollerRef.current?.changed();
      addMessages([message]);
      stick.current = true;
      return true;
    } catch (reason) {
      composerStore.finish(ticket, explain(reason, "Ответ об отправке не получен. Проверьте историю перед повтором."), false);
      return false;
    } finally { endSend(); }
  }

  function insertEmoji(emoji: string) {
    const input = inputRef.current;
    const start = input?.selectionStart ?? draft.length;
    const end = input?.selectionEnd ?? draft.length;
    const next = draft.slice(0, start) + emoji + draft.slice(end);
    setDraft(next);
    const caret = start + emoji.length;
    requestAnimationFrame(() => { input?.focus(); input?.setSelectionRange(caret, caret); });
  }

  async function sendCard(body: string | null) {
    if (!body) { reportError("Нечего отправить"); return; }
    const ticket = beginSend();
    if (!ticket) return;
    const replyTo = ticket.state.reply?.messageId;
    setPanel(null);
    try {
      const message = await api.socialCard(friend.conversationId, body, replyTo);
      composerStore.finish(ticket, "", true);
      if (!aliveRef.current) return;
      pollerRef.current?.changed();
      addMessages([message]);
      stick.current = true;
    } catch { composerStore.finish(ticket, "Ответ об отправке карточки не получен. Проверьте историю перед повтором.", false); }
    finally { endSend(); }
  }

  async function sendSticker(id: string) {
    const ticket = beginSend();
    if (!ticket) return;
    const replyTo = ticket.state.reply?.messageId;
    setPanel(null);
    try {
      const message = await api.socialSticker(friend.conversationId, id, replyTo);
      composerStore.finish(ticket, "", true);
      if (!aliveRef.current) return;
      pollerRef.current?.changed();
      addMessages([message]);
      stick.current = true;
    } catch { composerStore.finish(ticket, "Ответ об отправке стикера не получен. Проверьте историю перед повтором.", false); }
    finally { endSend(); }
  }

  async function change(message: SocialMessage, emoji: string) {
    pollerRef.current?.changed();
    try {
      const next = await api.socialReact(friend.conversationId, message.messageId, emoji);
      if (!aliveRef.current) return;
      pollerRef.current?.changed();
      addMessages([next]);
    } catch { reportError("Реакция не сохранилась"); }
  }

  async function remove(message: SocialMessage) {
    if (!window.confirm("Удалить сообщение?")) return;
    pollerRef.current?.changed();
    try {
      const next = await api.socialDelete(friend.conversationId, message.messageId);
      if (!aliveRef.current) return;
      pollerRef.current?.changed();
      addMessages([next]);
    } catch { reportError("Сообщение не удалилось"); }
  }

  async function toggleVoice() {
    if (recording) { recorder.current?.stop(); return; }
    if (circling || sendingRef.current || composerStore.read(friend.conversationId).busy || voiceStartingRef.current) return;
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === "undefined") { reportError("Этот браузер не записывает голос"); return; }
    voiceStartingRef.current = true;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: { sampleRate: { ideal: 48_000 }, channelCount: { ideal: 1 } } });
      if (!aliveRef.current) { stream.getTracks().forEach(track => track.stop()); return; }
      const mime = ["audio/webm;codecs=opus", "audio/webm", "audio/ogg;codecs=opus", "audio/mp4"].find(item => MediaRecorder.isTypeSupported(item)) || "";
      const options: MediaRecorderOptions = mime ? { mimeType: mime, audioBitsPerSecond: 96_000 } : { audioBitsPerSecond: 96_000 };
      let media: MediaRecorder;
      try { media = new MediaRecorder(stream, options); }
      catch { media = new MediaRecorder(stream); }
      chunks.current = [];
      cancelVoice.current = false;
      elapsedRef.current = 0;
      setElapsed(0);
      let recordedBytes = 0;
      media.ondataavailable = event => {
        if (!event.data.size) return;
        recordedBytes += event.data.size;
        if (recordedBytes > voiceRecordingLimit) {
          if (!cancelVoice.current) reportError("Запись слишком большая");
          cancelVoice.current = true;
          if (media.state !== "inactive") media.stop();
          return;
        }
        chunks.current.push(event.data);
      };
      media.onstop = () => {
        stream.getTracks().forEach(track => track.stop());
        window.clearInterval(tick);
        setRecording(false);
        const blob = new Blob(chunks.current, { type: media.mimeType || "audio/webm" });
        const duration = Math.max(1, elapsedRef.current) * 1000;
        if (cancelVoice.current || blob.size < 200 || blob.size > voiceRecordingLimit) {
          if (!cancelVoice.current) reportError(blob.size > voiceRecordingLimit ? "Запись слишком большая" : "Слишком короткое сообщение");
          return;
        }
        const extension = blob.type.includes("mp4") ? "m4a" : blob.type.includes("ogg") ? "ogg" : "webm";
        if(!aliveRef.current)return;const file=new File([blob], `voice.${extension}`, {type:blob.type||"audio/webm"});const url=URL.createObjectURL(file);capturedUrl.current=url;setCaptured({file,kind:"voice",duration,url});
      };
      const tick = window.setInterval(() => {
        elapsedRef.current += 1;
        setElapsed(elapsedRef.current);
        if (elapsedRef.current >= 180) media.stop();
      }, 1000);
      recorder.current = media;
      try { media.start(250); }
      catch {
        window.clearInterval(tick);
        stream.getTracks().forEach(track => track.stop());
        throw new Error("record");
      }
      setRecording(true);
    } catch { reportError("Нет доступа к микрофону"); }
    finally { voiceStartingRef.current = false; }
  }

  function discardVoice() {
    cancelVoice.current = true;
    recorder.current?.stop();
  }

  function stopCircleStream() {
    circleStream.current?.getTracks().forEach(track => track.stop());
    circleStream.current = null;
    if (circleTick.current !== null) window.clearInterval(circleTick.current);
    circleTick.current = null;
  }

  async function startCircle() {
    if (circling || recording || sendingRef.current || composerStore.read(friend.conversationId).busy || circleStartingRef.current) return;
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === "undefined") { reportError("Этот браузер не снимает кружочки"); return; }
    circleStartingRef.current = true;
    setPanel(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: { sampleRate: { ideal: 48_000 }, channelCount: { ideal: 1 } },
        video: { facingMode: "user", width: { ideal: 720, max: 1280 }, height: { ideal: 720, max: 1280 }, frameRate: { ideal: 24, max: 30 } }
      });
      if (!aliveRef.current) { stream.getTracks().forEach(track => track.stop()); return; }
      circleStream.current = stream;
      const mime = ["video/webm;codecs=vp8,opus", "video/webm;codecs=vp9,opus", "video/webm", "video/mp4"].find(item => MediaRecorder.isTypeSupported(item)) || "";
      const options: MediaRecorderOptions = mime ? { mimeType: mime, videoBitsPerSecond: 2_000_000, audioBitsPerSecond: 96_000 } : { videoBitsPerSecond: 2_000_000, audioBitsPerSecond: 96_000 };
      let media: MediaRecorder;
      try { media = new MediaRecorder(stream, options); }
      catch { media = new MediaRecorder(stream); }
      circleChunks.current = [];
      circleCancel.current = false;
      elapsedRef.current = 0;
      setElapsed(0);
      let recordedBytes = 0;
      media.ondataavailable = event => {
        if (!event.data.size) return;
        recordedBytes += event.data.size;
        if (recordedBytes > circleRecordingLimit) {
          if (!circleCancel.current) reportError("Запись слишком большая");
          circleCancel.current = true;
          if (media.state !== "inactive") media.stop();
          return;
        }
        circleChunks.current.push(event.data);
      };
      media.onstop = () => {
        stopCircleStream();
        setCircling(false);
        const blob = new Blob(circleChunks.current, { type: media.mimeType || "video/webm" });
        const duration = Math.min(60, Math.max(1, elapsedRef.current)) * 1000;
        if (circleCancel.current || blob.size < 1000 || blob.size > circleRecordingLimit) {
          if (!circleCancel.current) reportError(blob.size > circleRecordingLimit ? "Запись слишком большая" : "Слишком короткий кружок");
          return;
        }
        const extension = blob.type.includes("mp4") ? "mp4" : "webm";
        if(!aliveRef.current)return;const file=new File([blob], `circle.${extension}`, {type:blob.type||"video/webm"});const url=URL.createObjectURL(file);capturedUrl.current=url;setCaptured({file,kind:"circle",duration,url});
      };
      circleRecorder.current = media;
      circleTick.current = window.setInterval(() => {
        elapsedRef.current += 1;
        setElapsed(elapsedRef.current);
        if (elapsedRef.current >= 60) media.stop();
      }, 1000);
      try { media.start(250); }
      catch {
        stopCircleStream();
        throw new Error("circle");
      }
      setCircling(true);
    } catch {
      stopCircleStream();
      reportError("Нет доступа к камере");
    }
    finally { circleStartingRef.current = false; }
  }

  useEffect(() => {
    const node = previewRef.current;
    const stream = circleStream.current;
    if (!circling || !node || !stream) return;
    node.srcObject = stream;
    void node.play().catch(() => undefined);
  }, [circling]);

  function discardCircle() {
    circleCancel.current = true;
    if (circleRecorder.current && circleRecorder.current.state !== "inactive") circleRecorder.current.stop();
    else { stopCircleStream(); setCircling(false); }
  }

  return (
    <section className="card chat">
      <div className="chat-title"><Avatar kind="user" id={friend.userId} name={personName(friend.username, friend.displayName)} /><h2>{personName(friend.username, friend.displayName)}</h2></div>
      <button className="btn personal-search-toggle" type="button" aria-expanded={historySearchOpen} onClick={()=>setHistorySearchOpen(value=>!value)}><Icon name="search" size={18}/>{historySearchOpen?"Скрыть поиск":historyFiltered?`Поиск и фильтры · ${historyFilterCount}`:"Поиск сообщений"}</button>
      {historySearchOpen&&<div className="personal-history-tools stack">
        <label className="field">Поиск в загруженной истории<input type="search" value={historyQuery} onChange={event => setHistoryQuery(event.target.value)} placeholder="Текст или имя файла" /></label>
        <div className="row"><button className="btn" type="button" onClick={()=>setHistoryFiltersOpen(true)}>Фильтры сообщений{historyKind!=="all"||historyAuthor!=="all"?` · ${Number(historyKind!=="all")+Number(historyAuthor!=="all")}`:""}</button>{historyFiltered&&<button className="btn quiet" type="button" onClick={resetHistoryFilters}>Сбросить</button>}</div>
        {historyFiltered&&<div className="row personal-match-actions"><button className="btn quiet" disabled={!visibleMessages.length} onClick={()=>match(-1)}>Предыдущее совпадение</button><button className="btn quiet" disabled={!visibleMessages.length} onClick={()=>match(1)}>Следующее совпадение</button></div>}
      </div>}
      {historyFiltersOpen&&<Sheet title="Фильтры сообщений" onClose={()=>setHistoryFiltersOpen(false)}><div className="stack chat-filter-sheet">
        <label className="field">Автор<select value={historyAuthor} onChange={event=>setHistoryAuthor(event.target.value)}><option value="all">Все</option><option value="mine">Мои</option><option value="other">Собеседник</option></select></label>
        <label className="field">Содержимое<select value={historyKind} onChange={event=>setHistoryKind(event.target.value)}><option value="all">Всё</option>{[["text","Текст"],["image","Фото"],["file","Файлы"],["voice","Голос"],["circle","Кружки"],["card","Учебные карточки"]].map(([value,title])=><option key={value} value={value}>{title}</option>)}</select></label>
        <p className="muted">Показано {visibleMessages.length} из {messages.length} загруженных сообщений.</p>
        <div className="row chat-sheet-actions"><button className="btn quiet" type="button" onClick={resetHistoryFilters}>Сбросить фильтры</button><button className="btn primary" type="button" onClick={()=>setHistoryFiltersOpen(false)}>Готово</button></div>
      </div></Sheet>}
      {historyFiltered&&<div className="row personal-history-status" role="status"><span className="muted">{historyQuery.trim()&&`«${historyQuery.trim()}» · `}Совпадений: {visibleMessages.length} из {messages.length}{more?" · Есть ранние сообщения":""}. Поиск в загруженной истории.</span>{!historySearchOpen&&<button className="btn quiet" type="button" onClick={resetHistoryFilters}>Сбросить</button>}</div>}
      {copyNotice && <p className="muted" role="status">{copyNotice}</p>}
      {!!historyQuery.trim() && visibleMessages.length === 0 && <p className="muted" role="status">В загруженной истории совпадений нет. Очистите поиск или загрузите более ранние сообщения.</p>}
      <div className="log" ref={logRef} onScroll={onScroll}>
        {more && <button className="btn" type="button" disabled={loadingEarlier} onClick={() => void earlier()}>{loadingEarlier ? "Загрузка…" : "Раньше"}</button>}
        {visibleMessages.map((message, index) => {
          const mine = message.senderId === self;
          const sticker = message.kind === "sticker" && !message.deleted;
          const round = message.kind === "circle" && !message.deleted;
          const previous = visibleMessages[index - 1];
          const grouped = !!previous && sameSocialCluster(previous, message);
          const newDay = !previous || new Date(previous.createdAt).toDateString() !== new Date(message.createdAt).toDateString();
          return (
            <Fragment key={message.messageId}>
            {newDay && <div className="message-day" role="separator">{new Date(message.createdAt).toLocaleDateString("ru-RU", { day: "numeric", month: "long", year: "numeric" })}</div>}
            <article id={`personal-message-${message.messageId}`} tabIndex={-1} data-hold={message.kind} className={"bubble" + (mine ? " mine" : " chat-incoming") + (grouped ? " grouped" : "") + (sticker ? " sticker" : "") + (round ? " round" : "")}
              onPointerDown={() => { heldOpen.current = false; if (holdTimer.current) window.clearTimeout(holdTimer.current); holdTimer.current = window.setTimeout(() => { holdTimer.current = 0; heldOpen.current = true; setOpenMenu(message.messageId); }, 450); }}
              onPointerUp={event => { if (holdTimer.current) window.clearTimeout(holdTimer.current); if (heldOpen.current && !(event.target instanceof Element && event.target.closest(".actions"))) event.preventDefault(); }}
              onPointerLeave={() => { if (holdTimer.current) window.clearTimeout(holdTimer.current); }}
              onClickCapture={event => { if (event.target instanceof Element && event.target.closest(".actions")) return; if (heldOpen.current || openMenu === message.messageId) { event.preventDefault(); event.stopPropagation(); } }}>
              {!mine && !grouped && <Avatar kind="user" id={message.senderId} name={message.senderName} className="message-avatar" />}
              {message.replyTo && <button type="button" className="quote btn quiet" onClick={() => onError(revealQuote(messagesRef.current,message.replyTo!,"personal-message-"))}>{message.replyBody || "Сообщение"}</button>}
              {message.deleted ? <div>Сообщение удалено</div> : (
                <>
                  {message.kind === "image" && message.attachmentId && <PhotoViewer src={api.socialAttachment(message.attachmentId)}/>}
                  {message.kind === "file" && message.attachmentId && <a className="file" href={api.socialAttachment(message.attachmentId)}>{message.fileName || "Документ"}{message.bytes ? ` · ${size(message.bytes)}` : ""}</a>}
                  {message.kind === "voice" && message.attachmentId && <VoiceNote src={api.socialAttachment(message.attachmentId)} duration={message.durationMs} />}
                  {message.kind === "circle" && message.attachmentId && <CircleNote src={api.socialAttachment(message.attachmentId)} duration={message.durationMs} />}
                  {message.kind === "sticker" && <Sticker id={message.body} />}
                  {message.kind === "text" && <div className="text">{message.body}</div>}
                  {message.kind === "card" && <CardView body={message.body} />}
                </>
              )}
              <div className="meta">
                <span className="muted" title={new Date(message.createdAt).toLocaleString("ru-RU")}>{when(message.createdAt)}{message.editedAt && !message.deleted ? " · изменено" : ""}{mine && message.read ? " · прочитано" : ""}</span>
                {!message.deleted && <button className="tool message-action-toggle" type="button" aria-label="Действия с сообщением" title="Действия с сообщением" onClick={() => { setReactFor(null); setOpenMenu(openMenu === message.messageId ? null : message.messageId); }}><Icon name="more" size={16} /></button>}
              </div>
              {message.reactions.length > 0 && (
                <div className="react-chips">
                  {message.reactions.map(item => <button key={item.emoji} type="button" className={item.mine ? "on" : ""} onClick={() => void change(message, item.emoji)}>{reactionMark(item.emoji)} {item.count}</button>)}
                </div>
              )}
              {holdActions(message.kind, mine, message.deleted, openMenu === message.messageId).length > 0 && (
                <div className="actions">
                  {personalCopyText(message) && <button type="button" onClick={() => { const text = personalCopyText(message); if (!text) return; void navigator.clipboard.writeText(text).then(() => { if (aliveRef.current) setCopyNotice("Текст скопирован"); }).catch(() => { if (aliveRef.current) setCopyNotice("Не удалось скопировать. Выделите текст сообщения вручную."); }); }}>Копировать</button>}
                  {holdActions(message.kind, mine, message.deleted, true).includes("reaction") && <button type="button" onClick={() => runHold("reaction", { reply() {}, reaction() { setReactFor(reactFor === message.messageId ? null : message.messageId); }, edit() {}, delete() {} })}>Реакция</button>}
                  {holdActions(message.kind, mine, message.deleted, true).includes("reply") && <button type="button" onClick={() => runHold("reply", { reply() { setOpenMenu(null); composerStore.reply(friend.conversationId, message); refreshComposer(); }, reaction() {}, edit() {}, delete() {} })}>Ответить</button>}
                  {holdActions(message.kind, mine, message.deleted, true).includes("edit") && <button type="button" onClick={() => runHold("edit", { reply() {}, reaction() {}, edit() { setOpenMenu(null); setPanel(null); composerStore.edit(friend.conversationId, message); refreshComposer(); }, delete() {} })}>Изменить</button>}
                  {holdActions(message.kind, mine, message.deleted, true).includes("delete") && <button type="button" onClick={() => runHold("delete", { reply() {}, reaction() {}, edit() {}, delete() { setOpenMenu(null); void remove(message); } })}>Удалить</button>}
                </div>
              )}
              {reactFor === message.messageId && (
                <div className="react">
                  {reactionChoices.map(([code, mark]) => <button key={code} type="button" className={message.reactions.some(item => item.emoji === code && item.mine) ? "on" : ""} onClick={() => void change(message, code)} aria-label={mark}>{mark}</button>)}
                </div>
              )}
            </article>
            </Fragment>
          );
        })}
        {historyError && <div className="banner" role="status">{historyError} <button className="btn" type="button" onClick={() => void pollerRef.current?.poll()}>Обновить</button></div>}
        {emptyChatState(historyLoading, historyError, messages.length) === "loading" && <p className="muted" role="status">Загружаем сообщения…</p>}
        {emptyChatState(historyLoading, historyError, messages.length) === "empty" && <p className="muted">Напишите сообщение, отправьте стикер или кружок.</p>}
      </div>
      {showLatestJump && <button className="btn latest-jump" type="button" onClick={jumpToLatest}>К новым сообщениям</button>}
      {composer.error && <div className="banner composer-error" role="status">{composer.error}</div>}
      {sending && <p className="muted composer-status" role="status">{editing ? "Сохраняем изменение…" : "Отправляем…"}</p>}
      {captured ? <div className="card stack"><h2>Проверьте запись перед отправкой</h2><MediaPlayer src={captured.url} kind={captured.kind} knownDuration={captured.duration}/><div className="row"><button className="btn quiet" disabled={sending} onClick={()=>setCaptured(null)}>Удалить запись</button><button className="btn quiet" disabled={sending} onClick={()=>{const kind=captured.kind;setCaptured(null);if(kind==="circle")void startCircle();else void toggleVoice();}}>Перезаписать</button><button className="btn primary" disabled={sending} onClick={()=>{const file=captured;void sendFile(file.file,file.kind,file.duration).then(sent=>{if(sent)setCaptured(null);});}}>Отправить запись</button></div></div> : circling ? (
        <div className="circle-record">
          <div className="circle live">
            <video ref={previewRef} muted playsInline autoPlay />
            <span className="time">{clock(elapsed * 1000)}</span>
          </div>
          <div className="compose">
            <button className="btn" type="button" onClick={discardCircle}>Отменить</button>
            <button className="btn primary" type="button" onClick={() => { if (circleRecorder.current && circleRecorder.current.state !== "inactive") circleRecorder.current.stop(); }}>Отправить</button>
          </div>
        </div>
      ) : recording ? (
        <div className="compose">
          <span>Запись {clock(elapsed * 1000)}</span>
          <button className="btn" type="button" onClick={discardVoice}>Отменить</button>
          <button className="btn primary" type="button" onClick={() => void toggleVoice()}>Отправить</button>
        </div>
      ) : (
        <>
          {(reply || editing) && (
            <div className="row" style={{ justifyContent: "space-between" }}>
              <span className="muted">{editing ? "Редактирование" : `Ответ · ${reply ? snippet(reply) : ""}`}</span>
              <button className="btn" type="button" onClick={() => { composerStore.cancel(friend.conversationId); refreshComposer(); }}>Отмена</button>
            </div>
          )}
          <form className="compose personal-compose" onSubmit={event => void submit(event)}>
            {!editing && <button className="btn tool" type="button" aria-label="Прикрепить файл, опрос или карточку пары" title="Прикрепить файл, опрос или карточку пары" onClick={() => setPanel(panel === "attach" ? null : "attach")}><Icon name="paperclip" size={18} /></button>}
            {!editing && <button className={"btn tool" + (panel === "emoji" ? " primary" : "")} type="button" aria-label="Смайлы" onClick={() => setPanel(panel === "emoji" ? null : "emoji")}><Icon name="smile" size={18} /></button>}
            <textarea ref={inputRef} rows={1} value={draft} onChange={event => setDraft(event.target.value)} placeholder={editing ? "Новый текст" : "Сообщение"} aria-label="Сообщение" aria-describedby="personal-compose-help" aria-invalid={!!draft.trim() && !personalTextValid(draft)}
              onCompositionStart={() => { composingRef.current = true; }} onCompositionEnd={() => { composingRef.current = false; }}
              onKeyDown={event => { if (sendOnEnter(event.key, event.shiftKey, composingRef.current || event.nativeEvent.isComposing, event.keyCode === 229, desktopKeyboard)) { event.preventDefault(); event.currentTarget.form?.requestSubmit(); } }} />
            {draft.trim() || editing
              ? <button className="btn primary" type="submit" disabled={sending || !personalTextValid(draft)}>{sending ? "Отправляем…" : editing ? "Сохранить" : "Отправить"}</button>
              : <>
                  <button className="btn tool" type="button" aria-label="Записать кружок" title="Записать кружок" disabled={sending} onClick={() => void startCircle()}><Icon name="circle" size={18} /></button>
                  <button className="btn primary tool" type="button" aria-label="Записать голосовое" title="Записать голосовое" disabled={sending} onClick={() => { setPanel(null); void toggleVoice(); }}><Icon name="mic" size={18} /></button>
                </>}
          </form>
          <div className="composer-help muted" id="personal-compose-help">
            {desktopKeyboard && <span>Enter — отправить · Shift+Enter — новая строка</span>}
            {draftCount >= 1800 && <span className={draftCount > personalTextLimit ? "composer-limit-error" : ""} role="status">{draftCount}/{personalTextLimit}{draftCount > personalTextLimit ? " · сократите текст" : ""}</span>}
            {!!draft.trim() && draftCount <= personalTextLimit && !personalTextValid(draft) && <span role="status">Уберите недопустимые символы из текста.</span>}
          </div>
          {panel && <Sheet title={panel === "attach" ? "Прикрепить" : panel === "emoji" ? "Смайлы" : panel === "study" ? "Расписание и домашка" : "Стикеры"} onClose={()=>setPanel(null)}><div className="personal-composer-panel">
          {panel === "attach" && (
            <div className="actions">
              <button type="button" onClick={() => { setPanel(null); photoRef.current?.click(); }}>Фото</button>
              <button type="button" onClick={() => { setPanel(null); fileRef.current?.click(); }}>Документ</button>
              <button type="button" onClick={() => setPanel("stickers")}>Стикеры</button>
              <button type="button" onClick={() => { setPanel(null); void startCircle(); }}>Кружок</button>
              <button type="button" onClick={() => setPanel("study")}>Расписание и домашка</button>
            </div>
          )}
          {panel === "emoji" && <EmojiPanel onPick={emoji=>{setPanel(null);insertEmoji(emoji);}} />}
          {panel === "study" && <StudyShelf onSend={body => void sendCard(body)} />}
          {panel === "stickers" && (
            <div className="picker sticker-grid">
              {stickerPack.map(item => <button key={item.id} type="button" onClick={() => void sendSticker(item.id)} disabled={sending} aria-label={item.title}><Sticker id={item.id} /><span>{item.title}</span></button>)}
            </div>
          )}
          </div></Sheet>}
        </>
      )}
      <input ref={photoRef} className="hidden-file" type="file" accept="image/jpeg,image/png,image/webp,image/gif,image/bmp" aria-label="Фото" onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; void sendFile(file, "image"); }} />
      <input ref={fileRef} className="hidden-file" type="file" accept=".pdf,.txt,.csv,.rtf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.odt,.ods,.odp,.zip" aria-label="Документ" onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; void sendFile(file, "file"); }} />
    </section>
  );
}
