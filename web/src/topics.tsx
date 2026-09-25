import { FormEvent, useEffect, useRef, useState } from "react";
import * as api from "./api";
import { channelAccentColor, filterTopics, nextUnreadTopic, topicPreview } from "./channels";
import { unreadBadgeDescription, unreadBadgeText } from "./groupBrowse";
import type { ChannelAccent, ChannelWritePolicy, GroupTopic, GroupTopicPage } from "./types";

const icons = ["📌", "💬", "🗳️", "📅", "📚", "💻", "📎", "❗", "🏀", "🧪", "✏️", "🎵", "🌍"];
const accentChoices: { code: ChannelAccent; title: string }[] = [
  { code: "default", title: "По умолчанию" },
  { code: "blue", title: "Синий" },
  { code: "green", title: "Зелёный" },
  { code: "purple", title: "Фиолетовый" },
  { code: "orange", title: "Оранжевый" },
  { code: "red", title: "Красный" },
];

function ChannelSettings({ kind, description, accent, pinned, writePolicy, onDescription, onAccent, onPinned, onWritePolicy }: {
  kind: "chat" | "ballots";
  description: string;
  accent: ChannelAccent;
  pinned: boolean;
  writePolicy: ChannelWritePolicy;
  onDescription: (value: string) => void;
  onAccent: (value: ChannelAccent) => void;
  onPinned: (value: boolean) => void;
  onWritePolicy: (value: ChannelWritePolicy) => void;
}) {
  return <>
    <label className="field">Описание
      <textarea value={description} onChange={event => onDescription(event.target.value)} maxLength={240} rows={2} placeholder="О чём этот раздел" aria-label="Описание раздела" />
    </label>
    <div className="channel-accents" role="group" aria-label="Цвет раздела">
      {accentChoices.map(choice => <button key={choice.code} className={accent === choice.code ? "channel-accent selected" : "channel-accent"}
        type="button" aria-label={choice.title} aria-pressed={accent === choice.code} onClick={() => onAccent(choice.code)}>
        <span className="channel-accent-dot" style={{ background: channelAccentColor(choice.code, "Раздел") }} />{choice.title}
      </button>)}
    </div>
    <label className="row channel-pin"><input type="checkbox" checked={pinned} onChange={event => onPinned(event.target.checked)} />Закрепить вверху списка</label>
    <label className="field">{kind === "chat" ? "Кто может писать" : "Кто может создавать голосования"}
      <select value={writePolicy} onChange={event => onWritePolicy(event.target.value as ChannelWritePolicy)} aria-label={kind === "chat" ? "Кто может писать в разделе" : "Кто может создавать голосования"}>
        <option value="all">Все участники группы</option>
        <option value="managers">Только управляющие разделами</option>
      </select>
    </label>
  </>;
}

function when(iso: string | null) {
  if (!iso) return "";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  const same = date.toDateString() === new Date().toDateString();
  return same
    ? date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })
    : date.toLocaleDateString("ru-RU", { day: "numeric", month: "short" });
}

