import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import * as accounts from '../Zapara.Web/wwwroot/js/accounts.js';

const id = '11111111-1111-4111-8111-111111111111';
test('account HTML username pattern is valid under browser Unicode-set semantics', async () => {
    const markup = await readFile(new URL('../Zapara.Web/Components/AccountPanel.razor', import.meta.url), 'utf8');
    const source = markup.match(/pattern="([^"]+)"/)[1];
    const pattern = new RegExp(`^(?:${source})$`, 'v');
    for (const name of ['abc', 'a-b', 'a_b.c', 'A123']) assert.equal(pattern.test(name), true);
    for (const name of ['ab', 'русский', 'a@b', 'a b']) assert.equal(pattern.test(name), false);
});
function storage() {
    const values = new Map();
    globalThis.sessionStorage = { setItem: (key, value) => values.set(key, value), getItem: key => values.get(key), removeItem: key => values.delete(key) };
    return values;
}
test('OAuth routing persistence drops all credentials and clears malformed routing', () => {
    const values = storage();
    accounts.savePending({ transactionId: id, action: 'export', familyId: id, expiresAt: '2030-09-21T10:00:00Z', password: 'SECRET', proofToken: 'SECRET', authorizeUrl: 'SECRET' });
    assert.equal([...values.values()][0].includes('SECRET'), false);
    assert.deepEqual(Object.keys(accounts.readPending()).sort(), ['action', 'expiresAt', 'familyId', 'transactionId']);
    values.set('zapara.account.oauth', '{"action":"arbitrary"}');
    assert.equal(accounts.readPending(), null); assert.equal(values.size, 0);
});
test('provider navigation permits exact configured provider authorize endpoints only', () => {
    const navigated = [];
    globalThis.window = { location: { assign: url => navigated.push(url) } };
    for (const target of ['http://id.vk.ru/authorize', 'https://id.vk.ru.evil.invalid/authorize', 'https://user@id.vk.ru/authorize', 'https://id.vk.ru/other'])
        assert.throws(() => accounts.navigateProvider('vk', target));
    accounts.navigateProvider('vk', 'https://id.vk.ru/authorize?state=public-routing');
    assert.deepEqual(navigated, ['https://id.vk.ru/authorize?state=public-routing']);
});
test('export helper downloads real JSON bytes with a fixed safe name and releases its object URL', () => {
    const oldCreate = URL.createObjectURL, oldRevoke = URL.revokeObjectURL, oldTimeout = globalThis.setTimeout;
    let blob, clicked = false, removed = false, revoked = false, link;
    URL.createObjectURL = value => { blob = value; return 'blob:owned'; };
    URL.revokeObjectURL = value => { assert.equal(value, 'blob:owned'); revoked = true; };
    globalThis.setTimeout = callback => callback();
    globalThis.document = { createElement: () => link = { click: () => { clicked = true; }, remove: () => { removed = true; } }, body: { appendChild() {} } };
    try {
        const bytes = new TextEncoder().encode('{"profile":{}}');
        assert.throws(() => accounts.download(bytes, '../private.json'));
        accounts.download(bytes, `zapara-export-${id}.json`);
        assert.equal(blob.size, bytes.length); assert.equal(blob.type, 'application/json;charset=utf-8');
        assert.equal(link.download, `zapara-export-${id}.json`);
        assert.ok(clicked && removed && revoked);
    } finally { URL.createObjectURL = oldCreate; URL.revokeObjectURL = oldRevoke; globalThis.setTimeout = oldTimeout; }
});
