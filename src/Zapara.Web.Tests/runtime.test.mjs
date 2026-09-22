import test from 'node:test';
import assert from 'node:assert/strict';
import * as runtime from '../Zapara.Web/wwwroot/js/runtime.js';

function environment() {
    globalThis.window = new EventTarget();
    globalThis.document = new EventTarget();
    document.visibilityState = 'visible';
    const calls = [];
    return { calls, reference: { invokeMethodAsync: async (...args) => { calls.push(args); } } };
}
test('runtime wakes on reconnect, focus and returning visible, but not hidden focus', async () => {
    const env = environment(); runtime.start(env.reference);
    window.dispatchEvent(new Event('online'));
    window.dispatchEvent(new Event('focus'));
    document.visibilityState = 'hidden'; window.dispatchEvent(new Event('focus')); document.dispatchEvent(new Event('visibilitychange'));
    document.visibilityState = 'visible'; document.dispatchEvent(new Event('visibilitychange'));
    assert.deepEqual(env.calls, [['BrowserRuntimeWake', 'online'], ['BrowserRuntimeWake', 'focus'], ['BrowserRuntimeWake', 'visible']]);
    runtime.stop();
});
test('restarting replaces event observers and stop removes every observer', () => {
    const first = environment(); runtime.start(first.reference);
    const nextCalls = [];
    runtime.start({ invokeMethodAsync: async (...args) => nextCalls.push(args) });
    window.dispatchEvent(new Event('online'));
    assert.equal(first.calls.length, 0); assert.equal(nextCalls.length, 1);
    runtime.stop(); runtime.stop();
    window.dispatchEvent(new Event('online')); window.dispatchEvent(new Event('focus')); document.dispatchEvent(new Event('visibilitychange'));
    assert.equal(nextCalls.length, 1);
});
test('disposed .NET callbacks are observed rather than becoming unhandled rejections', async () => {
    environment(); runtime.start({ invokeMethodAsync: () => Promise.reject(new Error('disposed')) });
    window.dispatchEvent(new Event('online')); await Promise.resolve(); runtime.stop();
});
