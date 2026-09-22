import { NavLink, Route, Routes, useLocation } from "react-router-dom";
import { useState } from "react";
import { Provider, useApp } from "./store";
import { CommunityPage, FriendsPage, GroupPage, HomeworkPage, MapsPage, SchedulePage, SettingsPage, SummaryPage, TeachersPage, WeekPage } from "./pages";

const items = [
  ["schedule", "Расписание"],
  ["week", "Неделя"],
  ["summary", "Сводка"],
  ["teachers", "Преподаватели"],
  ["maps", "Карты"],
  ["friends", "Друзья"],
  ["homework", "Домашка"],
  ["community", "Сообщество"],
  ["group", "Группа"],
  ["settings", "Настройки"]
] as const;

function Shell() {
  const app = useApp();
  const location = useLocation();
  const [menu, setMenu] = useState(false);
  const group = app.catalog?.groups.find(item => item.id === app.groupId);
  const bar = new Set(["/schedule", "/maps", "/homework", "/"]);
  return (
    <div className="app">
      <aside className="sidebar">
        <NavLink to="/schedule" className="brand">ЗАПАРА</NavLink>
        <p className="caption">Расписание и карты Военмеха</p>
        <button className="group-card" onClick={() => location.pathname !== "/settings" && (window.location.hash = "")} type="button">
          <span>Моя группа</span>
          <strong>{group?.name || "Не выбрана"}</strong>
          <span>{app.session?.authenticated ? "Аккаунт" : "На этом устройстве"}</span>
        </button>
        <nav className="nav">
          {items.map(([path, title]) => <NavLink key={path} to={"/" + path} className={({ isActive }) => "nav-btn" + (isActive ? " active" : "")}>{title}</NavLink>)}
        </nav>
        <div className="side-foot">
          <div>{app.session?.authenticated ? app.session.user?.displayName || app.session.user?.username : "Гостевой режим"}</div>
          Неофициальное приложение БГТУ «Военмех»
        </div>
      </aside>
      <div className="main">
        <header className="topbar">
          <NavLink to="/schedule" className="brand">ЗАПАРА</NavLink>
          <NavLink to="/settings" className="chip">{group?.name || "Группа"}</NavLink>
        </header>
        {app.notice && <div className="page" style={{ paddingBottom: 0 }}><div className="banner" role="status">{app.notice}</div></div>}
        <Routes>
          <Route path="/" element={<SchedulePage />} />
          <Route path="/schedule" element={<SchedulePage />} />
          <Route path="/week" element={<WeekPage />} />
          <Route path="/summary" element={<SummaryPage />} />
          <Route path="/teachers" element={<TeachersPage />} />
          <Route path="/maps" element={<MapsPage />} />
          <Route path="/friends" element={<FriendsPage />} />
          <Route path="/homework" element={<HomeworkPage />} />
          <Route path="/community" element={<CommunityPage />} />
          <Route path="/group" element={<GroupPage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Routes>
        <nav className="bottom">
          <NavLink to="/schedule" className={({ isActive }) => isActive || location.pathname === "/" ? "active" : ""}>Расписание</NavLink>
          <NavLink to="/maps" className={({ isActive }) => isActive ? "active" : ""}>Карты</NavLink>
          <NavLink to="/homework" className={({ isActive }) => isActive ? "active" : ""}>Домашка</NavLink>
          <button type="button" className={bar.has(location.pathname) ? "" : "active"} onClick={() => setMenu(true)}>Разделы</button>
        </nav>
      </div>
      {menu && (
        <div className="sheet" onClick={() => setMenu(false)}>
          <div className="card" onClick={event => event.stopPropagation()}>
            <h2>Разделы</h2>
            <div className="tiles">
              {items.filter(([path]) => !["schedule", "maps", "homework"].includes(path)).map(([path, title]) => (
                <NavLink key={path} to={"/" + path} className="tile" onClick={() => setMenu(false)}>{title}</NavLink>
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
