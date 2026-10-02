import { Link, NavLink, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { useEffect, useLayoutEffect, useState } from "react";
import { publicationMemory, publicationProfile } from "./homework-publication-batch";
import { Provider, useApp } from "./store";
import { ChatInboxPage, PersonalChatPage } from "./chat";
import { CommunityPage, FriendsPage, GroupPage, HomeworkPage, LegalPage, MapsPage, SchedulePage, SettingsPage, SummaryPage, TeachersPage, WeekPage } from "./pages";
import { useReminders } from "./settings-panels";
import { Icon, IconName } from "./icons";
import { Sheet } from "./sheet";
import { HomeworkDraftProvider } from "./homework-draft-context";
import { PersonalComposerProvider } from "./personal-composer-context";
import { noteSearch } from "./ux300";
import { SearchField, useRoutePosition } from "./ux300-controls";

const items: [string, string, IconName][] = [
  ["schedule", "Расписание", "calendar"],
  ["week", "Неделя", "week"],
  ["summary", "Сводка", "summary"],
  ["teachers", "Преподаватели", "teachers"],
  ["maps", "Карты", "map"],
  ["friends", "Пересечения", "friends"],
  ["homework", "Домашка", "homework"],
  ["chat", "Чат", "chat"],
  ["community", "Сообщество", "community"],
  ["group", "Группа", "users"],
  ["settings", "Настройки", "settings"]
];

function Shell() {
  const app = useApp();
  const publicationOwner=publicationProfile(app.session?.authenticated?app.session.user?.userId:null,app.session?.familyId);
  useLayoutEffect(()=>publicationMemory.profile(publicationOwner),[publicationOwner]);
  useReminders();
  const location = useLocation();
  const navigate = useNavigate();
  const [menu, setMenu] = useState(false);
  const [menuQuery, setMenuQuery] = useState("");
  useEffect(()=>{let current:HTMLMediaElement|null=null;const playing=(event:Event)=>{const node=event.target;if(!(node instanceof HTMLMediaElement))return;if(node.srcObject){current?.pause();current=null;return;}if(current&&current!==node)current.pause();current=node;};document.addEventListener("play",playing,true);return()=>{document.removeEventListener("play",playing,true);current?.pause();};},[]);
  useRoutePosition(`${app.session?.user?.userId || "guest"}:${app.groupId}:${location.pathname}${location.search}`);
  useEffect(() => { const frame = requestAnimationFrame(() => { const heading = document.querySelector<HTMLElement>(".stage h1"); if (heading && !document.querySelector('[role="dialog"]')) { heading.tabIndex = -1; heading.focus({ preventScroll: true }); } }); return () => cancelAnimationFrame(frame); }, [location.pathname]);
  useEffect(()=>{if(!menu)return;const close=(event:KeyboardEvent)=>{if(event.key==="Escape"){event.preventDefault();setMenu(false);}};window.addEventListener("keydown",close);return()=>window.removeEventListener("keydown",close);},[menu]);
  const group = app.catalog?.groups.find(item => item.id === app.groupId);
  const chatActive = location.pathname === "/chat" || location.pathname.startsWith("/chat/") || location.pathname === "/group";
  const bar = new Set(["/schedule", "/maps", "/homework", "/"]);
  return (
    <div className="app">
      <aside className="sidebar">
        <NavLink to="/schedule" className="brand">Расписание военмех</NavLink>
        <p className="caption">Расписание и карты Военмеха</p>
        <button className="group-card" onClick={() => navigate("/settings?section=study")} type="button">
          <span className="with-ico"><Icon name="users" size={16} /> Моя группа</span>
          <strong>{group?.name || "Не выбрана"}</strong>
          <span>{app.session?.authenticated ? "Аккаунт" : "На этом устройстве"}</span>
        </button>
        <nav className="nav">
          {items.map(([path, title, icon], index) => <div key={path}>{[0, 7, 10].includes(index) && <h2 className="nav-section">{index === 0 ? "Учёба" : index === 7 ? "Группа" : "Приложение"}</h2>}<NavLink key={path} to={"/" + path} className={({ isActive }) => "nav-btn" + (isActive ? " active" : "")}><Icon name={icon} size={18} />{title}</NavLink></div>)}
        </nav>
        <div className="side-foot">
          <div>{app.session?.authenticated ? app.session.user?.displayName || app.session.user?.username : "Гостевой режим"}</div>
          Неофициальное приложение БГТУ «Военмех»
        </div>
      </aside>
      <div className="main">
        <header className="topbar">
          <NavLink to="/schedule" className="brand">Расписание военмех</NavLink>
          <NavLink to="/settings?section=study" className="chip top-group">{group?.name || "Выбрать группу"}</NavLink>
        </header>
        {app.notice && <div className="page" style={{ paddingBottom: 0 }}><div className="banner" role="status">{app.notice}</div></div>}
        <div className="stage" key={location.pathname}>
        <Routes>
          <Route path="/" element={<SchedulePage />} />
          <Route path="/schedule" element={<SchedulePage />} />
          <Route path="/week" element={<WeekPage />} />
          <Route path="/summary" element={<SummaryPage />} />
          <Route path="/teachers" element={<TeachersPage />} />
          <Route path="/maps" element={<MapsPage />} />
          <Route path="/friends" element={<FriendsPage />} />
          <Route path="/chat" element={<ChatInboxPage />} />
          <Route path="/chat/people" element={<PersonalChatPage />} />
          <Route path="/chat/person/:conversationId" element={<PersonalChatPage />} />
          <Route path="/homework" element={<HomeworkPage />} />
          <Route path="/community" element={<CommunityPage />} />
          <Route path="/group" element={<GroupPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/legal/agreement" element={<LegalPage id="agreement" />} />
          <Route path="/legal/policy" element={<LegalPage id="policy" />} />
          <Route path="*" element={<section className="page"><h1>Страница не найдена</h1><div className="card stack"><p>Ссылка могла устареть. Сохранённое расписание и ваши данные остаются доступны.</p><Link className="btn primary" to="/schedule">Открыть расписание</Link><Link className="btn" to="/settings">Настройки</Link></div></section>} />
        </Routes>
        </div>
        <nav className="bottom">
          <NavLink to="/schedule" className={({ isActive }) => isActive || location.pathname === "/" ? "active" : ""}><Icon name="calendar" />Расписание</NavLink>
          <NavLink to="/maps" className={({ isActive }) => isActive ? "active" : ""}><Icon name="map" />Карты</NavLink>
          <NavLink to="/homework" className={({ isActive }) => isActive ? "active" : ""}><Icon name="homework" />Домашка</NavLink>
          <NavLink to="/chat" className={chatActive ? "active" : ""}><Icon name="chat" />Чат</NavLink>
          <button type="button" className={bar.has(location.pathname) || chatActive ? "" : "active"} onClick={() => setMenu(true)}><Icon name="menu" />Разделы</button>
        </nav>
      </div>
      {menu && (
        <Sheet title="Разделы" onClose={() => setMenu(false)}>
            <SearchField label="Найти раздел" value={menuQuery} onChange={setMenuQuery} />
            {menuQuery.trim() ? <div className="tiles">{items.filter(([path, title]) => noteSearch(menuQuery, title, ({ friends: "люди встречи сравнение расписаний", community: "участники заявки членство", group: "каналы роли голосования анкеты", chat: "переписка личные сообщения", settings: "аккаунт пароль уведомления тема" } as Record<string,string>)[path] || "")).map(([path, title, icon]) => <NavLink key={path} to={`/${path}`} className="tile" onClick={() => { setMenu(false); setMenuQuery(""); }}><Icon name={icon}/>{title}</NavLink>)}<button className="btn quiet" onClick={() => setMenuQuery("")}>Все разделы</button></div> :
            <div className="tiles">
              {([ ["Учёба", ["week", "summary", "teachers", "friends"]], ["Группа", ["group", "community"]], ["Приложение", ["settings"]] ] as [string, string[]][]).map(([label, paths]) => <section className="section-group" key={label}><h2>{label}</h2>{items.filter(([path]) => paths.includes(path)).map(([path, title, icon]) => (
                <NavLink key={path} to={"/" + path} className="tile" onClick={() => setMenu(false)}><Icon name={icon} />{title}</NavLink>
              ))}</section>)}
            </div>}
        </Sheet>
      )}
    </div>
  );
}

export function App() {
  return <Provider><HomeworkDraftProvider><PersonalComposerProvider><Shell /></PersonalComposerProvider></HomeworkDraftProvider></Provider>;
}
