import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { Script } from 'node:vm';

async function worker(windows = []) {
    const handlers = {}, shown = [], opened = [];
    const self = {
        location: { origin: 'https://example.test' },
        addEventListener: (name, handler) => handlers[name] = handler,
        registration: { showNotification: async (title, options) => shown.push({ title, options }) },
        clients: { matchAll: async () => windows, openWindow: async url => opened.push(url) }
    };
    new Script(await readFile(new URL('../Zapara.Web/wwwroot/js/push-worker.js', import.meta.url), 'utf8')).runInNewContext({ self, URL });
    return { handlers, shown, opened };
}

test('push displays only fixed neutral text even if payload contains private or malicious content', async () => {
    const w = await worker(); let pending;
    assert.equal(typeof w.handlers.push, 'function');
    w.handlers.push({ data: { json: () => ({ title: 'private name', body: 'private homework', url: 'https://attacker.test/' }) }, waitUntil: p => pending = p });
    await pending;
    assert.equal(w.shown[0].title, 'Запара');
    assert.equal(w.shown[0].options.body, 'Откройте приложение, чтобы проверить расписание и задания.');
    assert.equal(w.shown[0].options.data.url, '/app/');
    assert.equal(JSON.stringify(w.shown).includes('private'), false);
});

test('notification click never opens payload URL or focuses a different-origin window', async () => {
    const w = await worker([{ url: 'https://attacker.test/app/', focus: () => { throw new Error('must not focus'); } }]); let pending;
    assert.equal(typeof w.handlers.notificationclick, 'function');
    w.handlers.notificationclick({ notification: { close() {}, data: { url: 'https://attacker.test/' } }, waitUntil: p => pending = p });
    await pending; assert.deepEqual(w.opened, ['/app/']);
});

test('notification click focuses an existing app window without opening another', async () => {
    let focused = 0;
    const w = await worker([{ url: 'https://example.test/app/settings', focus: async () => focused++ }]); let pending;
    w.handlers.notificationclick({ notification: { close() {} }, waitUntil: p => pending = p });
    await pending; assert.equal(focused, 1); assert.equal(w.opened.length, 0);
});
