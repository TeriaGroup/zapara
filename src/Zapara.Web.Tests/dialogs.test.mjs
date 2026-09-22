import test from 'node:test';
import assert from 'node:assert/strict';

let sequence = 0;
function environment() {
    globalThis.document = { body: { style: { overflow: 'auto' } }, activeElement: null };
    const focusable = () => ({ isConnected: true, getClientRects: () => [1], focus() { document.activeElement = this; } });
    const original = focusable(); original.focus();
    const element = () => {
        const control = focusable(), listeners = new Map();
        return { isConnected: true, control, listeners,
            querySelector: () => control, querySelectorAll: () => [control],
            contains: item => item === control,
            addEventListener: (name, callback) => listeners.set(name, callback),
            removeEventListener: name => listeners.delete(name),
            focus() { document.activeElement = this; }
        };
    };
    return { original, outer: element(), inner: element() };
}

test('nested dialogs restore the original page overflow when outer unmounts before inner', async () => {
    const env = environment();
    const dialogs = await import('../Zapara.Web/wwwroot/js/dialogs.js?test=' + ++sequence);
    dialogs.open(env.outer); dialogs.open(env.inner);
    dialogs.close(env.outer);
    assert.equal(document.body.style.overflow, 'hidden');
    dialogs.close(env.inner);
    assert.equal(document.body.style.overflow, 'auto');
    assert.equal(document.activeElement, env.original);
});

test('closing a lower dialog never steals focus from the remaining top dialog', async () => {
    const env = environment();
    const dialogs = await import('../Zapara.Web/wwwroot/js/dialogs.js?test=' + ++sequence);
    dialogs.open(env.outer); dialogs.open(env.inner);
    dialogs.close(env.outer);
    assert.equal(document.activeElement, env.inner.control);
    dialogs.close(env.inner);
});

test('normal nested close restores the parent focus then the page focus', async () => {
    const env = environment();
    const dialogs = await import('../Zapara.Web/wwwroot/js/dialogs.js?test=' + ++sequence);
    dialogs.open(env.outer); dialogs.open(env.inner);
    dialogs.close(env.inner);
    assert.equal(document.activeElement, env.outer.control);
    assert.equal(document.body.style.overflow, 'hidden');
    dialogs.close(env.outer);
    assert.equal(document.activeElement, env.original);
    assert.equal(document.body.style.overflow, 'auto');
});
