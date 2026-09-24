import { FormEvent, UIEvent, useCallback, useEffect, useRef, useState } from "react";
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
import { createSocialPoller, mergeSocialMessages } from "./socialChat";
import type { SocialFriend, SocialHome, SocialMessage } from "./types";

function personName(username: string, displayName: string | null) {
  return displayName?.trim() || username;
}

function when(value: string) {
  return value.slice(0, 16).replace("T", " ");
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

export function PeoplePanel({ initialConversationId }: { initialConversationId?: string } = {}) {
  const app = useApp();
  const [home, setHome] = useState<SocialHome | null>(null);
  const [active, setActive] = useState<SocialFriend | null>(null);
  const [code, setCode] = useState("");
  const [error, setError] = useState("");
  const [copied, setCopied] = useState(false);
  const [busy, setBusy] = useState(false);
  const homeRevision = useRef(0);
  const activeIdRef = useRef<string | null>(null);
  const openedFromRoute = useRef<string | null>(null);
  const activeId = active?.conversationId ?? null;
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
          if (initialConversationId && openedFromRoute.current !== initialConversationId) {
            openedFromRoute.current = initialConversationId;
            const selected = value.friends.find(item => item.conversationId === initialConversationId);
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
  }, [app.session, initialConversationId]);

  async function invite(event: FormEvent) {
    event.preventDefault();
    if (!code.trim() || busy) return;
    homeRevision.current += 1;
    setBusy(true);
    setError("");
    try {
      setHome(await api.socialInvite(code.trim()));
      homeRevision.current += 1;
      setCode("");
    } catch (reason) {
      setError(explain(reason, "Не удалось добавить по коду"));
    } finally { setBusy(false); }
  }

  async function answer(friendshipId: string, accept: boolean) {
    homeRevision.current += 1;
    setError("");
    try {
      setHome(await (accept ? api.socialAccept(friendshipId) : api.socialDecline(friendshipId)));
      homeRevision.current += 1;
    }
    catch { setError(accept ? "Не удалось принять запрос" : "Не удалось отклонить запрос"); }
  }

  async function copy() {
    if (!home) return;
    try {
      await navigator.clipboard.writeText(home.code);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1600);
    } catch { setCopied(false); setError("Скопируйте код вручную"); }
  }

  if (!app.session?.authenticated) {
    return <div className="card"><h2>Переписка</h2><p className="muted">Войдите в аккаунт, чтобы получить свой код и писать другим. Расписание и карты работают и без входа.</p></div>;
  }

  return (
    <div className="stack">
      {error && <div className="banner">{error}</div>}
      {!!home?.incoming.length && (
        <div className="stack">
          <h2>Входящие запросы</h2>
          {home.incoming.map(item => (
            <article className="card row" key={item.friendshipId} style={{ justifyContent: "space-between" }}>
              <div><b>{personName(item.username, item.displayName)}</b><div className="muted">@{item.username}</div></div>
              <div className="row">
                <button className="btn primary" type="button" onClick={() => void answer(item.friendshipId, true)}>Принять</button>
                <button className="btn" type="button" onClick={() => void answer(item.friendshipId, false)}>Отклонить</button>
              </div>
            </article>
          ))}
        </div>
      )}
      {!!home?.outgoing.length && (
        <div className="stack">
          <h2>Ожидают ответа</h2>
          {home.outgoing.map(item => (
            <article className="card" key={item.friendshipId}><b>{personName(item.username, item.displayName)}</b><div className="muted">Запрос отправлен · @{item.username}</div></article>
          ))}
        </div>
      )}
      <div className={"grid-2 split" + (active ? " focus" : "")}>
        <div className="split-list stack">
          <article className="card">
            <h2>Ваш код</h2>
            <div className="row" style={{ justifyContent: "space-between" }}>
              <span className="code">{home?.code || "……"}</span>
              <button className="btn" type="button" onClick={() => void copy()} disabled={!home}>{copied ? "Скопирован" : "Скопировать"}</button>
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
          {(home?.friends || []).map(friend => (
            <button className="person" key={friend.userId} type="button" onClick={() => { activeIdRef.current = friend.conversationId; setError(""); setActive(friend); }}>
              <span><b>{personName(friend.username, friend.displayName)}</b><div className="muted">{friend.lastBody || "Нет сообщений"}</div></span>
              {friend.unread > 0 && <span className="chip">{friend.unread}</span>}
            </button>
          ))}
          {home && home.friends.length === 0 && <div className="empty">Пока никого нет. Добавьте человека по коду.</div>}
          </div>
        </div>
        {active ? <section className="split-detail"><button className="btn back-only" type="button" onClick={() => { activeIdRef.current = null; setError(""); setActive(null); }}>К списку</button><Chat key={active.conversationId} friend={active} self={app.session.user?.userId || ""} onError={chatError} /></section> : <section className="card chat split-detail"><h2>Чат</h2><p className="muted">Выберите человека в списке.</p></section>}
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

function VoiceNote({ src, duration }: { src: string; duration: number | null }) {
  const audio = useRef<HTMLAudioElement>(null);
  const [playing, setPlaying] = useState(false);
  const [known, setKnown] = useState(duration);
  return (
    <div className="voice">
      <button className="btn" type="button" onClick={() => { const node = audio.current; if (!node) return; if (playing) node.pause(); else void node.play(); }}>{playing ? "Пауза" : "Слушать"}</button>
      <span>{clock(known || 0)}</span>
      <audio ref={audio} src={src} preload="metadata" onPlay={() => setPlaying(true)} onPause={() => setPlaying(false)} onEnded={() => setPlaying(false)} onLoadedMetadata={event => { if (event.currentTarget.duration && Number.isFinite(event.currentTarget.duration)) setKnown(event.currentTarget.duration * 1000); }} />
    </div>
  );
}

function CircleNote({ src, duration }: { src: string; duration: number | null }) {
  const video = useRef<HTMLVideoElement>(null);
  const [playing, setPlaying] = useState(false);
  const [progress, setProgress] = useState(0);
  const [known, setKnown] = useState(duration);
  const radius = 96;
  const length = 2 * Math.PI * radius;
  function toggle() {
    const node = video.current;
    if (!node) return;
    if (playing) node.pause();
    else { node.muted = false; void node.play(); }
  }
  return (
    <div className="circle">
      <svg className="ring" viewBox="0 0 200 200" aria-hidden="true">
        <circle cx="100" cy="100" r={radius} />
        <circle className="progress" cx="100" cy="100" r={radius} strokeDasharray={length} strokeDashoffset={length * (1 - progress)} />
      </svg>
      <video
        ref={video}
        src={src}
        playsInline
        preload="metadata"
        onPlay={() => setPlaying(true)}
        onPause={() => setPlaying(false)}
        onEnded={() => { setPlaying(false); setProgress(0); }}
        onTimeUpdate={event => { const node = event.currentTarget; if (node.duration) setProgress(node.currentTime / node.duration); }}
        onLoadedMetadata={event => { if (Number.isFinite(event.currentTarget.duration)) setKnown(event.currentTarget.duration * 1000); }}
      />
      <button type="button" className="circle-hit" aria-label={playing ? "Пауза" : "Смотреть кружок"} onClick={toggle} />
      {!playing && <span className="play">▶</span>}
      <span className="time">{clock(known || 0)}</span>
    </div>
  );
}

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

function Chat({ friend, self, onError }: { friend: SocialFriend; self: string; onError: (text: string) => void }) {
  const [messages, setMessages] = useState<SocialMessage[]>([]);
  const [more, setMore] = useState(false);
  const [loadingEarlier, setLoadingEarlier] = useState(false);
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const [reply, setReply] = useState<SocialMessage | null>(null);
  const holdTimer = useRef<number | null>(null);
  const heldOpen = useRef(false);
  const [editing, setEditing] = useState<SocialMessage | null>(null);
  const [recording, setRecording] = useState(false);
  const [circling, setCircling] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [panel, setPanel] = useState<null | "emoji" | "stickers" | "attach" | "study">(null);
  const [openMenu, setOpenMenu] = useState<string | null>(null);
  const [reactFor, setReactFor] = useState<string | null>(null);
  const logRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
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
  const replyRef = useRef<string | undefined>(undefined);
  const messagesRef = useRef<SocialMessage[]>([]);
  const pollerRef = useRef<ReturnType<typeof createSocialPoller> | null>(null);
  const aliveRef = useRef(false);
  const sendingRef = useRef(false);
  const earlierRef = useRef(false);
  const voiceStartingRef = useRef(false);
  const circleStartingRef = useRef(false);
  replyRef.current = reply?.messageId;

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

  function beginSend() {
    if (!aliveRef.current || sendingRef.current) return false;
    sendingRef.current = true;
    setSending(true);
    pollerRef.current?.changed();
    return true;
  }

  function endSend() {
    sendingRef.current = false;
    if (aliveRef.current) setSending(false);
  }

  useEffect(() => {
    aliveRef.current = true;
    stick.current = true;
    messagesRef.current = [];
    setMessages([]);
    setMore(false);
    setDraft("");
    setReply(null);
    setEditing(null);
    setPanel(null);
    setOpenMenu(null);
    setReactFor(null);
    const poller = createSocialPoller(
      before => api.socialMessages(friend.conversationId, before),
      () => messagesRef.current,
      (page, firstLoad) => {
        if (firstLoad) setMore(page.hasMore);
        addMessages(page.messages);
      },
      () => reportError("Чат не обновился"),
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
  }, [friend.conversationId, onError]);

  useEffect(() => {
    if (stick.current && logRef.current) logRef.current.scrollTop = logRef.current.scrollHeight;
  }, [messages]);

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
    const body = draft.trim();
    if (!body || !beginSend()) return;
    const sentDraft = draft;
    const target = editing;
    const replyTo = reply?.messageId;
    try {
      const message = target
        ? await api.socialEdit(friend.conversationId, target.messageId, body)
        : await api.socialText(friend.conversationId, body, replyTo);
      if (!aliveRef.current) return;
      pollerRef.current?.changed();
      addMessages([message]);
      setDraft(current => current === sentDraft ? "" : current);
      setReply(current => current?.messageId === replyTo ? null : current);
      if (target) setEditing(current => current?.messageId === target.messageId ? null : current);
      stick.current = true;
    } catch {
      reportError(target ? "Изменение не сохранилось" : "Сообщение не отправилось");
    } finally { endSend(); }
  }

  async function sendFile(file: File | undefined, kind: "image" | "file" | "voice" | "circle", durationMs?: number) {
    if (!file || !beginSend()) return;
    const replyTo = replyRef.current;
    reportError("");
    try {
      const message = await api.socialUpload(friend.conversationId, file, kind, { replyTo, durationMs });
      if (!aliveRef.current) return;
      pollerRef.current?.changed();
      setReply(current => current?.messageId === replyTo ? null : current);
      addMessages([message]);
      stick.current = true;
    } catch (reason) {
      reportError(explain(reason, kind === "image" ? "Фото не отправилось" : kind === "voice" ? "Голосовое не отправилось" : kind === "circle" ? "Кружок не отправился" : "Документ не отправился"));
    } finally { endSend(); }
  }

  function insertEmoji(emoji: string) {
    const input = inputRef.current;
    const start = input?.selectionStart ?? draft.length;
    const end = input?.selectionEnd ?? draft.length;
    const next = draft.slice(0, start) + emoji + draft.slice(end);
    if (next.length > 2000) return;
    setDraft(next);
    const caret = start + emoji.length;
    requestAnimationFrame(() => { input?.focus(); input?.setSelectionRange(caret, caret); });
  }

  async function sendCard(body: string | null) {
    if (!body) { reportError("Нечего отправить"); return; }
    if (!beginSend()) return;
    const replyTo = replyRef.current;
    setPanel(null);
    try {
      const message = await api.socialCard(friend.conversationId, body, replyTo);
      if (!aliveRef.current) return;
      pollerRef.current?.changed();
      addMessages([message]);
      setReply(current => current?.messageId === replyTo ? null : current);
      stick.current = true;
    } catch { reportError("Карточка не отправилась"); }
    finally { endSend(); }
  }

  async function sendSticker(id: string) {
    if (!beginSend()) return;
    const replyTo = replyRef.current;
    setPanel(null);
    try {
      const message = await api.socialSticker(friend.conversationId, id, replyTo);
      if (!aliveRef.current) return;
      pollerRef.current?.changed();
      addMessages([message]);
      setReply(current => current?.messageId === replyTo ? null : current);
      stick.current = true;
    } catch { reportError("Стикер не отправился"); }
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
    if (circling || sendingRef.current || voiceStartingRef.current) return;
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === "undefined") { reportError("Этот браузер не записывает голос"); return; }
    voiceStartingRef.current = true;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      if (!aliveRef.current) { stream.getTracks().forEach(track => track.stop()); return; }
      const mime = ["audio/webm;codecs=opus", "audio/webm", "audio/ogg;codecs=opus", "audio/mp4"].find(item => MediaRecorder.isTypeSupported(item)) || "";
      const options: MediaRecorderOptions = mime ? { mimeType: mime, audioBitsPerSecond: 24000 } : { audioBitsPerSecond: 24000 };
      let media: MediaRecorder;
      try { media = new MediaRecorder(stream, options); }
      catch { media = new MediaRecorder(stream); }
      chunks.current = [];
      cancelVoice.current = false;
      elapsedRef.current = 0;
      setElapsed(0);
      media.ondataavailable = event => { if (event.data.size) chunks.current.push(event.data); };
      media.onstop = () => {
        stream.getTracks().forEach(track => track.stop());
        window.clearInterval(tick);
        setRecording(false);
        const blob = new Blob(chunks.current, { type: media.mimeType || "audio/webm" });
        const duration = Math.max(1, elapsedRef.current) * 1000;
        if (cancelVoice.current || blob.size < 200) { if (!cancelVoice.current) reportError("Слишком короткое сообщение"); return; }
        const extension = blob.type.includes("mp4") ? "m4a" : blob.type.includes("ogg") ? "ogg" : "webm";
        void sendFile(new File([blob], `voice.${extension}`, { type: blob.type || "audio/webm" }), "voice", duration);
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
    if (circling || recording || sendingRef.current || circleStartingRef.current) return;
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === "undefined") { reportError("Этот браузер не снимает кружочки"); return; }
    circleStartingRef.current = true;
    setPanel(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: true,
        video: { facingMode: "user", width: { ideal: 480, max: 640 }, height: { ideal: 480, max: 640 }, frameRate: { ideal: 24, max: 30 } }
      });
      if (!aliveRef.current) { stream.getTracks().forEach(track => track.stop()); return; }
      circleStream.current = stream;
      const mime = ["video/webm;codecs=vp8,opus", "video/webm;codecs=vp9,opus", "video/webm", "video/mp4"].find(item => MediaRecorder.isTypeSupported(item)) || "";
      const options: MediaRecorderOptions = mime ? { mimeType: mime, videoBitsPerSecond: 450000, audioBitsPerSecond: 24000 } : { videoBitsPerSecond: 450000, audioBitsPerSecond: 24000 };
      let media: MediaRecorder;
      try { media = new MediaRecorder(stream, options); }
      catch { media = new MediaRecorder(stream); }
      circleChunks.current = [];
      circleCancel.current = false;
      elapsedRef.current = 0;
      setElapsed(0);
      media.ondataavailable = event => { if (event.data.size) circleChunks.current.push(event.data); };
      media.onstop = () => {
        stopCircleStream();
        setCircling(false);
        const blob = new Blob(circleChunks.current, { type: media.mimeType || "video/webm" });
        const duration = Math.min(60, Math.max(1, elapsedRef.current)) * 1000;
        if (circleCancel.current || blob.size < 1000) { if (!circleCancel.current) reportError("Слишком короткий кружок"); return; }
        const extension = blob.type.includes("mp4") ? "mp4" : "webm";
        void sendFile(new File([blob], `circle.${extension}`, { type: blob.type || "video/webm" }), "circle", duration);
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
      <h2>{personName(friend.username, friend.displayName)}</h2>
      <div className="log" ref={logRef} onScroll={onScroll}>
        {more && <button className="btn" type="button" disabled={loadingEarlier} onClick={() => void earlier()}>{loadingEarlier ? "Загрузка…" : "Раньше"}</button>}
        {messages.map(message => {
          const mine = message.senderId === self;
          const sticker = message.kind === "sticker" && !message.deleted;
          const round = message.kind === "circle" && !message.deleted;
          return (
            <article key={message.messageId} data-hold={message.kind} className={"bubble" + (mine ? " mine" : "") + (sticker ? " sticker" : "") + (round ? " round" : "")}
              onPointerDown={() => { heldOpen.current = false; if (holdTimer.current) window.clearTimeout(holdTimer.current); holdTimer.current = window.setTimeout(() => { holdTimer.current = 0; heldOpen.current = true; setOpenMenu(message.messageId); }, 450); }}
              onPointerUp={event => { if (holdTimer.current) window.clearTimeout(holdTimer.current); if (heldOpen.current && !(event.target instanceof Element && event.target.closest(".actions"))) event.preventDefault(); }}
              onPointerLeave={() => { if (holdTimer.current) window.clearTimeout(holdTimer.current); }}
              onClickCapture={event => { if (event.target instanceof Element && event.target.closest(".actions")) return; if (heldOpen.current || openMenu === message.messageId) { event.preventDefault(); event.stopPropagation(); } }}>
              {!mine && !sticker && !round && <b>{message.senderName}</b>}
              {message.replyTo && <div className="quote">{message.replyBody || "Сообщение"}</div>}
              {message.deleted ? <div>Сообщение удалено</div> : (
                <>
                  {message.kind === "image" && message.attachmentId && <a href={api.socialAttachment(message.attachmentId)} target="_blank" rel="noreferrer"><img src={api.socialAttachment(message.attachmentId)} alt="Фото" /></a>}
                  {message.kind === "file" && message.attachmentId && <a className="file" href={api.socialAttachment(message.attachmentId)}>{message.fileName || "Документ"}{message.bytes ? ` · ${size(message.bytes)}` : ""}</a>}
                  {message.kind === "voice" && message.attachmentId && <VoiceNote src={api.socialAttachment(message.attachmentId)} duration={message.durationMs} />}
                  {message.kind === "circle" && message.attachmentId && <CircleNote src={api.socialAttachment(message.attachmentId)} duration={message.durationMs} />}
                  {message.kind === "sticker" && <Sticker id={message.body} />}
                  {message.kind === "text" && <div className="text">{message.body}</div>}
                  {message.kind === "card" && <CardView body={message.body} />}
                </>
              )}
              <div className="meta">
                <span className="muted">{when(message.createdAt)}{message.editedAt && !message.deleted ? " · изменено" : ""}{mine && message.read ? " · прочитано" : ""}</span>
                {!message.deleted && <button className="tool" type="button" aria-label="Действия" onClick={() => { setReactFor(null); setOpenMenu(openMenu === message.messageId ? null : message.messageId); }}><Icon name="more" size={16} /></button>}
              </div>
              {message.reactions.length > 0 && (
                <div className="react-chips">
                  {message.reactions.map(item => <button key={item.emoji} type="button" className={item.mine ? "on" : ""} onClick={() => void change(message, item.emoji)}>{reactionMark(item.emoji)} {item.count}</button>)}
                </div>
              )}
              {holdActions(message.kind, mine, message.deleted, openMenu === message.messageId).length > 0 && (
                <div className="actions">
                  {holdActions(message.kind, mine, message.deleted, true).includes("reaction") && <button type="button" onClick={() => runHold("reaction", { reply() {}, reaction() { setReactFor(reactFor === message.messageId ? null : message.messageId); }, edit() {}, delete() {} })}>Реакция</button>}
                  {holdActions(message.kind, mine, message.deleted, true).includes("reply") && <button type="button" onClick={() => runHold("reply", { reply() { setOpenMenu(null); setEditing(null); setReply(message); }, reaction() {}, edit() {}, delete() {} })}>Ответить</button>}
                  {holdActions(message.kind, mine, message.deleted, true).includes("edit") && <button type="button" onClick={() => runHold("edit", { reply() {}, reaction() {}, edit() { setOpenMenu(null); setReply(null); setPanel(null); setEditing(message); setDraft(message.body || ""); }, delete() {} })}>Изменить</button>}
                  {holdActions(message.kind, mine, message.deleted, true).includes("delete") && <button type="button" onClick={() => runHold("delete", { reply() {}, reaction() {}, edit() {}, delete() { setOpenMenu(null); void remove(message); } })}>Удалить</button>}
                </div>
              )}
              {reactFor === message.messageId && (
                <div className="react">
                  {reactionChoices.map(([code, mark]) => <button key={code} type="button" className={message.reactions.some(item => item.emoji === code && item.mine) ? "on" : ""} onClick={() => void change(message, code)} aria-label={mark}>{mark}</button>)}
                </div>
              )}
            </article>
          );
        })}
        {messages.length === 0 && <p className="muted">Напишите сообщение, отправьте стикер или кружок.</p>}
      </div>
      {circling ? (
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
              <button className="btn" type="button" onClick={() => { setReply(null); setEditing(null); if (editing) setDraft(""); }}>Отмена</button>
            </div>
          )}
          <form className="compose" onSubmit={event => void submit(event)}>
            {!editing && <button className="btn tool" type="button" aria-label="Вложения" onClick={() => setPanel(panel === "attach" ? null : "attach")}><Icon name="plus" size={18} /></button>}
            {!editing && <button className={"btn tool" + (panel === "emoji" ? " primary" : "")} type="button" aria-label="Смайлы" onClick={() => setPanel(panel === "emoji" ? null : "emoji")}><Icon name="smile" size={18} /></button>}
            <input ref={inputRef} value={draft} onChange={event => setDraft(event.target.value)} placeholder={editing ? "Новый текст" : "Сообщение"} aria-label="Сообщение" maxLength={2000} />
            {draft.trim() || editing
              ? <button className="btn primary" type="submit" disabled={sending || !draft.trim()}>{editing ? "Сохранить" : "Отправить"}</button>
              : <>
                  <button className="btn tool" type="button" aria-label="Кружок" disabled={sending} onClick={() => void startCircle()}><Icon name="circle" size={18} /></button>
                  <button className="btn primary tool" type="button" aria-label="Голосовое" disabled={sending} onClick={() => { setPanel(null); void toggleVoice(); }}><Icon name="mic" size={18} /></button>
                </>}
          </form>
          {panel === "attach" && (
            <div className="actions">
              <button type="button" onClick={() => { setPanel(null); photoRef.current?.click(); }}>Фото</button>
              <button type="button" onClick={() => { setPanel(null); fileRef.current?.click(); }}>Документ</button>
              <button type="button" onClick={() => setPanel("stickers")}>Стикеры</button>
              <button type="button" onClick={() => { setPanel(null); void startCircle(); }}>Кружок</button>
              <button type="button" onClick={() => setPanel("study")}>Расписание и домашка</button>
            </div>
          )}
          {panel === "emoji" && <EmojiPanel onPick={insertEmoji} />}
          {panel === "study" && <StudyShelf onSend={body => void sendCard(body)} />}
          {panel === "stickers" && (
            <div className="picker sticker-grid">
              {stickerPack.map(item => <button key={item.id} type="button" onClick={() => void sendSticker(item.id)} disabled={sending} aria-label={item.title}><Sticker id={item.id} /><span>{item.title}</span></button>)}
            </div>
          )}
        </>
      )}
      <input ref={photoRef} className="hidden-file" type="file" accept="image/jpeg,image/png,image/webp,image/gif,image/bmp" aria-label="Фото" onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; void sendFile(file, "image"); }} />
      <input ref={fileRef} className="hidden-file" type="file" accept=".pdf,.txt,.csv,.rtf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.odt,.ods,.odp,.zip" aria-label="Документ" onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; void sendFile(file, "file"); }} />
    </section>
  );
}
