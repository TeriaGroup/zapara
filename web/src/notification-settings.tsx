import { useEffect, useRef, useState } from "react";
import * as api from "./api";
import { useApp } from "./store";
import { projectAccountReminders, reminderTimePatch, type ReminderPreferences } from "./reminder-settings";
import { readReminders } from "./settings-panels";
import { visibleLessons } from "./subgroups";
import { addDays, lessonsOn } from "./parity";
import { BackgroundDelivery } from "./settings-panels";

export function NotificationSettings() {
  const app = useApp();
  return <NotificationContent key={JSON.stringify([app.session?.authenticated,app.session?.user?.userId,app.session?.familyId])} />;
}
function NotificationContent() {
  const app = useApp();
  const [prefs, setPrefs] = useState(() => app.session?.authenticated ? app.privateHomework.settings ? projectAccountReminders(app.privateHomework.settings,readReminders()) : {...readReminders(),enabled:false} : readReminders());
  const [times, setTimes] = useState({morningAt:prefs.morningAt,eveningAt:prefs.eveningAt});
  const [notice, setNotice] = useState("");
  const dirty = useRef(false);
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => {
    if (!app.session?.authenticated || !app.privateHomework.settings) return;
    const next = projectAccountReminders(app.privateHomework.settings,readReminders());
    setPrefs(next);
    if (!dirty.current) setTimes({morningAt:next.morningAt,eveningAt:next.eveningAt});
  }, [app.privateHomework.settings,app.session?.authenticated]);
  function save(next: ReminderPreferences) {
    const patch = reminderTimePatch(next,next);
    if (!patch || !alive.current) return false;
    if (app.session?.authenticated) {
      if (!app.privateHomework.saveSettings(patch)) { setNotice("Изменение не сохранено. Черновик остался — повторите сохранение."); return false; }
      setNotice("Сохранено локально, ожидает синхронизации аккаунта.");
    } else { localStorage.setItem("zapara.reminders",JSON.stringify(next)); setNotice("Сохранено на устройстве."); }
    setPrefs(next); return true;
  }
  async function enable(value: boolean) {
    if (value) {
      if (typeof Notification === "undefined") { setNotice("Этот браузер не поддерживает уведомления."); return; }
      const permission = Notification.permission === "default" ? await Notification.requestPermission() : Notification.permission;
      if (!alive.current) return;
      if (permission !== "granted") { setNotice("Разрешите уведомления в настройках браузера."); return; }
    }
    save({...prefs,enabled:value});
  }
  const valid = reminderTimePatch(prefs,times) !== null;
  const changed = times.morningAt !== prefs.morningAt || times.eveningAt !== prefs.eveningAt;
  const period = api.readCache().lessons[app.groupId]?.period;
  const lessons = period && app.timetableAvailable ? lessonsOn(visibleLessons(app.lessons,app.subgroups[app.groupId] || {}),addDays(new Date(),1),period.start,period.weekCount,app.invert) : null;
  return <article className="card stack notification-settings"><h2>Напоминания</h2>
    <label className="switch-row"><span>Уведомления</span><input type="checkbox" role="switch" checked={prefs.enabled} onChange={event => void enable(event.target.checked)}/></label>
    <p className="muted">Напоминания в браузере приходят, пока приложение открыто. Изменение времени применяется только после сохранения.</p>
    {([["morning","morningAt","Утром о сегодняшнем дне"],["evening","eveningAt","Вечером о завтрашнем дне"]] as const).map(([flag,time,title]) => <div className="stack notification-time" key={flag}><label className="switch-row"><span>{title}</span><input type="checkbox" role="switch" checked={prefs[flag]} disabled={!prefs.enabled} onChange={event => save({...prefs,[flag]:event.target.checked})}/></label><label className="field">Время<input type="time" value={times[time]} disabled={!prefs.enabled || !prefs[flag]} onChange={event => { dirty.current = true; setTimes(current => ({...current,[time]:event.target.value})); setNotice(""); }}/></label></div>)}
    {changed && <p className="muted" role="status">{valid ? "Время изменено, но ещё не сохранено." : "Введите время от 00:00 до 23:59. Действующие напоминания не изменились."}</p>}
    <div className="row"><button className="btn primary" type="button" disabled={!prefs.enabled || !changed || !valid} onClick={() => { if(save({...prefs,...times})) dirty.current = false; }}>Сохранить время</button>{changed && <button className="btn quiet" type="button" onClick={() => { dirty.current = false; setTimes({morningAt:prefs.morningAt,eveningAt:prefs.eveningAt}); setNotice(""); }}>Отменить правки</button>}</div>
    <div className="notification-preview"><span className="muted">Предпросмотр уведомления</span><h2>Расписание военмех</h2><p>{lessons === null ? "Расписание ещё не загружено" : `Завтра: ${lessons.length} пар${lessons.length ? `, начало в ${lessons[0].timeStart}` : ""}.`}</p></div>
    {app.session?.authenticated && <BackgroundDelivery />}{notice && <p role="status">{notice}</p>}
  </article>;
}
