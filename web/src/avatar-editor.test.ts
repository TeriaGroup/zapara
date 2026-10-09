import assert from "node:assert/strict";
import { test } from "node:test";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";
import ts from "typescript";

const source = ts.transpileModule(await readFile(new URL("./avatar-view.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
}).outputText;

test("avatar editor does not upload after account change during photo preparation", async () => {
  const slots: any[] = [];
  let cursor = 0;
  let session = { authenticated: true, user: { userId: "first" }, familyId: "family-first" };
  let finishPreparation!: (file: File) => void;
  let prepared = new Promise<File>(resolve => { finishPreparation = resolve; });
  let uploads = 0;
  const react = {
    useState(initial: any) {
      const index = cursor++;
      if (!(index in slots)) slots[index] = typeof initial === "function" ? initial() : initial;
      return [slots[index], (next: any) => { slots[index] = typeof next === "function" ? next(slots[index]) : next; }];
    },
    useRef(initial: any) {
      const index = cursor++;
      if (!(index in slots)) slots[index] = { current: initial };
      return slots[index];
    },
    useEffect(effect: () => (() => void) | void) {
      const index = cursor++;
      slots[index]?.cleanup?.();
      slots[index] = { cleanup: effect() };
    },
  };
  const jsx = (type: any, props: any) => ({ type, props });
  const modules: Record<string, any> = {
    react,
    "react/jsx-runtime": { jsx, jsxs: jsx },
    "./api": { saveAvatar: async () => { uploads++; return { revision: "new" }; }, deleteAvatar: async () => {} },
    "./avatar": { createAvatarCache: () => ({ invalidate() {} }), validateAvatarFile: () => null, avatarFallback: () => "initials", avatarInitials: () => "" },
    "./icons": { Icon: () => null },
    "./avatar-photo": { prepareAvatarFile: () => prepared },
    "./store": { useApp: () => ({ session }) },
  };
  const context = { exports: {} as any, require: (id: string) => modules[id] };
  runInNewContext(source, context);
  const render = () => { cursor = 0; return context.exports.AvatarEditor({ kind: "user", id: session.user.userId, name: "Имя" }); };
  const nodes = (tree: any): any[] => Array.isArray(tree) ? tree.flatMap(nodes) : tree && typeof tree === "object" ? [tree, ...nodes(tree.props?.children)] : [];
  const first = render();
  const input = nodes(first).find(node => node.type === "input" && node.props.type === "file");
  assert.ok(input);
  input.props.onChange({ target: { files: [new File(["original"], "photo.png", { type: "image/png" })] } });
  session = { authenticated: true, user: { userId: "second" }, familyId: "family-second" };
  const second = render();
  finishPreparation(new File(["converted"], "avatar.webp", { type: "image/webp" }));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(uploads, 0);
  prepared = new Promise<File>(resolve => { finishPreparation = resolve; });
  const nextInput = nodes(second).find(node => node.type === "input" && node.props.type === "file");
  nextInput.props.onChange({ target: { files: [new File(["next"], "photo.png", { type: "image/png" })] } });
  for (const slot of slots) slot?.cleanup?.();
  finishPreparation(new File(["converted"], "avatar.webp", { type: "image/webp" }));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(uploads, 0);
});
