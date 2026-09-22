const markerKey = 'zapara.session-generation.v1';
const tabKey = 'zapara.session-tab.v1';
const lockName = 'zapara.session-transition.v1';
const redirectLifetime = 15 * 60 * 1000; // Server OAuth transactions expire after ten minutes.
let tabId, receiver, channel, timer, removers = [], releaseLock, heldGeneration, observed, viewMasked = false;
const initial = () => ({ generation: 'initial', phase: 'stable', ownerTab: '', changedAt: 0 });
const mask = () => { viewMasked = true; document.documentElement.setAttribute('data-session-transition', 'true'); };

function read() {
    const raw = localStorage.getItem(markerKey);
    if (!raw) return initial();
    let value;
    try { value = JSON.parse(raw); } catch { throw new Error('Состояние переключения аккаунта повреждено.'); }
    if (!value || typeof value.generation !== 'string' || value.generation.length > 80
        || !['stable', 'transition', 'redirect', 'verify'].includes(value.phase)
        || typeof value.ownerTab !== 'string' || value.ownerTab.length > 80
        || !Number.isSafeInteger(value.changedAt) || value.changedAt < 0)
        throw new Error('Состояние переключения аккаунта повреждено.');
    return { generation: value.generation, phase: value.phase, ownerTab: value.ownerTab, changedAt: value.changedAt };
}

function notice(force = false) {
    try {
        const marker = read(), signature = JSON.stringify(marker);
        if (force || signature !== observed || marker.phase !== 'stable') {
            mask(); observed = signature;
            observe(receiver?.invokeMethodAsync('SessionMarkerChanged', marker));
        }
    } catch {
        mask(); observe(receiver?.invokeMethodAsync('SessionCoordinationFailed'));
    }
}
function observe(promise) {
    promise?.catch(error => {
        const text = String(error && (error.message || error) || '');
        if (/disposed|disconnect|circuit/i.test(text)) return;
        throw error;
    });
}

function publish(marker) {
    localStorage.setItem(markerKey, JSON.stringify(marker));
    mask(); observed = JSON.stringify(marker);
    channel?.postMessage({ generation: marker.generation });
}

export async function initialize(dotnet) {
    if (receiver) dispose();
    receiver = dotnet;
    tabId = sessionStorage.getItem(tabKey);
    if (!tabId) { tabId = crypto.randomUUID(); sessionStorage.setItem(tabKey, tabId); }
    const marker = read(); observed = JSON.stringify(marker);
    const listener = event => { if (event.key === markerKey) notice(); };
    window.addEventListener('storage', listener); removers.push(() => window.removeEventListener('storage', listener));
    const visible = () => { if (document.visibilityState === 'visible') notice(); };
    document.addEventListener('visibilitychange', visible); removers.push(() => document.removeEventListener('visibilitychange', visible));
    if (typeof BroadcastChannel !== 'undefined') {
        channel = new BroadcastChannel(lockName); channel.onmessage = () => notice();
    }
    // A closed owner releases its Web Lock without a storage event. Recover on the next bounded check.
    timer = setInterval(() => { if (viewMasked) notice(true); }, 5000);
    if (marker.phase !== 'stable') mask();
    return { tabId, marker, exclusiveSupported: !!navigator.locks?.request };
}

export function current(expectedGeneration) {
    const value = read();
    if (expectedGeneration && expectedGeneration !== value.generation) mask();
    return value;
}

function acquire(phase, recovery) {
    if (!navigator.locks?.request) return Promise.reject(new Error('Браузер не поддерживает безопасное переключение аккаунтов между вкладками.'));
    if (releaseLock) return Promise.resolve(null);
    return new Promise((resolve, reject) => {
        navigator.locks.request(lockName, { mode: 'exclusive', ifAvailable: true }, async lock => {
            if (!lock) { resolve(null); return; }
            const previous = read();
            if (previous.phase === 'redirect' && previous.ownerTab !== tabId && Date.now() - previous.changedAt < redirectLifetime) { resolve(null); return; }
            if (!recovery && previous.phase !== 'stable') { resolve(null); return; }
            const waitingForRedirect = recovery && previous.phase === 'redirect' && Date.now() - previous.changedAt < redirectLifetime;
            const marker = { generation: crypto.randomUUID(), phase: waitingForRedirect ? 'redirect' : phase, ownerTab: tabId,
                changedAt: waitingForRedirect ? previous.changedAt : Date.now() };
            let release;
            const held = new Promise(done => release = done);
            try { publish(marker); } catch (error) { reject(error); return; }
            releaseLock = release; heldGeneration = marker.generation;
            resolve(marker);
            await held;
        }).catch(reject);
    });
}

export function begin(phase = 'transition') {
    if (!['transition', 'redirect'].includes(phase)) throw new Error('Некорректный переход аккаунта.');
    return acquire(phase, false);
}
export function recover() {
    if (read().phase === 'stable') return Promise.resolve(null);
    return acquire('transition', true);
}
export function finish(generation) {
    return release(generation, 'stable');
}
export function abandon(generation) {
    return release(generation, 'verify');
}
export function park(generation) {
    const marker = read();
    if (!releaseLock || heldGeneration !== generation || marker.generation !== generation || marker.ownerTab !== tabId || marker.phase !== 'redirect') return false;
    const release = releaseLock; releaseLock = null; heldGeneration = null; release(); return true;
}
function release(generation, phase) {
    const marker = read();
    if (!releaseLock || heldGeneration !== generation || marker.generation !== generation || marker.ownerTab !== tabId) return false;
    try { publish({ ...marker, phase, changedAt: Date.now() }); }
    finally { const release = releaseLock; releaseLock = null; heldGeneration = null; release(); }
    return true;
}
export function releaseView(generation) {
    const marker = read();
    if (marker.generation !== generation || marker.phase !== 'stable') return false;
    viewMasked = false; document.documentElement.removeAttribute('data-session-transition'); return true;
}
export function dispose() {
    clearInterval(timer); removers.forEach(remove => remove()); removers = [];
    channel?.close(); channel = null; receiver = null;
    if (releaseLock) { const release = releaseLock; releaseLock = null; heldGeneration = null; release(); }
}
