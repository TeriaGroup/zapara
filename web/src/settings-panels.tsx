import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import * as api from "./api";
import { useApp } from "./store";
import { subgroupIndex, visibleLessons } from "./subgroups";
import { lessonsOn, addDays, isoDay } from "./parity";
import { clockMinutes } from "./planner";
export type ReminderPreferences = {
    enabled: boolean;
    morning: boolean;
    evening: boolean;
    morningAt: string;
    eveningAt: string;
};
const key = "zapara.reminders";
export function readReminders(): ReminderPreferences { try {
    const value = JSON.parse(localStorage.getItem(key) || "{}");
    return { enabled: value.enabled === true, morning: value.morning !== false, evening: value.evening !== false, morningAt: clockMinutes(value.morningAt ?? "") !== null ? value.morningAt : "07:00", eveningAt: clockMinutes(value.eveningAt ?? "") !== null ? value.eveningAt : "20:00" };
}
catch {
    return { enabled: false, morning: true, evening: true, morningAt: "07:00", eveningAt: "20:00" };
} }
export function useReminders() {
    const app = useApp();
    useEffect(() => { const run = () => { const prefs = readReminders(); if (!prefs.enabled || typeof Notification === "undefined" || Notification.permission !== "granted")
        return; const now = new Date(); const minute = now.getHours() * 60 + now.getMinutes(); const tomorrow = prefs.evening && clockMinutes(prefs.eveningAt) === minute; const morning = prefs.morning && clockMinutes(prefs.morningAt) === minute; if (!tomorrow && !morning)
        return; const fired = `${isoDay(now)}:${tomorrow ? "evening" : "morning"}`; if (localStorage.getItem("zapara.reminder-fired") === fired)
        return; const period = api.readCache().lessons[app.groupId]?.period; if (!period || !app.timetableAvailable)
        return; const date = tomorrow ? addDays(now, 1) : now; const lessons = lessonsOn(visibleLessons(app.lessons, app.subgroups[app.groupId] || {}), date, period.start, period.weekCount, app.invert); localStorage.setItem("zapara.reminder-fired", fired); new Notification("Расписание военмех", { body: `${tomorrow ? "Завтра" : "Сегодня"}: ${lessons.length} пар${lessons.length ? `, начало в ${lessons[0].timeStart}` : ""}.`, tag: "zapara-study-reminder", icon: "/app/icons/icon-512.png" }); }; run(); const timer = window.setInterval(run, 15000); return () => window.clearInterval(timer); }, [app.groupId, app.lessons, app.subgroups, app.invert, app.timetableAvailable]);
}
export function NotificationSettings() {
    const app = useApp();
    const [prefs, setPrefs] = useState(readReminders);
    const [times, setTimes] = useState({ morningAt: prefs.morningAt, eveningAt: prefs.eveningAt });
    const [notice, setNotice] = useState("");
    function save(next: ReminderPreferences) { localStorage.setItem(key, JSON.stringify(next)); setPrefs(next); const synced = app.session?.authenticated ? app.privateHomework.saveSettings({ notifyTime1: next.enabled && next.evening ? next.eveningAt : null, notifyTime2: next.enabled && next.morning ? next.morningAt : null }) : false; setNotice(app.session?.authenticated && !synced ? "Сохранено на устройстве. Синхронизация пока недоступна." : "Сохранено на устройстве"); }
    useEffect(() => { const settings = app.privateHomework.settings; if (!app.session?.authenticated || !settings)
        return; const next = { ...readReminders(), morning: !!settings.notifyTime2, evening: !!settings.notifyTime1, morningAt: settings.notifyTime2 || prefs.morningAt, eveningAt: settings.notifyTime1 || prefs.eveningAt }; localStorage.setItem(key, JSON.stringify(next)); setPrefs(next); setTimes({ morningAt: next.morningAt, eveningAt: next.eveningAt }); }, [app.privateHomework.settings, app.session?.authenticated]);
    async function enable(value: boolean) { if (value) {
        if (typeof Notification === "undefined") {
            setNotice("Этот браузер не поддерживает уведомления");
            return;
        }
        const permission = Notification.permission === "default" ? await Notification.requestPermission() : Notification.permission;
        if (permission !== "granted") {
            setNotice("Разрешите уведомления в настройках браузера");
            return;
        }
    } save({ ...prefs, enabled: value }); }
    function saveTime(field: "morningAt" | "eveningAt", value: string) { setTimes(rows => ({ ...rows, [field]: value })); if (clockMinutes(value) === null) {
        setNotice("Укажите корректное время. Последнее сохранённое значение не изменилось.");
        return;
    } save({ ...prefs, [field]: value }); }
    const period = api.readCache().lessons[app.groupId]?.period;
    const lessons = period ? lessonsOn(visibleLessons(app.lessons, app.subgroups[app.groupId] || {}), addDays(new Date(), 1), period.start, period.weekCount, app.invert) : null;
    return <article className="card stack notification-settings"><h2>Напоминания</h2><label className="switch-row"><span>Уведомления</span><input type="checkbox" role="switch" checked={prefs.enabled} onChange={event => void enable(event.target.checked)}/></label><p className="muted">В браузере напоминания приходят, пока приложение открыто. {prefs.enabled ? "Разрешение получено." : "Время недоступно, пока уведомления выключены."}</p>{([["morning", "morningAt", "Утром о сегодняшнем дне"], ["evening", "eveningAt", "Вечером о завтрашнем дне"]] as const).map(([flag, time, title]) => <div className="stack notification-time" key={flag}><label className="switch-row"><span>{title}</span><input type="checkbox" role="switch" checked={prefs[flag]} disabled={!prefs.enabled} onChange={event => save({ ...prefs, [flag]: event.target.checked })}/></label><label className="field">Время<input type="time" value={times[time]} disabled={!prefs.enabled || !prefs[flag]} onChange={event => saveTime(time, event.target.value)}/></label></div>)}<div className="notification-preview"><span className="muted">Предпросмотр уведомления</span><h2>Расписание военмех</h2><p>{lessons === null ? "Расписание ещё не загружено" : `Завтра: ${lessons.length} пар${lessons.length ? `, начало в ${lessons[0].timeStart}` : ""}.`}</p></div>{app.session?.authenticated && <BackgroundDelivery />}
    {notice && <p role="status">{notice}</p>}</article>;
}
export function StudyExtras() { const app = useApp(); const index = subgroupIndex(app.lessons); const [gaps, setGaps] = useState(localStorage.getItem("zapara.free-time") !== "0"); return <article className="card stack"><h2>Планирование</h2><label className="switch-row"><span>Свободное время между парами</span><input type="checkbox" role="switch" checked={gaps} onChange={event => { setGaps(event.target.checked); localStorage.setItem("zapara.free-time", event.target.checked ? "1" : "0"); }}/></label><p className="muted">Показываем перерывы от 30 минут. Выбор учебной группы не меняет членство в чате.</p><h2>Подгруппы по предметам</h2>{index.streams.map(stream => <div key={stream.id}><b>{stream.title}</b><div className="row">{stream.options.map(option => <button className={(app.subgroups[app.groupId]?.[stream.id] === option.id) ? "btn primary" : "btn"} type="button" key={option.id} onClick={() => app.pickSubgroup(stream.id, option.id)}>{option.label}</button>)}</div></div>)}{index.streams.length === 0 && <p className="muted">В сохранённом расписании разделения на подгруппы нет.</p>}</article>; }
export function AccountDetails() { const app = useApp(); const [methods, setMethods] = useState<string[]>([]); const [devices, setDevices] = useState<api.AccountDevice[]>([]); const [cursor, setCursor] = useState<string | null>(null); const [busy, setBusy] = useState(false); const [notice, setNotice] = useState(""); useEffect(() => { let stop = false; void Promise.all([api.accountMe(), api.accountDevices()]).then(([me, list]) => { if (!stop) {
    setMethods(me.authenticationMethods);
    setDevices(list.devices);
    setCursor(list.nextCursor);
} }).catch(() => { if (!stop)
    setNotice("Данные аккаунта не загрузились"); }); return () => { stop = true; }; }, [app.session?.user?.userId]); return <article className="card stack"><h2>Способ входа</h2><p>{methods.map(method => ({ vk: "VK ID", yandex: "Яндекс ID", password: "Логин и пароль" }[method] ?? method)).join(", ") || "Загружаем…"}</p><h2>Активные устройства</h2>{devices.map(device => <div className="row device-line" key={device.familyId}><div><b>{device.deviceName}</b><p className="muted">{device.platform} · {new Date(device.lastSeenAt).toLocaleString("ru-RU")}{device.isCurrent ? " · Это устройство" : ""}</p></div>{!device.isCurrent && <button className="btn quiet" type="button" disabled={busy} onClick={() => { if (!window.confirm(`Завершить сеанс «${device.deviceName}»?`))
    return; setBusy(true); void api.revokeDevice(device.familyId).then(() => setDevices(rows => rows.filter(row => row.familyId !== device.familyId))).catch(() => setNotice("Сеанс не завершён")).finally(() => setBusy(false)); }}>Завершить сеанс</button>}</div>)}{cursor && <button className="btn" disabled={busy} type="button" onClick={() => { setBusy(true); void api.accountDevices(cursor).then(next => { setDevices(rows => [...rows, ...next.devices]); setCursor(next.nextCursor); }).catch(() => setNotice("Список не загрузился")).finally(() => setBusy(false)); }}>Ещё устройства</button>}{notice && <p role="status">{notice}</p>}</article>; }