export function GroupTopics({ communityId, onOpen, onError }: {
  communityId: string;
  onOpen: (topic: GroupTopic, canManageChannels: boolean) => void;
  onError: (text: string) => void;
}) {
  const [page, setPage] = useState<GroupTopicPage>({ topics: [], canManageChannels: false });
  const [title, setTitle] = useState("");
  const [icon, setIcon] = useState("📚");
  const [kind, setKind] = useState<"chat" | "ballots">("chat");
  const [description, setDescription] = useState("");
  const [accent, setAccent] = useState<ChannelAccent>("default");
  const [pinned, setPinned] = useState(false);
  const [writePolicy, setWritePolicy] = useState<ChannelWritePolicy>("all");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState("");
  const [editIcon, setEditIcon] = useState("💬");
  const [editKind, setEditKind] = useState<"chat" | "ballots">("chat");
  const [editDescription, setEditDescription] = useState("");
  const [editAccent, setEditAccent] = useState<ChannelAccent>("default");
  const [editPinned, setEditPinned] = useState(false);
  const [editWritePolicy, setEditWritePolicy] = useState<ChannelWritePolicy>("all");
  const [busy, setBusy] = useState(false);
  const [nextBusy, setNextBusy] = useState(false);
  const [off, setOff] = useState(false);
  const [loading, setLoading] = useState(true);
  const [reloadEpoch, setReloadEpoch] = useState(0);
  const [search, setSearch] = useState("");
  const [kindFilter, setKindFilter] = useState<"all" | "chat" | "ballots">("all");
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [manageOpen, setManageOpen] = useState(false);
  const requestEpoch = useRef(0);
  const lastCommunity = useRef(communityId);
  const visible = filterTopics(page.topics, { query: search, kind: kindFilter, unreadOnly });
  const filtered = !!search.trim() || kindFilter !== "all" || unreadOnly;
  const nextUnread = nextUnreadTopic(page.topics);

  useEffect(() => {
    let stop = false;
    requestEpoch.current += 1;
    if (lastCommunity.current !== communityId) {
      lastCommunity.current = communityId;
      setPage({ topics: [], canManageChannels: false });
      setSearch("");
      setKindFilter("all");
      setUnreadOnly(false);
      setManageOpen(false);
      setEditingId(null);
    }
    setLoading(true);
    const pull = () => {
      const ticket = ++requestEpoch.current;
      void api.topics(communityId).then(next => {
        if (stop || ticket !== requestEpoch.current) return;
        setPage(next);
        setOff(false);
        setLoading(false);
      }).catch(() => { if (!stop && ticket === requestEpoch.current) { setOff(true); setLoading(false); } });
    };
    pull();
    const timer = window.setInterval(pull, 4000);
    return () => { stop = true; requestEpoch.current += 1; window.clearInterval(timer); };
  }, [communityId, reloadEpoch]);

  function create(event: FormEvent) {
    event.preventDefault();
    const name = title.trim();
    if (!page.canManageChannels || name.length < 2 || busy) return;
    setBusy(true);
    void api.createTopic(communityId, name, icon, kind, { description: description.trim(), accent, pinned, writePolicy }).then(next => {
      requestEpoch.current += 1;
      setPage(next);
      setTitle("");
      setDescription("");
      setAccent("default");
      setPinned(false);
      setWritePolicy("all");
      setManageOpen(false);
    }).catch(() => onError("Не получилось создать раздел")).finally(() => setBusy(false));
  }

  function rename(event: FormEvent) {
    event.preventDefault();
    const name = editTitle.trim();
    if (!page.canManageChannels || !editingId || name.length < 2 || busy) return;
    setBusy(true);
    void api.renameTopic(communityId, editingId, name, editIcon, editKind, {
      description: editDescription.trim(), accent: editAccent, pinned: editPinned, writePolicy: editWritePolicy,
    }).then(next => {
      requestEpoch.current += 1;
      setPage(next);
      setEditingId(null);
    }).catch(() => onError("Не получилось изменить раздел")).finally(() => setBusy(false));
  }

  function openNextUnread() {
    if (busy || nextBusy || !nextUnread) return;
    const ticket = requestEpoch.current;
    setNextBusy(true);
    void api.topics(communityId).then(fresh => {
      if (ticket !== requestEpoch.current || lastCommunity.current !== communityId) return;
      setPage(fresh);
      const target = nextUnreadTopic(fresh.topics);
      if (target) onOpen(target, fresh.canManageChannels);
      else onError("Непрочитанных каналов нет");
    }).catch(() => { if (ticket === requestEpoch.current && lastCommunity.current === communityId) onError("Не удалось проверить непрочитанные каналы"); })
      .finally(() => setNextBusy(false));
  }

  return (
    <div className="topics">
      <p className="muted">Разделы группы: чаты с сообщениями и файлами или отдельные каналы для голосований. Общий поток остаётся наверху.</p>
      <label className="field">Поиск раздела
        <input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Название или описание" aria-label="Поиск раздела" />
      </label>
      <div className="row topic-filters" role="group" aria-label="Фильтр разделов">
        {([ ["all", "Все"], ["chat", "Чаты"], ["ballots", "Голосования"] ] as const).map(([value, label]) =>
          <button key={value} className={kindFilter === value ? "btn primary" : "btn"} type="button"
            aria-pressed={kindFilter === value} onClick={() => setKindFilter(value)}>{label}</button>)}
        <button className={unreadOnly ? "btn primary" : "btn"} type="button" aria-pressed={unreadOnly}
          onClick={() => setUnreadOnly(value => !value)}>Непрочитанные</button>
      </div>
      <div className="row">
        <span className="muted">Показано {visible.length} из {page.topics.length}</span>
        <button className="btn" type="button" disabled={!nextUnread || loading || off || busy || nextBusy}
          onClick={openNextUnread}>{nextBusy ? "Проверяем…" : "Следующий непрочитанный канал"}</button>
        {!nextUnread && !loading && !off && <span className="muted">Непрочитанных каналов нет</span>}
        {filtered && <button className="btn" type="button" onClick={() => { setSearch(""); setKindFilter("all"); setUnreadOnly(false); }}>Сбросить фильтры</button>}
        {page.canManageChannels && <button className="btn" type="button" aria-expanded={manageOpen}
          onClick={() => setManageOpen(value => !value)}>{manageOpen ? "Закрыть управление" : "Управлять разделами"}</button>}
      </div>
      {page.canManageChannels && manageOpen && <form className="stack channel-create" onSubmit={create}>
        <h2>Новый раздел</h2>
        <div className="channel-kinds" role="group" aria-label="Тип раздела">
          <button className={kind === "chat" ? "channel-kind selected" : "channel-kind"} type="button" aria-pressed={kind === "chat"} onClick={() => { setKind("chat"); setIcon("💬"); }}>
            <b>💬 Чат</b><span>Сообщения, фото, файлы и записи</span>
          </button>
          <button className={kind === "ballots" ? "channel-kind selected" : "channel-kind"} type="button" aria-pressed={kind === "ballots"} onClick={() => { setKind("ballots"); setIcon("🗳️"); }}>
            <b>🗳️ Голосования</b><span>Вопрос и варианты ответа без переписки</span>
          </button>
        </div>
        <div className="row" aria-label="Значок раздела">
          {icons.map(item => <button key={item} className={"btn channel-icon-choice" + (icon === item ? " primary" : "")} type="button" onClick={() => setIcon(item)} aria-label={item} aria-pressed={icon === item}>{item}</button>)}
        </div>
        <div className="row">
          <input value={title} onChange={event => setTitle(event.target.value)} placeholder="Название раздела" aria-label="Название раздела" maxLength={40} />
        </div>
        <ChannelSettings kind={kind} description={description} accent={accent} pinned={pinned} writePolicy={writePolicy}
          onDescription={setDescription} onAccent={setAccent} onPinned={setPinned} onWritePolicy={setWritePolicy} />
        <p className="muted">Тип раздела нельзя изменить после создания.</p>
        <button className="btn primary" type="submit" disabled={title.trim().length < 2 || busy}>Создать раздел</button>
      </form>}
      {loading && <p className="muted" role="status">Загрузка разделов…</p>}
      {off && <div className="row" role="alert"><p className="muted">Разделы сейчас не открылись.</p>
        <button className="btn" type="button" onClick={() => setReloadEpoch(value => value + 1)}>Повторить</button></div>}
      {!loading && !off && visible.length === 0 && <div className="empty">{filtered ? "По запросу ничего не найдено" : "Разделов пока нет"}
        {filtered && <button className="btn" type="button" onClick={() => { setSearch(""); setKindFilter("all"); setUnreadOnly(false); }}>Показать все</button>}</div>}
      <div className="topic-list">
        {visible.map(topic => (
          <div className={topic.pinned ? "topic pinned" : "topic"} key={topic.topicId ?? "general"}>
            <div className="topic-row">
              <button className="topic-open" type="button" onClick={() => onOpen(topic, page.canManageChannels)}>
                <span className="topic-icon" style={{ background: channelAccentColor(topic.accent, topic.title) }}>{topic.icon}</span>
                <span className="topic-main">
                  <b>{topic.title} {topic.pinned && <span className="chip">Закреплено</span>} {topic.kind === "ballots" && <span className="chip">Голосования</span>}</b>
                  {topic.description && <span className="topic-description">{topic.description}</span>}
                  <span className="preview">{topicPreview(topic)}</span>
                </span>
                <span className="topic-meta">
                  {when(topic.lastAt)}
                  {topic.unread > 0 && <span className="chip" aria-label={unreadBadgeDescription(topic.unread)}>{unreadBadgeText(topic.unread)}</span>}
                </span>
              </button>
              {page.canManageChannels && manageOpen && topic.topicId && <button className="btn topic-edit" type="button" onClick={() => {
                setEditingId(topic.topicId);
                setEditTitle(topic.title);
                setEditIcon(topic.icon);
                setEditKind(topic.kind);
                setEditDescription(topic.description);
                setEditAccent(topic.accent);
                setEditPinned(topic.pinned);
                setEditWritePolicy(topic.writePolicy);
              }}>Изменить</button>}
            </div>
            {page.canManageChannels && manageOpen && topic.topicId && editingId === topic.topicId && <form className="stack channel-edit" onSubmit={rename}>
              <div className="row" aria-label="Значок раздела">
                {icons.map(item => <button key={item} className={"btn channel-icon-choice" + (editIcon === item ? " primary" : "")} type="button" aria-label={item} aria-pressed={editIcon === item} onClick={() => setEditIcon(item)}>{item}</button>)}
              </div>
              <div className="row">
                <input value={editTitle} onChange={event => setEditTitle(event.target.value)} aria-label="Новое название раздела" maxLength={40} />
              </div>
              <ChannelSettings kind={editKind} description={editDescription} accent={editAccent} pinned={editPinned} writePolicy={editWritePolicy}
                onDescription={setEditDescription} onAccent={setEditAccent} onPinned={setEditPinned} onWritePolicy={setEditWritePolicy} />
              <div className="row">
                <button className="btn primary" type="submit" disabled={editTitle.trim().length < 2 || busy}>Сохранить</button>
                <button className="btn" type="button" onClick={() => setEditingId(null)}>Отмена</button>
                {topic.canDelete && <button className="btn danger" type="button" disabled={busy} onClick={() => {
                  if (!window.confirm(topic.kind === "chat" ? "Удалить раздел и его сообщения?" : "Удалить раздел? Голосования останутся в общем списке.")) return;
                  setBusy(true);
                  void api.deleteTopic(communityId, topic.topicId as string).then(next => { requestEpoch.current += 1; setPage(next); setEditingId(null); })
                    .catch(() => onError("Не получилось удалить раздел")).finally(() => setBusy(false));
                }}>Удалить</button>}
              </div>
            </form>}
          </div>
        ))}
      </div>
    </div>
  );
}
