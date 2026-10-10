import { useEffect, useRef, useState } from "react";
import * as api from "./api";
import { useApp } from "./store";
import { mergeDevicePages, resetRequestReady, resetConfirmationReady } from "./account-actions";
import { scalarInput } from "./scalar-input";
import { displayNameDraft,rememberDisplayName,clearDisplayNameDraft } from "./account-form-draft";
import { browseDevices } from "./ux300";
import { FilterEmpty, SearchField, SecretInput } from "./ux300-controls";
import { Sheet } from "./sheet";
const methodLabels: Record<string, string> = {vk:"VK ID",yandex:"Яндекс ID",password:"Логин и пароль"};

export function AccountDetails() {
  const app = useApp();
  return <AccountDetailContent key={JSON.stringify([app.session?.user?.userId, app.session?.familyId])} />;
}
function AccountDetailContent() {
  const app = useApp();
  const [methods, setMethods] = useState<string[]>([]);
  const [devices, setDevices] = useState<api.AccountDevice[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState("");
  const [retry, setRetry] = useState(0);
  const [methodError, setMethodError] = useState(false);
  const [deviceError, setDeviceError] = useState(false);
  const [deviceQuery, setDeviceQuery] = useState("");
  const [otherDevices, setOtherDevices] = useState(false);
  const [deviceDetail, setDeviceDetail] = useState<string | null>(null);
  const owner=app.session?.user?.userId||"guest";
  const confirmedName=app.session?.user?.displayName||app.session?.user?.username||"";
  const [name,setName]=useState(()=>displayNameDraft(owner,confirmedName));
  const [nameBase,setNameBase]=useState(confirmedName);
  const [currentPassword,setCurrentPassword]=useState("");const[newPassword,setNewPassword]=useState("");
  const validName = scalarInput(name.trim(),1,80,true);
  const visibleDevices = browseDevices(devices, deviceQuery, otherDevices);
  const selectedDevice = devices.find(device => device.familyId === deviceDetail);
  useEffect(() => { if (name === nameBase && confirmedName !== nameBase) { setName(confirmedName); setNameBase(confirmedName); clearDisplayNameDraft(owner); } }, [confirmedName, name, nameBase]);
  const action = useRef(false);
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => {
    let stopped = false; setLoading(true); setNotice("");
    void Promise.allSettled([api.accountMe(), api.accountDevices()]).then(([me, page]) => {
      if (stopped) return;
      if (me.status === "fulfilled") setMethods(me.value.authenticationMethods);
      if (page.status === "fulfilled") { setDevices(mergeDevicePages([], page.value.devices)); setCursor(page.value.nextCursor); }
      setMethodError(me.status === "rejected"); setDeviceError(page.status === "rejected");
      if (me.status === "rejected" || page.status === "rejected") setNotice("Часть данных аккаунта не загрузилась. Сохранённые данные остаются на экране.");
      setLoading(false);
    });
    return () => { stopped = true; };
  }, [retry]);
  async function retryBlock(kind: "methods" | "devices") {
    if (action.current) return;
    action.current = true; setBusy(true);
    try {
      if (kind === "methods") { const result = await api.accountMe(); if (alive.current) { setMethods(result.authenticationMethods); setMethodError(false); } }
      else { const result = await api.accountDevices(); if (alive.current) { setDevices(mergeDevicePages([], result.devices)); setCursor(result.nextCursor); setDeviceError(false); } }
    } catch { if (alive.current) setNotice("Блок не обновился. Сохранённые сведения остаются доступны."); }
    finally { action.current = false; if (alive.current) setBusy(false); }
  }
  async function more() {
    if (!cursor || action.current) return;
    action.current = true; setBusy(true); setNotice("");
    const after = cursor;
    try { const page = await api.accountDevices(after); if (alive.current) { setDevices(rows => mergeDevicePages(rows, page.devices)); setCursor(page.nextCursor === after ? null : page.nextCursor); } }
    catch { if (alive.current) setNotice("Следующая страница не загрузилась. Нажмите «Ещё устройства», чтобы повторить."); }
    finally { action.current = false; if (alive.current) setBusy(false); }
  }
  async function saveName() {
    const submitted=name.trim();if(action.current||!scalarInput(submitted,1,80,true)||submitted===nameBase)return;
    action.current=true;setBusy(true);setNotice("");
    try{const user=await api.updateDisplayName(submitted);if(alive.current&&user.userId===app.session?.user?.userId){app.acceptProfileName(user);setNameBase(user.displayName||user.username);clearDisplayNameDraft(owner);setNotice("Имя сохранено.");}}
    catch{if(alive.current)setNotice("Имя не сохранилось. Ввод оставлен — повторите сохранение.");}
    finally{action.current=false;if(alive.current)setBusy(false);}
  }
  async function changePassword() {
    if(action.current||!methods.includes("password")||currentPassword===newPassword||!scalarInput(currentPassword,12,128,false)||!scalarInput(newPassword,12,128,false))return;
    action.current=true;setBusy(true);setNotice("");
    try{await api.changeAccountPassword(currentPassword,newPassword);if(!alive.current)return;setNotice("Пароль изменён. Войдите заново на устройствах.");try{await app.refreshSession();}catch{if(alive.current)setNotice("Пароль изменён. Не удалось обновить состояние аккаунта; повторите загрузку.");}}
    catch{if(alive.current)setNotice("Пароль не изменён. Проверьте текущий пароль и повторите.");}
    finally{action.current=false;if(alive.current){setCurrentPassword("");setNewPassword("");setBusy(false);}}
  }
  async function revoke(device: api.AccountDevice | null) {
    if (action.current) return;
    const target = device ? { familyId: device.familyId, name: device.deviceName, current: device.isCurrent } : null;
    const message = !target ? "Завершить все сеансы? Вы выйдете из аккаунта на всех устройствах, включая это." : target.current ? `Завершить сеанс «${target.name}» на этом устройстве? Потребуется снова войти.` : `Завершить сеанс «${target.name}»? На том устройстве потребуется снова войти.`;
    if (!window.confirm(message)) return;
    action.current = true; setBusy(true); setNotice("");
    try {
      if (target) await api.revokeDevice(target.familyId); else await api.revokeAllDevices();
      if (!alive.current) return;
      if (!target || target.current) {
        setNotice("Сеанс завершён. Обновляем состояние аккаунта…");
        try { await app.refreshSession(); }
        catch { if (alive.current) setNotice("Сеанс завершён, но экран аккаунта не обновился. Повторите загрузку аккаунта."); }
      }
      else { setDevices(rows => rows.filter(row => row.familyId !== target.familyId)); setNotice(`Сеанс «${target.name}» завершён.`); }
    } catch { if (alive.current) setNotice("Сеанс не завершён. Повторите попытку."); }
    finally { action.current = false; if (alive.current) setBusy(false); }
  }
  return <article className="card stack"><h2>Способ входа</h2><p>{methods.map(method => methodLabels[method] ?? method).join(", ") || (loading ? "Загружаем…" : "Способы входа не загрузились")}</p>
    {methodError && <button className="btn" disabled={busy||loading} onClick={() => void retryBlock("methods")}>Повторить загрузку способов входа</button>}
    <label className="field">Имя в приложении<input value={name} disabled={busy} aria-invalid={!validName} onChange={event=>{setName(event.target.value);rememberDisplayName(owner,event.target.value);}}/></label><p className="muted">От 1 до 80 символов.</p>{!validName && <p role="status">Введите имя от 1 до 80 символов без недопустимых символов.</p>}<div className="row"><button className="btn" type="button" disabled={busy||!validName||name.trim()===nameBase} onClick={()=>void saveName()}>Сохранить имя</button>{name !== nameBase && <button className="btn quiet" disabled={busy} onClick={() => { if (window.confirm("Вернуть последнее сохранённое имя?")) { setName(confirmedName); setNameBase(confirmedName); clearDisplayNameDraft(owner); } }}>Отменить правки имени</button>}</div>
    {confirmedName !== nameBase && <div className="banner" role="status">Имя обновилось на другом устройстве: {confirmedName}. Ваш текст оставлен в поле.</div>}
    {methods.includes("password")&&<details className="stack" onToggle={event => { if (!event.currentTarget.open) { setCurrentPassword(""); setNewPassword(""); } }}><summary>Изменить пароль</summary><SecretInput label="Текущий пароль" autoComplete="current-password" disabled={busy} value={currentPassword} onChange={setCurrentPassword}/><SecretInput label="Новый пароль" autoComplete="new-password" disabled={busy} value={newPassword} onChange={setNewPassword}/><p className="muted">12–128 символов. После изменения потребуется снова войти.</p>{newPassword && currentPassword === newPassword && <p role="status">Новый пароль должен отличаться от текущего.</p>}<button className="btn" type="button" disabled={busy||currentPassword===newPassword||!scalarInput(currentPassword,12,128,false)||!scalarInput(newPassword,12,128,false)} onClick={()=>void changePassword()}>Изменить пароль</button></details>}
    {notice && <p role="status">{notice}</p>}<button className="btn" type="button" disabled={loading || busy} onClick={() => setRetry(value => value + 1)}>{loading ? "Обновляем…" : "Обновить данные аккаунта"}</button>
    <h2>Активные устройства · {devices.length}</h2>{deviceError && <button className="btn" disabled={busy||loading} onClick={() => void retryBlock("devices")}>Повторить загрузку устройств</button>}<SearchField label="Найти устройство" value={deviceQuery} onChange={setDeviceQuery}/><label className="check"><input type="checkbox" checked={otherDevices} onChange={event => setOtherDevices(event.target.checked)}/>Только другие устройства</label>{visibleDevices.length === 0 && !loading && <FilterEmpty text={cursor ? "На загруженной странице таких устройств нет. Можно загрузить следующую." : otherDevices ? "Других устройств по выбранным условиям нет." : "Устройства по запросу не найдены."} onReset={() => { setOtherDevices(false); setDeviceQuery(""); }}/>}<p className="muted">Показано {visibleDevices.length} из {devices.length} загруженных</p>{visibleDevices.map(device => <div className="row device-line" key={device.familyId}><div><b>{device.deviceName}</b><p className="muted">{device.platform} · {new Date(device.lastSeenAt).toLocaleString("ru-RU")}{device.isCurrent ? " · Это устройство" : ""}</p></div><button className="btn quiet" type="button" onClick={() => setDeviceDetail(device.familyId)}>Подробности сеанса</button><button className="btn quiet" type="button" disabled={busy || loading} onClick={() => void revoke(device)}>Завершить сеанс</button></div>)}
    {selectedDevice && <Sheet title={selectedDevice.deviceName} onClose={() => setDeviceDetail(null)}><p>{selectedDevice.platform}{selectedDevice.isCurrent ? " · Это устройство" : ""}</p><p>Последняя активность: {new Date(selectedDevice.lastSeenAt).toLocaleString("ru-RU")}</p><p>Сеанс действует до {new Date(selectedDevice.expiresAt).toLocaleString("ru-RU")}</p><button className="btn" disabled={busy || loading} onClick={() => void revoke(selectedDevice)}>Завершить этот сеанс</button></Sheet>}
    {cursor && <button className="btn" type="button" disabled={busy || loading} onClick={() => void more()}>{busy ? "Подождите…" : "Ещё устройства"}</button>}
    {devices.length > 0 && <button className="btn quiet" type="button" disabled={busy || loading} onClick={() => void revoke(null)}>Завершить все сеансы</button>}
  </article>;
}

