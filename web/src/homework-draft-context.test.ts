import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { test } from "node:test";
import { runInNewContext } from "node:vm";
import ts from "typescript";
import { HomeworkDraftController } from "./homework-draft.ts";

const source = ts.transpileModule(await readFile(new URL("./homework-draft-context.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
}).outputText;

test("provider retains actual File references across route consumers; reload warning reads current dirty state", () => {
  const app: any = { homework: [{ id: "saved", done: false }], groupId: "g", session: { authenticated: false, user: null } };
  let context: any; const refs: any[] = []; let cursor = 0;
  let installed = false; const listeners = new Map<string, (event: any) => void>();
  const react = {
    createContext: () => { context = { Provider: "provider", current: null }; return context; },
    useContext: () => context.current,
    useRef: (initial: any) => { const index = cursor++; refs[index] ??= { current: initial }; return refs[index]; },
    useReducer: () => [0, () => {}],
    useEffect: (effect: () => void) => { if (!installed) { installed = true; effect(); } },
  };
  const jsx = (type: any, props: any) => { if (type === "provider") context.current = props.value; return { type, props }; };
  const modules: any = { react, "react/jsx-runtime": { jsx }, "./homework-draft": { HomeworkDraftController }, "./store": { useApp: () => app } };
  const runtime = { exports: {} as any, require: (id: string) => modules[id], window: { addEventListener: (type: string, fn: any) => listeners.set(type, fn), removeEventListener: (type: string) => listeners.delete(type) } };
  runInNewContext(source, runtime);
  const render = () => { cursor = 0; runtime.exports.HomeworkDraftProvider({ children: "route" }); };
  const warns = () => { let prevented = false; const event = { preventDefault: () => { prevented = true; }, returnValue: undefined }; listeners.get("beforeunload")!(event); return prevented; };
  render();
  const first = runtime.exports.useHomeworkDraft();
  first.controller.preload("Математика");
  assert.equal(warns(), false);
  const file = new File(["data"], "task.pdf");
  first.field("pending", [{ file, kind: "document" }]);
  assert.equal(warns(), true);
  render(); // Shell routes can unmount and remount their own consumer.
  const returned = runtime.exports.useHomeworkDraft();
  assert.equal(returned.controller, first.controller);
  assert.equal(returned.draft.pending[0].file, file);
  app.homework = [{ id: "saved", done: true }];
  render();
  assert.equal(first.readLocal("saved").done, true); // Even a prior, unmounted route consumer sees live completion.
  returned.field("pending", []);
  assert.equal(warns(), false);
  returned.field("text", "Задачи");
  const oldTicket = returned.controller.begin();
  app.session = { authenticated: true, user: { userId: "other" } }; render();
  assert.equal(runtime.exports.useHomeworkDraft().draft.text, "");
  assert.equal(warns(), false);
  first.controller.finish(oldTicket, "Старый результат", true);
  assert.equal(runtime.exports.useHomeworkDraft().note, "");
  runtime.exports.useHomeworkDraft().field("text", "Группа g");
  app.groupId = "other-group"; render();
  assert.equal(runtime.exports.useHomeworkDraft().draft.text, "");
});