export function DataSettings() { const app = useApp(); const cache = api.readCache(); const [state, setState] = useState(""); const [busy, setBusy] = useState(false); return <article className="card stack"><h2>Копия на устройстве</h2><p>{Object.keys(cache.lessons).length} сохранённых расписаний · {app.homework.length} личных заданий</p><p className="muted">Подключение: {navigator.onLine ? "сеть доступна" : "нет сети"}. {cache.lessons[app.groupId] ? `Копия расписания: ${new Date(cache.lessons[app.groupId].meta.fetchedAt).toLocaleString("ru-RU")}` : "Расписание ещё не сохранено."}</p><button className="btn" type="button" disabled={app.loading || app.timetableLoading} onClick={app.refresh}>Обновить расписание</button><h2>Синхронизация аккаунта</h2><p role="status">{app.privateHomework.status}</p>{app.session?.authenticated ? <><button className="btn" disabled={busy} type="button" onClick={() => { setBusy(true); void api.syncMetadata().then(value => setState(`Сервер доступен · версия ${value.currentSequence}`)).catch(() => setState("Сервер синхронизации недоступен. Локальная копия сохранена.")).finally(() => setBusy(false)); }}>{busy ? "Проверяем…" : "Проверить подключение"}</button><p role="status">{state}</p><button className="btn" type="button" onClick={app.privateHomework.refresh}>Синхронизировать личную домашку</button><button className="btn quiet" type="button" onClick={() => { if (window.confirm("Скопировать гостевые задания в этот аккаунт? Гостевая копия останется на устройстве."))
    app.privateHomework.importGuest(); }}>Импортировать гостевые задания</button>{app.privateHomework.pending.filter(row => row.conflict !== undefined).map(row => <div className="card stack" key={row.opId}><h2>Конфликт задания</h2><p>{app.homework.find(item => item.id === row.id)?.text}</p><p className="muted">Ваше изменение сохранено локально. Выберите версию.</p><div className="row"><button className="btn" type="button" onClick={() => app.privateHomework.resolve(row.opId, "server")}>Серверная версия</button><button className="btn" type="button" onClick={() => app.privateHomework.resolve(row.opId, "local")}>Сохранить мою</button></div></div>)}</> : <><p className="muted">Вы пользуетесь гостевой копией. Вход не переносит её в аккаунт автоматически.</p><Link className="btn" to="/settings?section=account">Войти</Link></>}<p className="muted">Расписание сохраняется после успешного обновления. Ошибка сети не удаляет последнюю копию.</p></article>; }
