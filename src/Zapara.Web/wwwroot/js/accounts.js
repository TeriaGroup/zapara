const key = 'zapara.account.oauth';
const actions = new Set(['login', 'link:vk', 'link:yandex', 'unlink:vk', 'unlink:yandex', 'set_password', 'set_recovery_email', 'export', 'delete_account']);
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

function validPending(value) {
    return value && uuid.test(value.transactionId) && actions.has(value.action)
        && (value.familyId === null || uuid.test(value.familyId))
        && typeof value.expiresAt === 'string' && Number.isFinite(Date.parse(value.expiresAt));
}
export function savePending(value) {
    if (!validPending(value)) throw new Error('Некорректное состояние подтверждения.');
    // Explicit allowlist: credentials, proof tokens and authorization URLs never enter storage.
    sessionStorage.setItem(key, JSON.stringify({ transactionId: value.transactionId, action: value.action, familyId: value.familyId, expiresAt: value.expiresAt }));
}
export function readPending() {
    const raw = sessionStorage.getItem(key);
    if (!raw) return null;
    try { const value = JSON.parse(raw); if (validPending(value)) return value; }
    catch { }
    clearPending();
    return null;
}
export function clearPending() { sessionStorage.removeItem(key); }
export function navigateProvider(provider, target) {
    const url = new URL(target);
    const host = provider === 'vk' ? 'id.vk.ru' : provider === 'yandex' ? 'oauth.yandex.ru' : null;
    if (!host || url.protocol !== 'https:' || url.hostname !== host || (url.port && url.port !== '443')
        || url.pathname !== '/authorize' || url.username || url.password || url.hash) throw new Error('Недопустимый адрес входа.');
    window.location.assign(url.href);
}
export function download(bytes, filename) {
    if (!(bytes instanceof Uint8Array) || bytes.byteLength === 0 || bytes.byteLength > 16 * 1024 * 1024
        || !/^zapara-export-[0-9a-f-]{36}\.json$/.test(filename)) throw new Error('Некорректный файл экспорта.');
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/json;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url; link.download = filename; link.hidden = true;
    document.body.appendChild(link);
    try { link.click(); }
    finally { link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
}
