import { useEffect, useRef, useState } from "react";
import { Link, Navigate, useParams } from "react-router-dom";
import * as api from "./api";
import { S } from "./strings.gen";
import { chatInboxTime, createChatInboxSourceSequence, filterChatInbox, mergeChatInbox, sortChatInbox, unreadChatTotal, type ChatInboxItem } from "./chatInbox";
import { PeoplePanel } from "./people";
import { useApp } from "./store";
import type { GroupHome, SocialHome } from "./types";
import { emptyChatState } from "./personal-composer";
import { Avatar } from "./avatar-view";
import { usePersonalDrafts } from "./personal-composer-context";
import { startVisibleRefresh } from "./visible-refresh";
import { PageHead } from "./page-head";
import { showInboxFilters, singleChat } from "./chat-ui";

function destination(item: ChatInboxItem): string {
  if (item.kind === "personal") return `/chat/person/${encodeURIComponent(item.conversationId)}`;
  return `/group?communityId=${encodeURIComponent(item.communityId || "")}&conversationId=${encodeURIComponent(item.conversationId)}`;
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
  return <ChatInboxContent key={JSON.stringify([app.session?.authenticated, app.session?.user?.userId, app.session?.familyId])} />;
}

function ChatInboxContent() {
  const app = useApp();
  const [rows, setRows] = useState<ChatInboxItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState<ChatInboxItem["kind"] | "all">("all");
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [draftsOnly,setDraftsOnly]=useState(false);
  const [unreadFirst,setUnreadFirst]=useState(false);
  const [filtersExpanded,setFiltersExpanded]=useState(false);
  const drafts=usePersonalDrafts();
  const [failedSources,setFailedSources]=useState<{id:string;name:string}[]>([]);
  const [retryingSource,setRetryingSource]=useState("");
  const alive=useRef(true);useEffect(()=>{alive.current=true;return()=>{alive.current=false;};},[]);
  const ownerRef = useRef<string | null | undefined>(undefined);
  const sourceSequence = useRef<ReturnType<typeof createChatInboxSourceSequence> | null>(null);
  if (!sourceSequence.current) sourceSequence.current = createChatInboxSourceSequence();
  const accountId = app.session?.authenticated ? app.session.user?.userId : null;
  const error = failedSources.length
    ? failedSources.some(item=>item.id==="memberships")&&failedSources.some(item=>item.id==="social")
      ? "Беседы не загрузились. Можно повторить."
      : "Часть бесед не загрузилась. Можно повторить."
    : "";
  const visible = filterChatInbox(rows.map(row=>{const draft=drafts.find(draft=>draft.conversationId===row.conversationId);return draft?{...row,preview:`${draft.editing?"Правка":"Черновик"}: ${draft.text}`} : row;}), query, kind, unreadOnly).filter(row=>!draftsOnly||drafts.some(draft=>draft.conversationId===row.conversationId)).sort((a,b)=>unreadFirst?Number(b.unread>0)-Number(a.unread>0):0);
  const unread = unreadChatTotal(rows);
  const activeFilters = Number(kind !== "all") + Number(unreadOnly) + Number(draftsOnly) + Number(unreadFirst);
  const filtered = !!query.trim() || activeFilters > 0;
  function resetFilters(){setQuery("");setKind("all");setUnreadOnly(false);setDraftsOnly(false);setUnreadFirst(false);}
  async function retrySource(id:string){
    if(retryingSource)return;
    const source=id==="social"?"social":id==="memberships"?"memberships":`group:${id}`;
    const ticket=sourceSequence.current!.begin(source);
    setRetryingSource(id);
    try{
      if(id==="memberships"){setRetry(value=>value+1);return;}
      const fresh=id==="social"?mergeChatInbox([],await api.socialHome()):mergeChatInbox([await api.groupHome(id)],null);
      if(alive.current&&sourceSequence.current!.isCurrent(source,ticket)){
        setRows(previous=>sortChatInbox([...previous.filter(row=>id==="social"?row.kind!=="personal":row.communityId!==id),...fresh]));
        setFailedSources(values=>values.filter(value=>value.id!==id));
      }
    }catch{
      // Keep the last-good rows and the source-level retry banner.
    }finally{if(alive.current)setRetryingSource("");}
  }

  useEffect(() => {
    if (ownerRef.current !== accountId) {
      ownerRef.current = accountId;
      setRows([]); setFailedSources([]); setQuery(""); setKind("all");
    }
    if (!accountId) { setRows([]); setLoading(false); return; }
    let stopped = false;
    const refresh = async () => {
      const sequence = sourceSequence.current!;
      const membershipTicket = sequence.begin("memberships");
      const socialTicket = sequence.begin("social");
      const [memberships, social] = await Promise.allSettled([api.communities(), api.socialHome()]);
      if (stopped) return;
      const membershipCurrent = sequence.isCurrent("memberships", membershipTicket);
      const socialCurrent = sequence.isCurrent("social", socialTicket);
      const joined = membershipCurrent && memberships.status === "fulfilled" ? memberships.value.filter(item => item.role) : [];
      const homeTickets = joined.map(item => sequence.begin(`group:${item.communityId}`));
      const homes = await Promise.allSettled(joined.map(item => api.groupHome(item.communityId)));
      if (stopped) return;
      const homeCurrent = homeTickets.map((ticket, index) => sequence.isCurrent(`group:${joined[index].communityId}`, ticket));
      const groups: GroupHome[] = homes.flatMap((item, index) => homeCurrent[index] && item.status === "fulfilled" ? [item.value] : []);
      const people: SocialHome | null = socialCurrent && social.status === "fulfilled" ? social.value : null;
      const fresh = mergeChatInbox(groups, people);
      const preserveGroups = new Set(joined.filter((_, index) => !homeCurrent[index] || homes[index]?.status === "rejected").map(item => item.communityId));
      const joinedIds = new Set(joined.map(item => item.communityId));
      setFailedSources(current => {
        const next = new Map(current.map(item => [item.id, item]));
        if (membershipCurrent) {
          if (memberships.status === "rejected") next.set("memberships", { id: "memberships", name: "Список моих сообществ" });
          else {
            next.delete("memberships");
            for (const id of next.keys()) if (id !== "social" && !joinedIds.has(id)) next.delete(id);
          }
        }
        if (socialCurrent) {
          if (social.status === "rejected") next.set("social", { id: "social", name: "Личные беседы" });
          else next.delete("social");
        }
        joined.forEach((item, index) => {
          if (!homeCurrent[index]) return;
          if (homes[index]?.status === "rejected") next.set(item.communityId, { id: item.communityId, name: item.name });
          else next.delete(item.communityId);
        });
        return [...next.values()];
      });
      setRows(previous => sortChatInbox([
        ...fresh,
        ...previous.filter(item => item.kind === "personal"
          ? !socialCurrent || social.status === "rejected"
          : !membershipCurrent || memberships.status === "rejected" || !!item.communityId && joinedIds.has(item.communityId) && preserveGroups.has(item.communityId)),
      ]));
      setLoading(false);
    };
    setLoading(true);
    const stopRefresh = startVisibleRefresh(refresh, document, window);
    return () => { stopped = true; stopRefresh(); };
  }, [accountId, retry]);

  if (!accountId) return <section className="page inbox"><PageHead title={S.navChats}/><div className="card empty">
    <p>Войдите в аккаунт, чтобы переписываться с группой и другими людьми.</p>
    <Link className="btn primary" to="/settings?section=account">Открыть настройки аккаунта</Link>
  </div></section>;

  // #17: единственная беседа — групповой чат: «Чат» открывает его сразу. Не при частичной загрузке (баннер источников важнее).
  const only = singleChat(rows, loading, error || (failedSources.length ? "partial" : ""), typeof window === "undefined" ? "" : window.location?.search ?? "");
  if (only?.kind === "group") return <Navigate replace to={destination(only)} />;

  return <section className="page inbox">
    <PageHead title={S.navChats} text="Сообщения группы и личные беседы в одном месте.">
      {unread > 0 && <span className="chip inbox-total" aria-label={`Непрочитанных сообщений: ${unread}`}>Непрочитано: {unread > 99 ? "99+" : unread}</span>}
    </PageHead>
    {/* #17: одна понятная кнопка; список обновляется сам (startVisibleRefresh), ручное «Обновить» — только при ошибке. */}
    <div className="row">
      <Link className="btn" to="/chat/people">Вступить по коду / Новый чат</Link>
    </div>
    {error && <div className="banner row" role="status"><span>{error}</span><button className="btn" type="button" onClick={() => setRetry(value => value + 1)}>Повторить</button></div>}
    {failedSources.map(source=><div className="banner row" key={source.id}><span>{source.name} · показаны ранее загруженные сведения</span><button className="btn" disabled={!!retryingSource} onClick={()=>void retrySource(source.id)}>Повторить этот источник</button></div>)}
    {showInboxFilters(rows.length) && <div className="card stack inbox-browse">
      <label className="field">Поиск беседы
        <input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Название или последнее сообщение" />
      </label>
      <button className="btn inbox-filter-toggle" type="button" aria-expanded={filtersExpanded} aria-controls="inbox-filter-options"
        onClick={() => setFiltersExpanded(value => !value)}>Фильтры чатов{activeFilters ? ` · ${activeFilters}` : ""}</button>
      {filtersExpanded && <div id="inbox-filter-options" className="row inbox-filter-options" role="group" aria-label="Источник беседы">
        <button className={unreadOnly ? "btn primary" : "btn"} type="button" aria-pressed={unreadOnly} onClick={() => setUnreadOnly(value => !value)}>Непрочитанные</button>
        <button className={draftsOnly?"btn primary":"btn"} aria-pressed={draftsOnly} onClick={()=>setDraftsOnly(value=>!value)}>Черновики личных чатов</button><button className={unreadFirst?"btn primary":"btn"} aria-pressed={unreadFirst} onClick={()=>setUnreadFirst(value=>!value)}>Непрочитанные сверху</button>
        {inboxKinds.map(option => <button key={option.value} className={kind === option.value ? "btn primary" : "btn"} type="button"
          aria-pressed={kind === option.value} onClick={() => setKind(option.value)}>{option.label}</button>)}
      </div>}
      <div className="row"><span className="muted">Показано {visible.length} из {rows.length}</span>
        {filtered && <button className="btn" type="button" onClick={resetFilters}>Сбросить</button>}
      </div>
    </div>}
    {emptyChatState(loading, error, rows.length) === "loading" && <p className="muted">Загружаем беседы…</p>}
    {emptyChatState(loading, error, rows.length) === "empty" && <div className="card empty">Пока нет бесед. Вступите в учебную группу или добавьте человека по коду.</div>}
    {rows.length > 0 && visible.length === 0 && <div className="card empty">По запросу бесед нет.
      <button className="btn" type="button" onClick={resetFilters}>Показать все</button>
    </div>}
    <div className="people">
      {visible.map(item => <Link className="person" key={`${item.kind}:${item.conversationId}`} to={destination(item)}>
        <Avatar kind={item.kind === "group" ? "group" : "user"} id={item.kind === "group" ? item.communityId : item.peerUserId} name={item.kind === "classmate" ? item.title.split(" · ")[0] : item.title} />
        <span className="inbox-main"><span className="inbox-heading"><b>{item.title}</b>{item.lastAt && <time className="muted" dateTime={item.lastAt} title={new Date(item.lastAt).toLocaleString("ru-RU")}>{chatInboxTime(item.lastAt)}</time>}</span>
          <span className="inbox-preview"><span className="muted preview-line">{item.preview || (item.kind === "group" ? "Чат группы" : "Нет сообщений")}</span>
            {item.unread > 0 && <span className="chip" aria-label={`Непрочитанных сообщений: ${item.unread}`}>{item.unread > 99 ? "99+" : item.unread}</span>}</span>
          <span className="sr-only">{inboxSource(item.kind)}</span></span>
      </Link>)}
    </div>
  </section>;
}

export function PersonalChatPage() {
  const { conversationId } = useParams();
  const [personTitle, setPersonTitle] = useState<string | null>(null);
  return <section className={"page personal-page" + (conversationId ? " personal-deep-link" : "")}>
    <PageHead title={personTitle || (conversationId ? "Переписка" : "Личные чаты")} />
    <Link className="btn personal-list-back" to="/chat?all=1">Ко всем беседам</Link>
    <PeoplePanel initialConversationId={conversationId} onTitleChange={setPersonTitle} />
  </section>;
}
