import assert from "node:assert/strict";
import { test } from "node:test";
import { startVisibleRefresh } from "./visible-refresh.ts";

function environment(initiallyHidden = true) {
  let hidden = initiallyHidden;
  let intervalCallback: (() => void) | null = null;
  let intervalActive = false;
  const documentListeners = new Map<string, Set<() => void>>();
  const windowListeners = new Map<string, Set<() => void>>();
  const target = (listeners: Map<string, Set<() => void>>) => ({
    addEventListener(type: string, listener: EventListenerOrEventListenerObject) {
      const handlers = listeners.get(type) ?? new Set<() => void>();
      handlers.add(listener as () => void);
      listeners.set(type, handlers);
    },
    removeEventListener(type: string, listener: EventListenerOrEventListenerObject) {
      listeners.get(type)?.delete(listener as () => void);
    },
  });
  const document = { ...target(documentListeners), get hidden() { return hidden; } };
  const window = {
    ...target(windowListeners),
    setInterval(callback: () => void, delay: number) {
      assert.equal(delay, 10_000);
      intervalCallback = callback;
      intervalActive = true;
      return 1;
    },
    clearInterval(id: number) {
      assert.equal(id, 1);
      intervalActive = false;
    },
  };
  return {
    document,
    window,
    get intervalActive() { return intervalActive; },
    count(type: string) { return (documentListeners.get(type)?.size ?? 0) + (windowListeners.get(type)?.size ?? 0); },
    tick() { if (intervalActive) intervalCallback?.(); },
    show() { hidden = false; for (const listener of documentListeners.get("visibilitychange") ?? []) listener(); },
    focus() { for (const listener of windowListeners.get("focus") ?? []) listener(); },
  };
}

const settle = () => new Promise(resolve => setImmediate(resolve));

test("inbox stays quiet while hidden and refreshes when the document becomes visible", async () => {
  const env = environment(true);
  let calls = 0;
  const stop = startVisibleRefresh(async () => { calls += 1; }, env.document as unknown as Document, env.window as unknown as Window);
  env.tick();
  await settle();
  assert.equal(calls, 0);
  env.show();
  await settle();
  assert.equal(calls, 1);
  stop();
});

test("overlapping inbox refresh triggers coalesce into one queued refresh", async () => {
  const env = environment(false);
  const releases: (() => void)[] = [];
  let calls = 0;
  const stop = startVisibleRefresh(() => {
    calls += 1;
    return new Promise<void>(resolve => releases.push(resolve));
  }, env.document as unknown as Document, env.window as unknown as Window);
  assert.equal(calls, 1);
  env.tick();
  env.focus();
  assert.equal(calls, 1);
  releases.shift()!();
  await settle();
  assert.equal(calls, 2);
  stop();
  releases.shift()!();
  await settle();
  assert.equal(calls, 2);
});

test("stopping inbox refresh removes listeners and prevents queued work", async () => {
  const env = environment(false);
  let release!: () => void;
  let calls = 0;
  const stop = startVisibleRefresh(() => {
    calls += 1;
    return new Promise<void>(resolve => { release = resolve; });
  }, env.document as unknown as Document, env.window as unknown as Window);
  env.tick();
  stop();
  release();
  await settle();
  env.show();
  env.focus();
  env.tick();
  await settle();
  assert.equal(calls, 1);
  assert.equal(env.count("visibilitychange"), 0);
  assert.equal(env.count("focus"), 0);
  assert.equal(env.count("online"), 0);
  assert.equal(env.intervalActive, false);
});
