import test from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';

let sequence = 0;
function environment(session = new Map(), locks = new Set()) {
    if (!globalThis.crypto) Object.defineProperty(globalThis, 'crypto', { configurable: true, value: webcrypto });
    globalThis.window = new EventTarget();
    globalThis.sessionStorage = { getItem: key => session.get(key) ?? null, setItem: (key, value) => session.set(key, value) };
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { locks: { async request(name, _, action) {
        if (locks.has(name)) return action(null);
        locks.add(name);
        try { return await action({ name }); } finally { locks.delete(name); }
    } } } });
    return { session, locks, window };
}
async function module() { return import('../Zapara.Web/wwwroot/js/drafts.js?test=' + ++sequence); }

test('draft tab identity survives a reload and stores no form data in sessionStorage', async () => {
    const env = environment(); const first = await module(); const id = await first.tabId();
    assert.equal(await first.tabId(), id); first.dispose(); await Promise.resolve();
    environment(env.session, env.locks); const second = await module();
    assert.equal(await second.tabId(), id);
    assert.deepEqual([...env.session.keys()], ['zapara.drafts-tab.v1']);
    second.dispose();
});

test('a duplicated live tab with cloned sessionStorage gets a separate draft identity', async () => {
    const env = environment(); const first = await module(); const id = await first.tabId();
    const duplicate = new Map(env.session); environment(duplicate, env.locks); const second = await module();
    assert.notEqual(await second.tabId(), id);
    assert.equal(env.session.get('zapara.drafts-tab.v1'), id);
    second.dispose(); first.dispose();
});

test('unload prompts only while draft writes are pending', async () => {
    const env = environment(); const drafts = await module(); await drafts.tabId();
    drafts.setPending(true); const blocked = new Event('beforeunload', { cancelable: true }); env.window.dispatchEvent(blocked);
    assert.equal(blocked.defaultPrevented, true);
    drafts.setPending(false); const ready = new Event('beforeunload', { cancelable: true }); env.window.dispatchEvent(ready);
    assert.equal(ready.defaultPrevented, false); drafts.dispose();
});
