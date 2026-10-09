import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { runInNewContext } from "node:vm";
import ts from "typescript";

const require = createRequire(import.meta.url);
const React = require("react");
const { renderToStaticMarkup } = require("react-dom/server");

async function component(joined: string[]) {
  const source = await readFile(new URL("./group-no-membership.tsx", import.meta.url), "utf8");
  const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText;
  const modules: Record<string, unknown> = {
    "react-router-dom": { Link: ({ to, children, ...rest }: any) => React.createElement("a", { href: to, ...rest }, children) },
    "./api": { joinCommunity: async (id: string) => { joined.push(id); } },
    "./icons": { Icon: () => null },
  };
  const context: any = { exports: {}, require: (name: string) => modules[name] ?? require(name) };
  runInNewContext(code, context);
  return context.exports.GroupNoMembership;
}

test("no group selected: one empty state with «Выбрать группу» (#36)", async () => {
  const View = await component([]);
  const html = renderToStaticMarkup(React.createElement(View, { missing: "group", candidate: null, onRecheck() {} }));
  assert.match(html, /Группа не выбрана/);
  assert.match(html, /href="\/settings\?section=study"[^>]*>Выбрать группу/);
  assert.doesNotMatch(html, /Повторить загрузку/);
});

test("not a member: «Вы ещё не в группе» once, with the join request as the main action (#36)", async () => {
  const View = await component([]);
  const html = renderToStaticMarkup(React.createElement(View, { missing: "membership", candidate: { communityId: "c1", name: "Группа А131С" }, onRecheck() {} }));
  assert.equal(html.match(/Вы ещё не в группе/g)?.length, 1);
  assert.match(html, /class="btn primary"[^>]*>Отправить заявку на вступление/);
  assert.match(html, /«Группа А131С»/);
  assert.doesNotMatch(html, /Повторить загрузку/);
});

test("not a member and the group has no community: explain and link to «Сообщество» (#36)", async () => {
  const View = await component([]);
  const html = renderToStaticMarkup(React.createElement(View, { missing: "membership", candidate: null, onRecheck() {} }));
  assert.match(html, /пока нет сообщества/);
  assert.match(html, /href="\/community"/);
  assert.doesNotMatch(html, /Отправить заявку/);
});

test("group page shows the empty state instead of an error for missing membership and does not repeat errors in the header (#36)", async () => {
  const pages = await readFile(new URL("./pages.tsx", import.meta.url), "utf8");
  assert.match(pages, /\{!home && missing\?\.missing && <GroupNoMembership /);
  assert.doesNotMatch(pages, /: error \|\| "Одногруппники и чат"/);
});
