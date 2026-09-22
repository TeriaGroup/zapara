const appRoot = () => new URL('/app/', location.origin).href;
let cleanup = [], receiver, reloadRequested = false, reloadAvailable = false, activationTimer;

function basics() {
    const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    return {
        secure: !!globalThis.isSecureContext,
        supported: !!(globalThis.isSecureContext && 'serviceWorker' in navigator && typeof Notification !== 'undefined' && typeof PushManager !== 'undefined'),
        permission: typeof Notification === 'undefined' ? 'unsupported' : Notification.permission,
        ios, installed: !!(window.matchMedia?.('(display-mode: standalone)').matches || navigator.standalone),
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'Europe/Moscow'
    };
}

async function registration() {
    if (!('serviceWorker' in navigator)) return null;
    const value = await navigator.serviceWorker.getRegistration(appRoot());
    return value?.scope === appRoot() ? value : null;
}

export async function inspect() {
    const result = basics();
    const reg = await registration();
    const subscription = reg?.pushManager ? await reg.pushManager.getSubscription() : null;
    return { ...result, registered: !!reg?.active, subscribed: !!subscription, updateAvailable: !!reg?.waiting || reloadAvailable };
}

function keyBytes(value) {
    if (typeof value !== 'string' || !/^[A-Za-z0-9_-]+$/.test(value)) throw new Error('Некорректный ключ уведомлений на сервере.');
    const decoded = atob(value.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - value.length % 4) % 4));
    const bytes = Uint8Array.from(decoded, c => c.charCodeAt(0));
    if (bytes.length !== 65 || bytes[0] !== 4) throw new Error('Некорректный ключ уведомлений на сервере.');
    return bytes;
}

// Called only by the explicit Enable button; permission request occurs before any await.
export async function subscribe(publicKey) {
    const state = basics();
    if (!state.supported) throw new Error('Этот браузер не поддерживает уведомления приложения.');
    if (state.ios && !state.installed) throw new Error('Сначала добавьте Запару на экран Домой и откройте её оттуда.');
    const key = keyBytes(publicKey);
    const permission = state.permission === 'default' ? await Notification.requestPermission() : state.permission;
    if (permission !== 'granted') throw new Error('Разрешение на уведомления не получено. Проверьте настройки браузера.');
    const reg = await registration();
    if (!reg?.active || !reg.pushManager) throw new Error('Приложение ещё не готово к уведомлениям. Перезагрузите его и повторите.');
    let subscription = await reg.pushManager.getSubscription();
    const created = !subscription;
    if (!subscription) {
        try { subscription = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key }); }
        catch { throw new Error('Браузер не смог зарегистрировать уведомления. Проверьте подключение и настройки.'); }
    }
    const raw = subscription.toJSON();
    if (!raw.endpoint || !raw.keys?.p256dh || !raw.keys?.auth) throw new Error('Браузер вернул неполную подписку.');
    return { created, value: { endpoint: raw.endpoint, keys: { p256dh: raw.keys.p256dh, auth: raw.keys.auth }, enabled: true, timeZone: state.timeZone } };
}

export async function unsubscribe() {
    const reg = await registration();
    const current = reg?.pushManager ? await reg.pushManager.getSubscription() : null;
    if (!current) return true;
    try {
        if (!await current.unsubscribe()) throw new Error();
        return true;
    } catch { throw new Error('Браузер не подтвердил отключение уведомлений. Повторите при подключении к сети.'); }
}

function listen(target, name, handler) {
    target.addEventListener(name, handler); cleanup.push(() => target.removeEventListener(name, handler));
}
function observe(promise) {
    promise?.catch(error => {
        const text = String(error && (error.message || error) || '');
        if (/disposed|disconnect|circuit/i.test(text)) return;
        throw error;
    });
}
function notify(available) { observe(receiver?.invokeMethodAsync('UpdateAvailableChanged', available)); }
export async function watch(dotnet) {
    dispose(); receiver = dotnet;
    const reg = await registration();
    if (!reg) return false;
    let previousController = navigator.serviceWorker.controller;
    listen(navigator.serviceWorker, 'controllerchange', () => {
        clearTimeout(activationTimer);
        if (reloadRequested) { location.reload(); return; }
        if (previousController && previousController !== navigator.serviceWorker.controller) { reloadAvailable = true; notify(true); }
        previousController = navigator.serviceWorker.controller;
    });
    const installing = () => {
        const worker = reg.installing;
        if (worker) listen(worker, 'statechange', () => { if (worker.state === 'installed' && navigator.serviceWorker.controller) notify(!!reg.waiting); });
    };
    listen(reg, 'updatefound', installing); installing(); notify(!!reg.waiting || reloadAvailable);
    return true;
}

export async function checkUpdate() {
    const reg = await registration();
    if (!reg) throw new Error('Обновление приложения пока недоступно. Откройте его по защищённому адресу.');
    try { await reg.update(); } catch { throw new Error('Проверить обновление не удалось. Повторите после подключения к сети.'); }
    return !!reg.waiting || reloadAvailable;
}

export async function activateUpdate() {
    const reg = await registration();
    if (reg?.waiting) {
        reloadRequested = true;
        reg.waiting.postMessage({ type: 'ACTIVATE_UPDATE' });
        activationTimer = setTimeout(() => { reloadRequested = false; observe(receiver?.invokeMethodAsync('UpdateActivationFailed')); }, 20000);
    } else if (reloadAvailable) location.reload();
    else throw new Error('Готового обновления нет. Сначала проверьте наличие новой версии.');
}

export function dispose() {
    cleanup.forEach(action => action()); cleanup = []; receiver = null;
    clearTimeout(activationTimer); reloadRequested = false;
}
