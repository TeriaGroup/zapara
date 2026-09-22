const key = 'zapara.drafts-tab.v1';
let identity, releaseTab, pending = false;
const warn = event => { if (pending) { event.preventDefault(); event.returnValue = ''; } };
window.addEventListener('beforeunload', warn);

async function claim(id) {
    if (!navigator.locks?.request) return true;
    return new Promise((resolve, reject) => {
        navigator.locks.request('zapara.drafts-tab.' + id, { ifAvailable: true }, lock => {
            if (!lock) { resolve(false); return; }
            resolve(true);
            return new Promise(release => { releaseTab = release; });
        }).catch(reject);
    });
}

export function tabId() {
    return identity ||= (async () => {
        let id = sessionStorage.getItem(key);
        if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id || '')) id = crypto.randomUUID();
        // Duplicating a tab can clone sessionStorage. A live owner keeps its identity;
        // the duplicate gets another slot, while a reload reclaims the released slot.
        if (!await claim(id)) { id = crypto.randomUUID(); if (!await claim(id)) throw new Error('Не удалось открыть черновики этой вкладки.'); }
        sessionStorage.setItem(key, id);
        return id;
    })();
}
export function setPending(value) { pending = value === true; }
export function dispose() { releaseTab?.(); releaseTab = undefined; pending = false; window.removeEventListener('beforeunload', warn); }
