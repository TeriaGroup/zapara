import test from 'node:test';
import assert from 'node:assert/strict';

let sequence = 0;
function database(profile = null, cache = 'last-good') {
    const data = { profiles: new Map(profile ? [['guest', JSON.stringify(profile)]] : []), public: new Map([['schedule', cache]]) };
    const fixture = { data, writes: 0, transactions: [], quota: false };
    const db = { transaction(names, mode) {
        fixture.transactions.push({ names, mode });
        const pending = [], tx = { aborted: false,
            abort() { this.aborted = true; queueMicrotask(() => this.onabort?.()); },
            objectStore(name) { return {
                get(key) {
                    const request = {};
                    queueMicrotask(() => {
                        request.result = data[name].get(key);
                        request.onsuccess?.();
                        queueMicrotask(() => { if (!tx.aborted) { for (const [store, id, value] of pending) data[store].set(id, value); tx.oncomplete?.(); } });
                    });
                    return request;
                },
                put(value, key) {
                    fixture.writes++;
                    if (fixture.quota && name === 'public') throw new Error('QuotaExceededError');
                    pending.push([name, key, value]);
                }
            }; }
        };
        return tx;
    }, close() {} };
    globalThis.indexedDB = { open() { const request = {}; queueMicrotask(() => { request.result = db; request.onsuccess(); }); return request; } };
    return fixture;
}
async function module() { return import('../Zapara.Web/wwwroot/js/storage.js?cas=' + ++sequence); }
const candidate = revision => JSON.stringify({ owner: 'guest', storageRevision: revision, records: {}, outbox: [] });

test('profile and public cache are committed by the same readwrite transaction only at expected revision', async () => {
    const db = database({ owner: 'guest', storageRevision: 2 }); const storage = await module();
    assert.equal(await storage.compareExchangeProfileSnapshot('guest', 2, candidate(3), '{"title":"fresh"}'), true);
    assert.equal(JSON.parse(db.data.profiles.get('guest')).storageRevision, 3);
    assert.equal(db.data.public.get('schedule'), '{"title":"fresh"}');
    assert.equal(db.data.public.get('schedule:guest'), '{"title":"fresh"}');
    assert.deepEqual(db.transactions, [{ names: ['profiles', 'public'], mode: 'readwrite' }]);
    assert.equal(await storage.compareExchangeProfileSnapshot('guest', 2, candidate(3), '{"title":"stale"}'), false);
    assert.equal(db.data.public.get('schedule'), '{"title":"fresh"}');
});

test('old profiles without a storage revision migrate without wiping their contents', async () => {
    const db = database({ owner: 'guest', records: { note: 'keep' }, outbox: [] }); const storage = await module();
    const next = JSON.stringify({ owner: 'guest', storageRevision: 1, records: { note: 'keep', extra: 'added' }, outbox: [] });
    assert.equal(await storage.compareExchangeProfile('guest', 0, next), true);
    assert.deepEqual(JSON.parse(db.data.profiles.get('guest')).records, { note: 'keep', extra: 'added' });
});

test('a quota failure in the second store aborts both profile and cache writes', async () => {
    const db = database({ owner: 'guest', storageRevision: 4 }); db.quota = true; const storage = await module();
    await assert.rejects(() => storage.compareExchangeProfileSnapshot('guest', 4, candidate(5), '{}'));
    assert.equal(JSON.parse(db.data.profiles.get('guest')).storageRevision, 4);
    assert.equal(db.data.public.get('schedule'), 'last-good');
});

test('mismatched stored owner and invalid candidate revision are rejected without any put', async () => {
    const db = database({ owner: 'someone-else', storageRevision: 0 }); const storage = await module();
    await assert.rejects(() => storage.compareExchangeProfile('guest', 0, candidate(1)));
    await assert.rejects(() => storage.compareExchangeProfile('guest', 0, candidate(2)));
    await assert.rejects(() => storage.compareExchangeProfile('guest', Number.MAX_SAFE_INTEGER, candidate(Number.MAX_SAFE_INTEGER)));
    assert.equal(db.writes, 0);
});
