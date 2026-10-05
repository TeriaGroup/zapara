import test from 'node:test';
import assert from 'node:assert/strict';

let sequence = 0;
function environment() {
    const local = new Map(), session = new Map(), attrs = new Map();
    let held = false;
    const window = new EventTarget();
    globalThis.window = window;
    globalThis.document = Object.assign(new EventTarget(), { visibilityState: 'visible', documentElement: { setAttribute: (k, v) => attrs.set(k, v), removeAttribute: k => attrs.delete(k) } });
    globalThis.localStorage = { getItem: k => local.get(k) ?? null, setItem: (k, v) => local.set(k, v) };
    globalThis.sessionStorage = { getItem: k => session.get(k) ?? null, setItem: (k, v) => session.set(k, v) };
    globalThis.BroadcastChannel = undefined;
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { locks: { request: async (_, __, callback) => {
        if (held) return callback(null);
        held = true; try { return await callback({ name: 'test-lock' }); } finally { held = false; }
    } } } });
    return { local, session, attrs, window };
}
async function module() { return import('../Zapara.Web/wwwroot/js/session-coordination.js?test=' + ++sequence); }
const receiver = { invokeMethodAsync: async () => {} };

test('parked OAuth keeps its original deadline and masks until verified completion', async () => {
    environment(); const api = await module(); await api.initialize(receiver);
    const first = await api.begin('redirect'); assert.ok(first);
    assert.equal(api.park(first.generation), true);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(await api.begin('transition'), null);
    const recovered = await api.recover();
    assert.equal(recovered.phase, 'redirect'); assert.equal(recovered.changedAt, first.changedAt);
    assert.equal(api.releaseView(recovered.generation), false);
    assert.equal(api.finish(recovered.generation), true); api.dispose();
});

test('failed verification releases exclusive ownership but cannot expose the previous session', async () => {
    environment(); const api = await module(); await api.initialize(receiver);
    const marker = await api.begin('transition');
    assert.equal(api.abandon(marker.generation), true);
    assert.equal(api.current().phase, 'verify'); assert.equal(api.releaseView(marker.generation), false);
    await new Promise(resolve => setImmediate(resolve));
    const recovered = await api.recover(); assert.ok(recovered);
    api.finish(recovered.generation); api.dispose();
});

test('auth transition is persisted without secrets and exclusive until matching completion', async () => {
    const env = environment(); const api = await module(); const initial = await api.initialize(receiver);
    const begun = await api.begin('transition');
    assert.ok(begun); assert.notEqual(begun.generation, initial.marker.generation);
    assert.equal(api.current().phase, 'transition'); assert.equal(env.attrs.get('data-session-transition'), 'true');
    const marker = JSON.parse([...env.local.values()][0]);
    assert.deepEqual(Object.keys(marker).sort(), ['changedAt', 'generation', 'ownerTab', 'phase']);
    assert.equal(api.finish('stale-generation'), false);
    assert.equal(api.finish(begun.generation), true);
    assert.equal(api.current().phase, 'stable');
    assert.equal(api.releaseView(begun.generation), true); assert.equal(env.attrs.has('data-session-transition'), false);
    api.dispose();
});

test('storage event masks private content synchronously before dotnet notification resolves', async () => {
    const env = environment(); const api = await module(); await api.initialize(receiver);
    const foreign = { generation: crypto.randomUUID(), phase: 'transition', ownerTab: crypto.randomUUID(), changedAt: Date.now() };
    localStorage.setItem('zapara.session-generation.v1', JSON.stringify(foreign));
    const event = new Event('storage'); Object.defineProperty(event, 'key', { value: 'zapara.session-generation.v1' }); env.window.dispatchEvent(event);
    assert.equal(env.attrs.get('data-session-transition'), 'true');
    assert.equal(api.current().generation, foreign.generation); api.dispose();
});

test('reload owner can recover abandoned redirect but another tab cannot steal a live OAuth window', async () => {
    const env = environment(); const api = await module(); const initialized = await api.initialize(receiver);
    localStorage.setItem('zapara.session-generation.v1', JSON.stringify({ generation: crypto.randomUUID(), phase: 'redirect', ownerTab: crypto.randomUUID(), changedAt: Date.now() }));
    assert.equal(await api.recover(), null);
    localStorage.setItem('zapara.session-generation.v1', JSON.stringify({ generation: crypto.randomUUID(), phase: 'redirect', ownerTab: initialized.tabId, changedAt: Date.now() }));
    const owned = await api.recover(); assert.ok(owned); assert.equal(owned.phase, 'redirect');
    api.finish(owned.generation); api.dispose();
});

test('abandoned ordinary transition is recoverable only after exclusive owner lock is released', async () => {
    environment(); const api = await module(); await api.initialize(receiver);
    const first = await api.begin('transition'); assert.ok(first);
    api.dispose();
    await new Promise(resolve => setImmediate(resolve));
    const again = await module(); await again.initialize(receiver);
    const recovered = await again.recover(); assert.ok(recovered); assert.notEqual(recovered.generation, first.generation);
    again.finish(recovered.generation); again.dispose();
});
