import test from 'node:test';
import assert from 'node:assert/strict';

let sequence = 0;
function environment({ permission = 'default', installed = false, ios = false } = {}) {
    const calls = { permission: 0, subscribe: 0, unsubscribe: 0, reload: 0, activate: 0 };
    let current = null;
    const subscription = {
        endpoint: 'https://push.example.test/subscription', options: {},
        toJSON: () => ({ endpoint: 'https://push.example.test/subscription', expirationTime: null, keys: { p256dh: 'public-key', auth: 'authentication-key' } }),
        unsubscribe: async () => { calls.unsubscribe++; current = null; return true; }
    };
    const registration = new EventTarget();
    Object.assign(registration, { scope: 'https://example.test/app/', active: {}, installing: null, waiting: null,
        update: async () => {}, pushManager: { getSubscription: async () => current, subscribe: async () => { calls.subscribe++; current = subscription; return current; } } });
    const serviceWorker = new EventTarget();
    Object.assign(serviceWorker, { controller: {}, ready: Promise.resolve(registration), getRegistration: async () => registration });
    const notifications = class {};
    notifications.permission = permission;
    notifications.requestPermission = async () => { calls.permission++; notifications.permission = permission === 'denied' ? 'denied' : 'granted'; return notifications.permission; };
    globalThis.Notification = notifications;
    globalThis.PushManager = class {};
    globalThis.isSecureContext = true;
    globalThis.location = { origin: 'https://example.test', href: 'https://example.test/app/settings', reload: () => { calls.reload++; } };
    globalThis.document = { baseURI: 'https://example.test/app/' };
    globalThis.window = { matchMedia: () => ({ matches: installed }), location: globalThis.location };
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { serviceWorker, userAgent: ios ? 'iPhone' : 'test', platform: ios ? 'iPhone' : 'test', maxTouchPoints: ios ? 5 : 0, standalone: installed } });
    return { calls, registration, serviceWorker, subscription };
}
async function module() { return import('../Zapara.Web/wwwroot/js/notifications.js?test=' + ++sequence); }
const publicKey = Buffer.from([4, ...Array(64).fill(1)]).toString('base64url');

test('watch can be retried after the first installation has registered its service worker', async () => {
    const env = environment(); const api = await module();
    env.serviceWorker.getRegistration = async () => null;
    assert.equal(await api.watch({ invokeMethodAsync: async () => {} }), false);
    env.serviceWorker.getRegistration = async () => env.registration;
    assert.equal(await api.watch({ invokeMethodAsync: async () => {} }), true);
    api.dispose();
});

test('inspection reports permission and iPhone install requirement without prompting', async () => {
    const env = environment({ ios: true }); const api = await module();
    const value = await api.inspect();
    assert.equal(value.permission, 'default'); assert.equal(value.ios, true); assert.equal(value.installed, false);
    assert.equal(env.calls.permission, 0); assert.equal(env.calls.subscribe, 0);
});

test('only explicit subscription prompts then returns the exact server payload', async () => {
    const env = environment(); const api = await module();
    const result = await api.subscribe(publicKey);
    assert.equal(env.calls.permission, 1); assert.equal(env.calls.subscribe, 1);
    assert.equal(result.created, true);
    assert.deepEqual(Object.keys(result.value).sort(), ['enabled', 'endpoint', 'keys', 'timeZone']);
    assert.equal(result.value.enabled, true); assert.equal(result.value.endpoint, env.subscription.endpoint);
    assert.equal(await api.unsubscribe(), true); assert.equal(env.calls.unsubscribe, 1);
});

test('denied permission and uninstalled iPhone never create subscriptions', async () => {
    const denied = environment({ permission: 'denied' }); const a = await module();
    await assert.rejects(() => a.subscribe(publicKey)); assert.equal(denied.calls.subscribe, 0);
    const iphone = environment({ ios: true }); const b = await module();
    await assert.rejects(() => b.subscribe(publicKey)); assert.equal(iphone.calls.permission, 0); assert.equal(iphone.calls.subscribe, 0);
});

test('another application service worker cannot acquire this app subscription', async () => {
    const env = environment({ permission: 'granted' }); env.registration.scope = 'https://example.test/another-app/';
    const api = await module(); await assert.rejects(() => api.subscribe(publicKey)); assert.equal(env.calls.subscribe, 0);
});

test('controller replacement does not reload until the user explicitly activates an update', async () => {
    const env = environment(); const api = await module();
    await api.watch({ invokeMethodAsync: async () => {} });
    env.serviceWorker.controller = {}; env.serviceWorker.dispatchEvent(new Event('controllerchange'));
    assert.equal(env.calls.reload, 0);
    await api.activateUpdate(); assert.equal(env.calls.reload, 1);
    api.dispose();
});

test('waiting update requires explicit activation and reloads only on controller change', async () => {
    const env = environment(); const api = await module();
    env.registration.waiting = { postMessage: message => { assert.deepEqual(message, { type: 'ACTIVATE_UPDATE' }); env.calls.activate++; } };
    await api.watch({ invokeMethodAsync: async () => {} });
    assert.equal((await api.inspect()).updateAvailable, true); assert.equal(env.calls.activate, 0);
    await api.activateUpdate(); assert.equal(env.calls.activate, 1); assert.equal(env.calls.reload, 0);
    env.serviceWorker.controller = {}; env.serviceWorker.dispatchEvent(new Event('controllerchange'));
    assert.equal(env.calls.reload, 1); api.dispose();
});
