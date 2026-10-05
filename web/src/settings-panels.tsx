import { useEffect, useState } from "react";
import * as api from "./api";
import { useApp } from "./store";
import { subgroupIndex, visibleLessons } from "./subgroups";
import { lessonsOn, addDays, isoDay } from "./parity";
import { clockMinutes } from "./planner";
import { projectAccountReminders, type ReminderPreferences } from "./reminder-settings";
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
    useEffect(() => { const run = () => { const guest = readReminders(); const prefs = app.session?.authenticated
        ? app.privateHomework.settings ? projectAccountReminders(app.privateHomework.settings, guest) : { ...guest, enabled: false }
        : guest; if (!prefs.enabled || typeof Notification === "undefined" || Notification.permission !== "granted")
        return; const now = new Date(); const minute = now.getHours() * 60 + now.getMinutes(); const tomorrow = prefs.evening && clockMinutes(prefs.eveningAt) === minute; const morning = prefs.morning && clockMinutes(prefs.morningAt) === minute; if (!tomorrow && !morning)
        return; const fired = `${app.session?.user?.userId || "guest"}:${isoDay(now)}:${tomorrow ? "evening" : "morning"}`; if (localStorage.getItem("zapara.reminder-fired") === fired)
        return; const period = api.readCache().lessons[app.groupId]?.period; if (!period || !app.timetableAvailable)
        return; const date = tomorrow ? addDays(now, 1) : now; const lessons = lessonsOn(visibleLessons(app.lessons, app.subgroups[app.groupId] || {}), date, period.start, period.weekCount, app.invert); localStorage.setItem("zapara.reminder-fired", fired); new Notification("Расписание военмех", { body: `${tomorrow ? "Завтра" : "Сегодня"}: ${lessons.length} пар${lessons.length ? `, начало в ${lessons[0].timeStart}` : ""}.`, tag: "zapara-study-reminder", icon: "/app/icons/icon-512.png" }); }; run(); const timer = window.setInterval(run, 15000); return () => window.clearInterval(timer); }, [app.groupId, app.lessons, app.subgroups, app.invert, app.timetableAvailable, app.session?.authenticated, app.session?.user?.userId, app.privateHomework.settings]);
}
export { NotificationSettings } from "./notification-settings";
export function StudyExtras() {
    const app = useApp();
    const index = subgroupIndex(app.lessons);
    return <article className="card stack homework-study-subgroups"><h2>Подгруппы по предметам</h2>
        {app.canUndoSubgroup&&<button className="btn" type="button" onClick={app.undoSubgroup}>Отменить последний выбор подгруппы</button>}
        <p className="muted">Сначала сравните, какие занятия добавятся или исчезнут. Выбор подгруппы применяется только после подтверждения предпросмотра.</p>
        {index.streams.map(stream => {
            const selected = stream.options.find(option => option.id === app.subgroups[app.groupId]?.[stream.id]);
            return <div className="row homework-study-subgroup" key={stream.id}><b>{stream.title}</b><span className="chip">{selected?.label || "Все занятия"}</span></div>;
        })}
        {index.streams.length === 0 && <p className="muted">В сохранённом расписании разделения на подгруппы нет.</p>}
    </article>;
}
export { AccountDetails, PasswordRecovery } from "./account-details";
export { DataSettings } from "./data-settings-view";
export { UpdateSettings } from "./update-settings-view";
export function BackgroundDelivery() {
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