export function PasswordRecovery({ login = "", onLogin }: { login?: string; onLogin?: (username: string) => void } = {}) {
  const app = useApp();
  const [username, setUsername] = useState("");
  const [token, setToken] = useState("");
  const [password, setPassword] = useState("");
  const [requested, setRequested] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [completed, setCompleted] = useState(false);
  const pending = useRef(false);
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  if (app.session?.authenticated || !app.session?.capabilities.recovery) return null;
  async function run(confirm: boolean) {
    if (pending.current || !(confirm ? resetConfirmationReady(token, password) : resetRequestReady(username))) return;
    pending.current = true; setBusy(true); setNotice("");
    try {
      if (confirm) await api.confirmPasswordReset(token.trim(), password); else await api.requestPasswordReset(username.trim());
      if (!alive.current) return;
      if (confirm) { setPassword(""); setToken(""); setCompleted(true); setNotice("Пароль изменён. Теперь можно войти с новым паролем."); }
      else { setRequested(true); setNotice("Если для этого аккаунта настроено восстановление, инструкция отправлена на подтверждённую почту."); }
    } catch { if (alive.current) setNotice(confirm ? "Пароль не изменён. Проверьте код и срок его действия." : "Не удалось запросить инструкцию. Попробуйте позже."); }
    finally { pending.current = false; if (alive.current) setBusy(false); }
  }
  return <details className="card stack password-recovery" id="password-recovery" onToggle={event => { if (!event.currentTarget.open) { setToken(""); setPassword(""); } }}><summary>Восстановить пароль</summary><p className="muted">1. Получите инструкцию на подтверждённую почту.</p><label className="field">Логин<input value={username} disabled={busy} autoComplete="username" onChange={event => setUsername(event.target.value)} maxLength={32}/></label>{login.trim() && login !== username && <button className="btn quiet" disabled={busy} onClick={() => setUsername(login)}>Использовать логин из формы входа</button>}<button className="btn" type="button" disabled={busy || !resetRequestReady(username)} onClick={() => void run(false)}>Получить инструкцию</button>{completed && onLogin && <button className="btn primary" onClick={() => { setToken(""); setPassword(""); onLogin(username); }}>Перейти ко входу</button>}
    <button className="btn quiet" type="button" onClick={() => setRequested(value => !value)} aria-expanded={requested}>У меня есть код восстановления</button>
    {requested && <div className="stack"><p className="muted">2. Введите код из инструкции и новый пароль: от 12 до 128 символов.</p><SecretInput label="Код восстановления" value={token} disabled={busy} autoComplete="one-time-code" onChange={setToken}/><SecretInput label="Новый пароль" value={password} disabled={busy} autoComplete="new-password" onChange={setPassword}/><button className="btn primary" type="button" disabled={busy || !resetConfirmationReady(token,password)} onClick={() => void run(true)}>Сохранить новый пароль</button></div>}{notice && <p role="status">{notice}</p>}</details>;
}
