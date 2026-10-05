import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { test } from "node:test";
import { runInNewContext } from "node:vm";
import ts from "typescript";
import { PersonalComposerStore } from "./personal-composer.ts";

test("provider retains route drafts and send locks, remounts descendants and resets owner before they read", async () => {
  const source = ts.transpileModule(await readFile(new URL("./personal-composer-context.tsx", import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
  }).outputText;
  const app: any = { session: { authenticated: true, user: { userId: "a" } } };
  let context: any; const refs: any[] = []; let cursor = 0;
  const react = {
    createContext: () => { context = { Provider: "provider", current: null }; return context; },
    useContext: () => context.current,
    useRef: (initial: any) => { const index = cursor++; refs[index] ??= { current: initial }; return refs[index]; },
    useReducer: () => [0, () => {}],
  };
  const modules: any = { react, "react/jsx-runtime": { jsx: (type: any, props: any, key: string) => {
    if (type === "provider") context.current = props.value; return { type, props, key };
  } }, "./personal-composer": { PersonalComposerStore }, "./store": { useApp: () => app } };
  const runtime = { exports: {} as any, require: (id: string) => modules[id] };
  runInNewContext(source, runtime);
  const render = () => { cursor = 0; return runtime.exports.PersonalComposerProvider({ children: "route" }); };
  const firstKey = render().key;
  const first = runtime.exports.usePersonalComposer("one");
  first.store.text("one", "Текст"); const ticket = first.store.begin("one");
  render(); const returned = runtime.exports.usePersonalComposer("one");
  assert.equal(returned.store, first.store); assert.equal(returned.composer.text, "Текст");
  assert.equal(returned.store.begin("one"), null);
  first.store.finish(ticket, "", true); assert.equal(returned.store.read("one").text, "");
  app.session.user.userId = "b"; assert.notEqual(render().key, firstKey);
  assert.equal(runtime.exports.usePersonalComposer("one").composer.text, "");
  app.session.authenticated = false; assert.equal(render().key, "guest");
});
