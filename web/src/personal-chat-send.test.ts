import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { test } from "node:test";
import { runInNewContext } from "node:vm";
import ts from "typescript";
import * as composerHelpers from "./personal-composer.ts";
import * as social from "./socialChat.ts";
import * as personalHistory from "./personal-history.ts";
import * as hold from "./hold.ts";

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
  const hooks: any[] = []; let cursor = 0, changed = false;
  const effects = new Map<number, { deps?: any[]; cleanup?: () => void }>();
  const pendingEffects = new Map<number, () => any>();
  const intervals: (() => any)[] = [];
  const react = {
    useState: (initial: any) => { const index = cursor++; if (!(index in hooks)) hooks[index] = typeof initial === "function" ? initial() : initial;
      return [hooks[index], (value: any) => { const next = typeof value === "function" ? value(hooks[index]) : value;
        if (!Object.is(next, hooks[index])) { hooks[index] = next; changed = true; } }]; },
    useRef: (initial: any) => { const index = cursor++; hooks[index] ??= { current: initial }; return hooks[index]; },
    useEffect: (effect: () => any, deps?: any[]) => { const index = cursor++; const old = effects.get(index);
      if (!old || !deps || !old.deps || deps.length !== old.deps.length || deps.some((item, offset) => !Object.is(item, old.deps![offset]))) {
        effects.set(index, { deps: deps?.slice(), cleanup: old?.cleanup }); pendingEffects.set(index, effect);
      } },
  };
  const jsx = (type: any, props: any) => ({ type, props });
  const modules: any = {
    react, "react/jsx-runtime": { jsx, jsxs: jsx }, "./api": { socialText: send, socialEdit: send, socialMessages: load },
    "./personal-composer": composerHelpers, "./personal-composer-context": {
      usePersonalComposer: (id: string) => ({ store, refresh: () => {}, composer: store.read(id) }),
    }, "./socialChat": social, "./personal-history": personalHistory, "./hold": hold,
  };
  const runtime = { exports: {} as any, require: (id: string) => modules[id] ?? {},
    window: { matchMedia: () => ({ matches: true }), setInterval: (fn: any) => { intervals.push(fn); return intervals.length; }, clearInterval() {}, clearTimeout() {} },
  };
  runInNewContext(source, runtime);
  const onError = () => {};
  const render = () => {
    for (let pass = 0; pass < 25; pass++) {
      cursor = 0; changed = false;
      const tree = runtime.exports.TestChat({ friend, self: "me", onError });
      const scheduled = [...pendingEffects]; pendingEffects.clear();
      for (const [index, effect] of scheduled) {
        const record = effects.get(index)!; record.cleanup?.();
        const cleanup = effect(); record.cleanup = typeof cleanup === "function" ? cleanup : undefined;
      }
      if (!changed) return tree;
    }
    throw new Error("Chat effects did not settle");
  };
  return { render, unmount: () => { effects.forEach(effect => effect.cleanup?.()); effects.clear(); pendingEffects.clear(); }, poll: () => intervals[0]() };
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

test("real personal-history search filters loaded messages and leaves the retained composer untouched", async () => {
  const store = new composerHelpers.PersonalComposerStore("a");store.text("c","Мой черновик");
  const messages = ["Физика","Математика"].map((body,index)=>({
    messageId:`message-${index}`,senderId:"peer",senderName:"Друг",kind:"text",body,
    attachmentId:null,fileName:null,contentType:null,bytes:null,createdAt:"2026-09-28T10:00:00Z",
    replyTo:null,replyBody:null,editedAt:null,deleted:false,read:false,durationMs:null,reactions:[],
  }));
  const instance=mount(store,async()=>{throw new Error("Search must not send a message");},async()=>({messages,hasMore:false}));
  instance.render();await flush();let tree=nodes(instance.render());
  assert.equal(tree.filter(node=>node.props?.["data-hold"]==="text").length,2);
  assert.equal(tree.some(node=>node.type==="input"&&node.props.type==="search"),false);
  tree.find(node=>node.type==="button"&&node.props.className==="btn personal-search-toggle").props.onClick();
  tree=nodes(instance.render());
  tree.find(node=>node.type==="input"&&node.props.type==="search").props.onChange({target:{value:"ФИЗИКА"}});
  tree=nodes(instance.render());
  assert.equal(tree.filter(node=>node.props?.["data-hold"]==="text").length,1);
  assert.ok(tree.some(node=>node.props?.className==="text"&&node.props.children==="Физика"));
  assert.equal(store.read("c").text,"Мой черновик");
  tree.find(node=>node.type==="button"&&node.props.children==="Сбросить").props.onClick();
  assert.equal(nodes(instance.render()).filter(node=>node.props?.["data-hold"]==="text").length,2);
  tree=nodes(instance.render());
  tree.find(node=>node.type==="button"&&node.props.className==="btn personal-search-toggle").props.onClick();
  assert.equal(nodes(instance.render()).some(node=>node.type==="input"&&node.props.type==="search"),false);
  assert.equal(store.read("c").text,"Мой черновик");
  assert.equal(messages.length,2);instance.unmount();
});
