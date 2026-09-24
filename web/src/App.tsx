import { NavLink, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { useState } from "react";
import { Provider, useApp } from "./store";
import { ChatInboxPage, PersonalChatPage } from "./chat";
import { CommunityPage, FriendsPage, GroupPage, HomeworkPage, LegalPage, MapsPage, SchedulePage, SettingsPage, SummaryPage, TeachersPage, WeekPage } from "./pages";
import { Icon, IconName } from "./icons";

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
  const location = useLocation();
  const navigate = useNavigate();
  const [menu, setMenu] = useState(false);
  const group = app.catalog?.groups.find(item => item.id === app.groupId);
  const chatActive = location.pathname === "/chat" || location.pathname.startsWith("/chat/") || location.pathname === "/group";
  const bar = new Set(["/schedule", "/maps", "/homework", "/"]);
  return (
    <div className="app">
      <aside className="sidebar">
        <NavLink to="/schedule" className="brand">Расписание военмех</NavLink>
        <p className="caption">Расписание и карты Военмеха</p>
        <button className="group-card" onClick={() => navigate("/settings")} type="button">
          <span className="with-ico"><Icon name="users" size={16} /> Моя группа</span>
          <strong>{group?.name || "Не выбрана"}</strong>
          <span>{app.session?.authenticated ? "Аккаунт" : "На этом устройстве"}</span>
        </button>
        <nav className="nav">
          {items.map(([path, title, icon]) => <NavLink key={path} to={"/" + path} className={({ isActive }) => "nav-btn" + (isActive ? " active" : "")}><Icon name={icon} size={18} />{title}</NavLink>)}
        </nav>
        <div className="side-foot">
          <div>{app.session?.authenticated ? app.session.user?.displayName || app.session.user?.username : "Гостевой режим"}</div>
          Неофициальное приложение БГТУ «Военмех»
        </div>
      </aside>
      <div className="main">
        <header className="topbar">
          <NavLink to="/schedule" className="brand">Расписание военмех</NavLink>
          <NavLink to="/settings" className="chip top-group">{group?.name || "Выбрать группу"}</NavLink>
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
        <div className="sheet" onClick={() => setMenu(false)}>
          <div className="card" onClick={event => event.stopPropagation()}>
            <h2>Разделы</h2>
            <div className="tiles">
              {items.filter(([path]) => !["schedule", "maps", "homework", "chat"].includes(path)).map(([path, title, icon]) => (
                <NavLink key={path} to={"/" + path} className="tile" onClick={() => setMenu(false)}><Icon name={icon} />{title}</NavLink>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export function App() {
  return <Provider><Shell /></Provider>;
}