export function UpdateSettings() { const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false); return <article className="card stack"><h2>Обновления</h2><p className="muted">Браузер получает новую версию приложения с сервера.</p><button className="btn" type="button" disabled={busy} onClick={() => { setBusy(true); void fetch("/app/index.html", { cache: "no-store", credentials: "same-origin" }).then(response => { if (!response.ok)
    throw new Error(); setMessage("Сервер доступен. Перезагрузите приложение, чтобы загрузить актуальную версию."); }).catch(() => setMessage("Проверка не удалась. Сохранённые данные остаются на устройстве.")).finally(() => setBusy(false)); }}>Проверить обновление</button>{message && <p role="status">{message}</p>}{message.startsWith("Сервер доступен") && <button className="btn primary" type="button" onClick={() => window.location.reload()}>Перезагрузить приложение</button>}</article>; }
function BackgroundDelivery() {
    const app = useApp();
    const [id, setId] = useState<string | null>(null);
    const [available, setAvailable] = useState(false);
    const [busy, setBusy] = useState(false);
    const [notice, setNotice] = useState("");
    useEffect(() => { let stop = false; void Promise.all([api.pushCapabilities(), api.pushSubscriptions()]).then(([capabilities, subscriptions]) => { if (!stop) {
        setAvailable(capabilities.available);
        setId(subscriptions.find(row => row.enabled)?.subscriptionId ?? null);
    } }).catch(() => { if (!stop)
        setNotice("Фоновая доставка сейчас недоступна."); }); return () => { stop = true; }; }, [app.session?.user?.userId]);
    async function change(enabled: boolean) {
        if (busy)
            return;
        setBusy(true);
        setNotice("");
        try {
            if (!enabled) {
                if (id)
                    await api.deletePushSubscription(id);
                const reg = await navigator.serviceWorker.getRegistration("/app/");
                const subscription = await reg?.pushManager?.getSubscription();
                if (subscription)
                    await subscription.unsubscribe();
                setId(null);
                return;
            }
            if (!isSecureContext || !("serviceWorker" in navigator) || !("PushManager" in window) || typeof Notification === "undefined")
                throw new Error("Этот браузер не поддерживает фоновую доставку.");
            const capabilities = await api.pushCapabilities();
            if (!capabilities.available || !capabilities.publicKey)
                throw new Error("Фоновая доставка недоступна на сервере.");
            if (Notification.permission !== "granted" && await Notification.requestPermission() !== "granted")
                throw new Error("Разрешите уведомления в настройках браузера.");
            const reg = await navigator.serviceWorker.register("/app/react-worker.js", { scope: "/app/" });
            await navigator.serviceWorker.ready;
            const padded = capabilities.publicKey.replaceAll("-", "+").replaceAll("_", "/") + "=".repeat((4 - capabilities.publicKey.length % 4) % 4);
            const keyBytes = Uint8Array.from(atob(padded), ch => ch.charCodeAt(0));
            let subscription = await reg.pushManager.getSubscription();
            const created = !subscription;
            subscription ??= await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes });
            const value = subscription.toJSON();
            if (!value.endpoint || !value.keys?.p256dh || !value.keys.auth)
                throw new Error("Браузер вернул неполную подписку.");
            try {
                const saved = await api.savePushSubscription({ endpoint: value.endpoint, keys: { p256dh: value.keys.p256dh, auth: value.keys.auth }, enabled: true, timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || "Europe/Moscow" });
                setId(saved.subscriptionId);
                setNotice("Фоновая доставка включена. Время берётся из синхронизированных настроек аккаунта.");
            }
            catch (error) {
                if (created)
                    await subscription.unsubscribe().catch(() => undefined);
                throw error;
            }
        }
        catch (error) {
            setNotice(error instanceof Error ? error.message : "Доставка не сохранена");
        }
        finally {
            setBusy(false);
        }
    }
    return <section className="stack"><h2>Фоновая доставка</h2><label className="switch-row"><span>Когда приложение закрыто</span><input type="checkbox" role="switch" checked={!!id} disabled={busy || (!available && !id)} onChange={event => void change(event.target.checked)}/></label><p className="muted">Нужны аккаунт, поддержка браузера и доступный сервер уведомлений. На экране блокировки показывается общий текст без учебных данных.</p>{id && <button className="btn" type="button" disabled={busy} onClick={() => { setBusy(true); void api.testPushSubscription(id).then(value => setNotice(value.status === "accepted" ? "Проверочное уведомление принято сервером" : "Доставка недоступна")).catch(() => setNotice("Проверка доставки не удалась")).finally(() => setBusy(false)); }}>Проверить доставку</button>}{notice && <p role="status">{notice}</p>}</section>;
}
