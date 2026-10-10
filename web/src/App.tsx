import { Link, NavLink, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
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
import { SearchField, useRoutePosition, focusRouteHeading, useClock } from "./ux300-controls";
import { MobileChromeContext, useCompactLayout, useMobileKeyboard } from "./mobile-chrome";
import { bottomNavReserve, showGroupChip, mobileTabForPath, studyGroupCaption } from "./mobile-navigation";
import { PageHead } from "./page-head";
import { S } from "./strings.gen";

const items: [string, string, IconName][] = [
  ["schedule", "Расписание", "calendar"],
  ["week", "Неделя", "week"],
  ["summary", "Сводка", "summary"],
  ["teachers", "Преподаватели", "teachers"],
  ["maps", "Карты", "map"],
  ["friends", "Пересечения", "friends"],
  ["homework", "Домашка", "homework"],
  ["chat", S.navChats, "chat"],
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
  const compact = useCompactLayout();
  const keyboardOpen = useMobileKeyboard(compact);
  const [screenTitle, setScreenTitle] = useState<string | null>(null);
  const [actionsHost, setActionsHost] = useState<HTMLElement | null>(null);
  const header = useRef<HTMLElement>(null);
  const footer = useRef<HTMLElement>(null);
  const now = useClock();
  const chrome = useMemo(() => ({ compact, actionsHost, setTitle: setScreenTitle }), [compact, actionsHost]);
  useLayoutEffect(() => {
    const node = header.current;
    if (!node) return;
    const update = () => document.documentElement.style.setProperty("--mobile-header-height", `${Math.max(56, Math.ceil(node.getBoundingClientRect().height))}px`);
    update();
    const observer = new ResizeObserver(update);
    observer.observe(node);
    return () => { observer.disconnect(); document.documentElement.style.removeProperty("--mobile-header-height"); };
  }, []);
  useLayoutEffect(() => {
    const node = footer.current;
    if (!node) return;
    const update = () => document.documentElement.style.setProperty("--mobile-footer-measured", `${bottomNavReserve(node.getBoundingClientRect().height)}px`);
    update();
    const observer = new ResizeObserver(update);
    observer.observe(node);
    return () => { observer.disconnect(); document.documentElement.style.removeProperty("--mobile-footer-measured"); };
  }, []);
  useEffect(()=>{let current:HTMLMediaElement|null=null;const playing=(event:Event)=>{const node=event.target;if(!(node instanceof HTMLMediaElement))return;if(node.srcObject){current?.pause();current=null;return;}if(current&&current!==node)current.pause();current=node;};document.addEventListener("play",playing,true);return()=>{document.removeEventListener("play",playing,true);current?.pause();};},[]);
  useRoutePosition(`${app.session?.user?.userId || "guest"}:${app.groupId}:${location.pathname}${location.search}`);
  useEffect(() => { const frame = requestAnimationFrame(focusRouteHeading); return () => cancelAnimationFrame(frame); }, [location.pathname]);
  useEffect(()=>{if(!menu)return;const close=(event:KeyboardEvent)=>{if(event.key==="Escape"){event.preventDefault();setMenu(false);}};window.addEventListener("keydown",close);return()=>window.removeEventListener("keydown",close);},[menu]);
  const group = app.catalog?.groups.find(item => item.id === app.groupId);
  const selectedTab = mobileTabForPath(location.pathname);
  const fallbackTitle = items.find(([path]) => location.pathname === `/${path}`)?.[1] || (selectedTab === "chat" ? S.navChats : S.scheduleTitle);
  const groupLabel = studyGroupCaption(group?.name, app.catalog?.period, now, app.invert);
  return (
    <MobileChromeContext.Provider value={chrome}>
    <div className="app" data-mobile-keyboard={keyboardOpen ? "open" : "closed"}>
      <aside className="sidebar">
        <NavLink to="/schedule" className="brand">{S.productName}</NavLink>
        <p className="caption">Расписание и карты Военмеха</p>
        <button className="group-card" onClick={() => navigate("/settings?section=study")} type="button">
          <span className="with-ico"><Icon name="users" size={16} /> Моя группа</span>
          <strong>{group?.name || "Не выбрана"}</strong>
          <span>{app.session?.authenticated ? "Аккаунт" : "На этом устройстве"}</span>
        </button>
        {/* #27 (W-06): разделы прокручиваются, «Настройки» закреплены под списком и всегда видны; дисклеймер — в Настройках. */}
        <nav className="nav" aria-label="Разделы">
          {items.slice(0, 10).map(([path, title, icon], index) => <div key={path}>{[0, 7].includes(index) && <h2 className="nav-section">{index === 0 ? "Учёба" : "Группа"}</h2>}<NavLink key={path} to={"/" + path} aria-current={path === "schedule" && location.pathname === "/" ? "page" : undefined} className={({ isActive }) => "nav-btn" + (isActive || path === "schedule" && location.pathname === "/" ? " active" : "")}><Icon name={icon} size={18} />{title}</NavLink></div>)}
        </nav>
        <div className="side-foot">
          {items.slice(10).map(([path, title, icon]) => <NavLink key={path} to={"/" + path} className={({ isActive }) => "nav-btn" + (isActive ? " active" : "")}><Icon name={icon} size={18} />{title}</NavLink>)}
          <div className="side-user">{app.session?.authenticated ? app.session.user?.displayName || app.session.user?.username : "Гостевой режим"}</div>
        </div>
      </aside>
      <div className="main">
        <header className="topbar" ref={header}>
          <div className="mobile-screen-title">{screenTitle || fallbackTitle}</div>
          <div className="mobile-header-actions" ref={setActionsHost} />
          {showGroupChip(location.pathname, !!group) && <NavLink to="/settings?section=study" className="top-group" aria-label={`Учебная группа: ${groupLabel}. Изменить группу`}>
            {app.catalog?.meta.stale && <span className="group-stale-dot" aria-hidden="true" />}{groupLabel}
          </NavLink>}
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
          <Route path="*" element={<section className="page"><PageHead title="Страница не найдена" /><div className="card stack"><p>Ссылка могла устареть. Сохранённое расписание и ваши данные остаются доступны.</p><Link className="btn primary" to="/schedule">Открыть расписание</Link><Link className="btn" to="/settings">Настройки</Link></div></section>} />
        </Routes>
        </div>
        <nav className="bottom" ref={footer} aria-label="Основные разделы">
          <NavLink to="/schedule" className={selectedTab === "schedule" ? "active" : ""}><Icon name="calendar" /><span className="bottom-label">Расписание</span></NavLink>
          <NavLink to="/maps" className={selectedTab === "maps" ? "active" : ""}><Icon name="map" /><span className="bottom-label">Карты</span></NavLink>
          <NavLink to="/homework" className={selectedTab === "homework" ? "active" : ""}><Icon name="homework" /><span className="bottom-label">Домашка</span></NavLink>
          <NavLink to="/chat" className={selectedTab === "chat" ? "active" : ""} aria-current={selectedTab === "chat" ? "page" : undefined}><Icon name="chat" /><span className="bottom-label">{S.navChats}</span></NavLink>
          <button type="button" className={menu ? "active" : ""} aria-expanded={menu} aria-controls={menu ? "sections-menu" : undefined} onClick={() => { setMenuQuery(""); setMenu(true); }}><Icon name="menu" /><span className="bottom-label">Разделы</span></button>
        </nav>
      </div>
      {menu && (
        <Sheet title="Разделы" id="sections-menu" onClose={() => setMenu(false)}>
            <SearchField label="Найти раздел" value={menuQuery} onChange={setMenuQuery} />
            {menuQuery.trim() ? <div className="section-menu-results">{items.filter(([path, title]) => noteSearch(menuQuery, title, ({ friends: "люди встречи сравнение расписаний", community: "участники заявки членство", group: "каналы роли голосования анкеты", chat: "переписка личные сообщения", settings: "аккаунт пароль уведомления тема" } as Record<string,string>)[path] || "")).map(([path, title, icon]) => <NavLink key={path} to={`/${path}`} className="section-menu-row" onClick={() => { setMenu(false); setMenuQuery(""); }}><Icon name={icon}/><span>{title}</span><Icon name="right" size={18} /></NavLink>)}<button className="btn quiet" type="button" onClick={() => setMenuQuery("")}>Все разделы</button></div> :
            <div className="section-menu-list">
              {([ ["Учёба", ["week", "summary", "teachers", "friends"]], ["Группа", ["group", "community"]], ["Приложение", ["settings"]] ] as [string, string[]][]).map(([label, paths]) => <section className="section-group" key={label}><h2>{label}</h2>{items.filter(([path]) => paths.includes(path)).map(([path, title, icon]) => (
                <NavLink key={path} to={"/" + path} className="section-menu-row" onClick={() => setMenu(false)}><Icon name={icon} /><span>{title}</span><Icon name="right" size={18} /></NavLink>
              ))}</section>)}
            </div>}
        </Sheet>
      )}
    </div>
    </MobileChromeContext.Provider>
  );
}

export function App() {
  return <Provider><HomeworkDraftProvider><PersonalComposerProvider><Shell /></PersonalComposerProvider></HomeworkDraftProvider></Provider>;
}
