import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { runInNewContext } from 'node:vm';

const source = await readFile(new URL('../Zapara.Web/wwwroot/service-worker.published.js', import.meta.url), 'utf8');
function worker(urls) {
    const events = {}, stored = new Map(), added = [];
    const origin = 'https://zapara.test';
    const cache = {
        async addAll(requests) {
            for (const request of requests) { added.push(request.url); stored.set(request.url, new Response('cached:' + request.url)); }
        },
        async match(request) { return stored.get(typeof request === 'string' ? request : request.url)?.clone(); }
    };
    const self = { location: new URL(origin + '/app/service-worker.js'), assetsManifest: { version: 'v1', assets: urls.map(url => ({url, hash:''})) },
        importScripts() {}, addEventListener(type, handler) { events[type] = handler; }, clients: { async claim() {} }, async skipWaiting() {} };
    runInNewContext(source, { self, URL, Request, caches: { async open() { return cache; }, async keys() { return []; } },
        fetch() { throw new Error('Network is offline'); } });
    return { added, async install() { let completed; events.install({waitUntil(value) { completed = value; }}); await completed; },
        async offline(url, mode = 'cors') { let response; events.fetch({request: {url: origin + url, method:'GET', mode}, respondWith(value) { response = value; }}); return response ? (await response).text() : null; } };
}

test('published host-prefixed manifest caches app assets at their actual routes', async () => {
    const app = worker(['app/index.html','app/_framework/blazor.webassembly.js','app/data/TimetableGroup50.xml']);
    await app.install();
    assert.deepEqual(app.added, ['https://zapara.test/app/index.html','https://zapara.test/app/_framework/blazor.webassembly.js','https://zapara.test/app/data/TimetableGroup50.xml']);
    assert.equal(await app.offline('/app/_framework/blazor.webassembly.js'), 'cached:https://zapara.test/app/_framework/blazor.webassembly.js');
    assert.equal(await app.offline('/app/homework', 'navigate'), 'cached:https://zapara.test/app/index.html');
});

test('manifest cannot cache private or cross-origin URLs outside the app scope', async () => {
    const app = worker(['app/index.html','../web-api/session.json','https://foreign.test/app/index.html']);
    await app.install();
    assert.deepEqual(app.added, ['https://zapara.test/app/index.html']);
    assert.equal(await app.offline('/web-api/session.json'), null);
});
