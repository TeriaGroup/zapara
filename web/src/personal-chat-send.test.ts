import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { test } from "node:test";
import { runInNewContext } from "node:vm";
import ts from "typescript";
import * as composerHelpers from "./personal-composer.ts";
import * as social from "./socialChat.ts";

const source = ts.transpileModule(await readFile(new URL("./people.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
}).outputText + "\nexports.TestChat = Chat;";
const flush = () => new Promise(resolve => setImmediate(resolve));
const friend = { conversationId: "c", displayName: "Друг", username: "friend" };
const sentMessage = { messageId: "sent", body: "Текст", createdAt: "2026-09-28T00:00:00Z", reactions: [] };

function deferred() {
  let resolve!: (value: any) => void; let reject!: (error: Error) => void;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
function mount(store: composerHelpers.PersonalComposerStore, send: (...args: any[]) => Promise<any>, load = async () => ({ messages: [], hasMore: false })) {
  const hooks: any[] = []; let cursor = 0; const pendingEffects: (() => any)[] = []; const cleanups: (() => void)[] = [];
  const intervals: (() => any)[] = [];
  const react = {
    useState: (initial: any) => { const index = cursor++; if (!(index in hooks)) hooks[index] = typeof initial === "function" ? initial() : initial;
      return [hooks[index], (value: any) => { hooks[index] = typeof value === "function" ? value(hooks[index]) : value; }]; },
    useRef: (initial: any) => { const index = cursor++; hooks[index] ??= { current: initial }; return hooks[index]; },
    useEffect: (effect: () => any, deps: any[]) => { const index = cursor++; const old = hooks[index];
      if (!old || deps.some((item, offset) => item !== old[offset])) { hooks[index] = deps; pendingEffects.push(effect); } },
  };
  const jsx = (type: any, props: any) => ({ type, props });
  const modules: any = {
    react, "react/jsx-runtime": { jsx, jsxs: jsx }, "./api": { socialText: send, socialEdit: send, socialMessages: load },
    "./personal-composer": composerHelpers, "./personal-composer-context": {
      usePersonalComposer: (id: string) => ({ store, refresh: () => {}, composer: store.read(id) }),
    }, "./socialChat": social,
  };
  const runtime = { exports: {} as any, require: (id: string) => modules[id] ?? {},
    window: { matchMedia: () => ({ matches: true }), setInterval: (fn: any) => { intervals.push(fn); return intervals.length; }, clearInterval() {}, clearTimeout() {} },
  };
  runInNewContext(source, runtime);
  const render = () => {
    cursor = 0; const tree = runtime.exports.TestChat({ friend, self: "me", onError: () => {} });
    while (pendingEffects.length) { const cleanup = pendingEffects.shift()!(); if (cleanup) cleanups.push(cleanup); }
    return tree;
  };
  return { render, unmount: () => cleanups.forEach(fn => fn()), poll: () => intervals[0]() };
}
function nodes(tree: any): any[] {
  if (!tree || typeof tree !== "object") return [];
  if (Array.isArray(tree)) return tree.flatMap(nodes);
  return [tree, ...nodes(tree.props?.children)];
}
function submit(instance: ReturnType<typeof mount>) {
  nodes(instance.render()).find(node => node.type === "form").props.onSubmit({ preventDefault() {} });
}

test("real submit handler completes retained draft after route unmount; remount blocks duplicate send", async () => {
  const store = new composerHelpers.PersonalComposerStore("a"); store.text("c", "  Текст  ");
  const response = deferred(); let calls = 0;
  const send = (...args: any[]) => { calls++; assert.equal(args[1], "Текст"); return response.promise; };
  const first = mount(store, send); first.render(); await flush(); submit(first);
  assert.equal(store.read("c").busy, true); first.unmount();
  const returned = mount(store, send); returned.render(); await flush(); submit(returned);
  assert.equal(calls, 1); response.resolve(sentMessage); await flush();
  assert.equal(store.read("c").busy, false); assert.equal(store.read("c").text, ""); returned.unmount();
});

test("real submit handler preserves newer typing and persistent send failure through polling", async () => {
  const store = new composerHelpers.PersonalComposerStore("a"); store.text("c", "Текст");
  const response = deferred(); const instance = mount(store, () => response.promise);
  instance.render(); await flush(); submit(instance);
  nodes(instance.render()).find(node => node.type === "textarea").props.onChange({ target: { value: "Новый текст" } });
  response.reject(new Error("unknown result")); await flush();
  assert.equal(store.read("c").text, "Новый текст"); assert.ok(store.read("c").error);
  await instance.poll(); await flush();
  const tree = nodes(instance.render());
  assert.ok(tree.some(node => node.props?.className === "banner composer-error"));
  assert.equal(store.read("c").busy, false); instance.unmount();
});

test("failed initial history has retry feedback and never presents successful empty history", async () => {
  const store = new composerHelpers.PersonalComposerStore("a");
  const instance = mount(store, async () => sentMessage, async () => { throw new Error("offline"); });
  instance.render(); await flush();
  const tree = nodes(instance.render());
  assert.ok(tree.some(node => node.props?.className === "banner"));
  assert.ok(!tree.some(node => node.props?.children === "Напишите сообщение, отправьте стикер или кружок."));
  instance.unmount();
});
