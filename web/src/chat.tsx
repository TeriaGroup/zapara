import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import * as api from "./api";
import { filterChatInbox, mergeChatInbox, sortChatInbox, unreadChatTotal, type ChatInboxItem } from "./chatInbox";
import { PeoplePanel } from "./people";
import { useApp } from "./store";
import type { GroupHome, SocialHome } from "./types";

function destination(item: ChatInboxItem): string {
  if (item.kind === "personal") return `/chat/person/${encodeURIComponent(item.conversationId)}`;
  return `/group?communityId=${encodeURIComponent(item.communityId || "")}&conversationId=${encodeURIComponent(item.conversationId)}`;
}

function inboxTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return date.toDateString() === new Date().toDateString()
    ? date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })
    : date.toLocaleDateString("ru-RU", { day: "numeric", month: "short", year: "numeric" });
}

const inboxKinds: { value: ChatInboxItem["kind"] | "all"; label: string }[] = [
  { value: "all", label: "Все" }, { value: "group", label: "Группы" },
  { value: "classmate", label: "Одногруппники" }, { value: "personal", label: "Друзья по коду" },
];

function inboxSource(kind: ChatInboxItem["kind"]): string {
  return kind === "group" ? "Группа" : kind === "classmate" ? "Чат одногруппника" : "Друг по коду";
}

export function ChatInboxPage() {
  const app = useApp();
  const [rows, setRows] = useState<ChatInboxItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState<ChatInboxItem["kind"] | "all">("all");
  const accountId = app.session?.authenticated ? app.session.user?.userId : null;
  const visible = filterChatInbox(rows, query, kind);
  const unread = unreadChatTotal(rows);
  const filtered = !!query.trim() || kind !== "all";

  useEffect(() => {
    if (!accountId) { setRows([]); setLoading(false); return; }
    let stopped = false;
    let running = false;
    const refresh = async () => {
      if (running) return;
      running = true;
      const [memberships, social] = await Promise.allSettled([api.communities(), api.socialHome()]);
      const joined = memberships.status === "fulfilled" ? memberships.value.filter(item => item.role) : [];
      const homes = await Promise.allSettled(joined.map(item => api.groupHome(item.communityId)));
      if (!stopped) {
        const groups: GroupHome[] = homes.flatMap(item => item.status === "fulfilled" ? [item.value] : []);
        const people: SocialHome | null = social.status === "fulfilled" ? social.value : null;
        const fresh = mergeChatInbox(groups, people);
        const failedGroups = new Set(joined.filter((_, index) => homes[index]?.status === "rejected").map(item => item.communityId));
        setRows(previous => sortChatInbox([
          ...fresh,
          ...previous.filter(item => item.kind === "personal"
            ? social.status === "rejected"
            : memberships.status === "rejected" || !!item.communityId && failedGroups.has(item.communityId)),
        ]));
        const missing = memberships.status === "rejected" || social.status === "rejected" || homes.some(item => item.status === "rejected");
        setError(missing ? "Часть бесед не загрузилась. Можно повторить." : "");
        setLoading(false);
      }
      running = false;
    };
    setLoading(true);
    void refresh();
    const timer = window.setInterval(() => void refresh(), 10_000);
    return () => { stopped = true; window.clearInterval(timer); };
  }, [accountId, retry]);

  if (!accountId) return <section className="page"><div className="card empty">
    <h1>Чат</h1><p>Войдите в аккаунт, чтобы переписываться с группой и другими людьми.</p>
    <Link className="btn primary" to="/settings">Открыть настройки</Link>
  </div></section>;

  return <section className="page inbox">
    <div className="page-head"><div><h1>Чат</h1><p className="sub">Сообщения группы и личные беседы в одном месте.</p></div>
      {unread > 0 && <span className="chip inbox-total" aria-label={`Непрочитанных сообщений: ${unread}`}>Непрочитано: {unread > 99 ? "99+" : unread}</span>}</div>
    <div className="row">
      <Link className="btn" to="/chat/people">Код, запросы и люди</Link>
      <button className="btn" type="button" onClick={() => setRetry(value => value + 1)}>Обновить</button>
    </div>
    {error && <div className="banner" role="status">{error}</div>}
    {rows.length > 0 && <div className="card stack inbox-browse">
      <label className="field">Поиск беседы
        <input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Название или последнее сообщение" />
      </label>
      <div className="row" role="group" aria-label="Источник беседы">
        {inboxKinds.map(option => <button key={option.value} className={kind === option.value ? "btn primary" : "btn"} type="button"
          aria-pressed={kind === option.value} onClick={() => setKind(option.value)}>{option.label}</button>)}
      </div>
      <div className="row"><span className="muted">Показано {visible.length} из {rows.length}</span>
        {filtered && <button className="btn" type="button" onClick={() => { setQuery(""); setKind("all"); }}>Сбросить</button>}
      </div>
    </div>}
    {loading && rows.length === 0 && <p className="muted">Загружаем беседы…</p>}
    {!loading && rows.length === 0 && <div className="card empty">Пока нет бесед. Вступите в учебную группу или добавьте человека по коду.</div>}
    {rows.length > 0 && visible.length === 0 && <div className="card empty">По запросу бесед нет.
      <button className="btn" type="button" onClick={() => { setQuery(""); setKind("all"); }}>Показать все</button>
    </div>}
    <div className="people">
      {visible.map(item => <Link className="person" key={`${item.kind}:${item.conversationId}`} to={destination(item)}>
        <span><span className="muted inbox-source">{inboxSource(item.kind)}</span><b>{item.title}</b>
          <span className="muted">{item.preview || (item.kind === "group" ? "Чат группы" : "Нет сообщений")}</span></span>
        <span className="inbox-meta">{item.lastAt && <span className="muted" title={new Date(item.lastAt).toLocaleString("ru-RU")}>{inboxTime(item.lastAt)}</span>}
          {item.unread > 0 && <span className="chip" aria-label={`Непрочитанных сообщений: ${item.unread}`}>{item.unread > 99 ? "99+" : item.unread}</span>}</span>
      </Link>)}
    </div>
  </section>;
}

export function PersonalChatPage() {
  const { conversationId } = useParams();
  return <section className="page"><div className="page-head"><h1>Личные чаты</h1></div>
    <Link className="btn" to="/chat">Ко всем беседам</Link>
    <PeoplePanel initialConversationId={conversationId} />
  </section>;
}
