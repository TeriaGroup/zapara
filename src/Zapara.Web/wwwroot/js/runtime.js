let detach;
export function start(reference) {
    stop();
    const wake = reason => { observe(reference.invokeMethodAsync('BrowserRuntimeWake', reason)); };
    const online = () => wake('online');
    const focus = () => { if (document.visibilityState !== 'hidden') wake('focus'); };
    const visible = () => { if (document.visibilityState === 'visible') wake('visible'); };
    window.addEventListener('online', online);
    window.addEventListener('focus', focus);
    document.addEventListener('visibilitychange', visible);
    detach = () => {
        window.removeEventListener('online', online);
        window.removeEventListener('focus', focus);
        document.removeEventListener('visibilitychange', visible);
    };
}
function observe(promise) {
    promise.catch(error => {
        const text = String(error && (error.message || error) || '');
        if (/disposed|disconnect|circuit/i.test(text)) return;
        throw error;
    });
}
export function stop() { detach?.(); detach = undefined; }
