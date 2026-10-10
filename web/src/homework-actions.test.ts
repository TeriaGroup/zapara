import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { homeworkSummary, plural, taskCount } from "./homework-counts.ts";

test("counters say what they count: «4 предмета · 5 заданий · открыто 5 · выполнено 0» (#15, W-11)", () => {
  assert.equal(taskCount(1), "1 задание");
  assert.equal(taskCount(3), "3 задания");
  assert.equal(taskCount(11), "11 заданий");
  assert.equal(taskCount(21), "21 задание");
  assert.equal(plural(12, ["предмет", "предмета", "предметов"]), "12 предметов");
  assert.equal(homeworkSummary([
    { subject: "Физика", done: false }, { subject: "физика ", done: true }, { subject: "ТАУ", done: false },
  ]), "2 предмета · 3 задания · открыто 2 · выполнено 1");
});

test("G-3: the open/done counter is the catalog key homeworkOpenDone, shared with the desktop counter (hwOpenDone)", async () => {
  const catalog = JSON.parse(await readFile(new URL("../../design/strings/ru.json", import.meta.url), "utf8"));
  assert.equal(catalog.strings.homeworkOpenDone.ru, "открыто {0} · выполнено {1}");
  assert.deepEqual(catalog.strings.homeworkOpenDone.desktop, ["hwOpenDone"]);
  const source = await readFile(new URL("./homework-counts.ts", import.meta.url), "utf8");
  assert.match(source, /t\("homeworkOpenDone", rows\.length - done, done\)/);
  assert.doesNotMatch(source, /`[^`]*открыто \$\{/);
});

const pages = await readFile(new URL("./pages.tsx", import.meta.url), "utf8");
const page = pages.slice(pages.indexOf('<section className="page homework-page">'), pages.indexOf("function communityRoleLabel("));

test("task card shows checkbox, subject, text and deadline; other actions live in «⋯» and none is lost (#15)", () => {
  const personal = page.slice(page.indexOf('<label className="homework-check"><input type="checkbox" checked={item.done}'), page.indexOf("<HomeworkAttachments"));
  assert.ok(personal.length > 0);
  assert.ok(personal.indexOf("{item.text}") < personal.indexOf("<HomeworkActions"), "text before actions");
  for (const label of ["Изменить", "Создать похожее", "К ближайшему занятию", "Обсудить в чате группы", "Отправить другу"]) assert.ok(personal.includes(label), label);
  assert.match(personal, /onDelete=\{\(\) => \{ try \{ app\.privateHomework\.remove\(item\.id\)/);
  assert.doesNotMatch(page, /className="btn quiet" type="button" onClick=\{\(\)=>\{if\(!window\.confirm\(`Удалить/);
  assert.doesNotMatch(page, /Обсудить задание</);
});

test("delete is a danger action, last in the list, behind a confirmation (#15)", async () => {
  const source = await readFile(new URL("./homework-actions.tsx", import.meta.url), "utf8");
  const list = source.slice(source.indexOf("homework-action-list"), source.indexOf('role="alertdialog"'));
  assert.ok(list.indexOf("actions.map(") < list.indexOf("homework-delete"), "delete after other actions");
  assert.match(source, /className="btn quiet danger homework-delete" type="button" onClick=\{\(\) => setConfirm\(true\)\}/);
  assert.match(source, /className="btn primary danger-solid" type="button" onClick=\{\(\) => \{ close\(\); onDelete\?\.\(\); \}\}/);
  const css = await readFile(new URL("./styles.css", import.meta.url), "utf8");
  // #8 (G-1): цвет опасного действия и текст на нём — токены danger / on-danger (свои в каждой теме), без hex в стилях.
  assert.match(css, /:root \{ --danger-ink: var\(--bad\); \}/);
  assert.match(css, /\.btn\.danger-solid \{ background: var\(--danger-ink\); border-color: var\(--danger-ink\); color: var\(--zp-on-danger\); \}/);
  assert.doesNotMatch(css, /--danger-ink: #|danger-solid \{[^}]*color: #/);
});

test("bulk actions hidden until «Выбрать несколько»; empty state has primary «Добавить задание»; privacy note in the form (#15)", () => {
  const bulk = page.slice(page.indexOf("{selecting && visibleHomework.length > 0"), page.indexOf("</div>}", page.indexOf("{selecting && visibleHomework.length > 0")));
  for (const tool of ["PersonalHomeworkBatch", "HomeworkPostpone", "HomeworkPublication", "HomeworkExportTools"]) assert.ok(bulk.includes(tool), tool);
  assert.equal(page.split("<PersonalHomeworkBatch").length, 2);
  assert.match(page, /<button className="btn primary" type="button" onClick=\{\(\) => setEditorOpen\(true\)\}><Icon name="plus" size=\{18\} \/>Добавить задание<\/button>/);
  assert.doesNotMatch(page, /Добавить первое/);
  assert.match(page, /<Head title="Домашка" mobileActions>/);
  const form = page.slice(page.indexOf('<form id="homework-editor"'), page.indexOf("</form>"));
  assert.match(form, /Личное задание хранится на устройстве/);
});

test("#9 (G-2): tapping the task row opens the same sheet as «⋯», like a lesson card; the checkbox stays its own", async () => {
  const { createRequire } = await import("node:module");
  const { runInNewContext } = await import("node:vm");
  const ts = (await import("typescript")).default;
  const require = createRequire(import.meta.url);
  const source = await readFile(new URL("./homework-actions.tsx", import.meta.url), "utf8");
  const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText;
  // A tiny hook runtime: the component is called directly, state lives here, a click is the element's onClick.
  const state: unknown[] = [];
  let slot = 0;
  const react = { useState: (initial: unknown) => { const at = slot++; if (!(at in state)) state[at] = initial; return [state[at], (value: unknown) => { state[at] = value; }]; }, useRef: (current: unknown) => ({ current }) };
  const Sheet = (props: unknown) => props;
  const context: any = { exports: {}, require: (name: string) => name === "react" ? react : name === "./sheet" ? { Sheet } : name === "./icons" ? { Icon: () => null } : require(name) };
  runInNewContext(code, context);
  const render = () => { slot = 0; return context.exports.HomeworkActions({ title: "Физика: задачи", actions: [{ label: "Изменить", onSelect() {} }], onDelete() {} }); };
  const flat = (node: any): any[] => node == null || typeof node !== "object" ? [] : Array.isArray(node) ? node.flatMap(flat) : [node, ...flat(node.props?.children)];
  const find = (tree: any, cls: string) => flat(tree).find(n => n.type === "button" && n.props.className?.split(" ").includes(cls));
  const sheetOpen = (tree: any) => flat(tree).some(n => n.type === Sheet);

  let tree = render();
  const row = find(tree, "homework-open");
  assert.ok(row, "row opener");
  assert.equal(row.props["aria-hidden"], "true", "one accessible action: «⋯»");
  assert.equal(row.props.tabIndex, -1);
  assert.equal(sheetOpen(tree), false);
  row.props.onClick();
  tree = render();
  assert.equal(sheetOpen(tree), true, "tapping the row opens the task sheet");
  assert.equal(flat(tree).find(n => n.type === Sheet).props.title, "Действия с заданием");

  state.length = 0; tree = render();
  find(tree, "homework-more").props.onClick();
  assert.equal(sheetOpen(render()), true, "«⋯» opens the same sheet");

  const css = await readFile(new URL("./styles.css", import.meta.url), "utf8");
  assert.match(css, /\.homework-open \{ position: absolute; inset: -8px; z-index: 0;/);
  assert.match(css, /\.homework-row:has\(> \.homework-open\) > :not\(\.homework-open, \.sheet\) \{ position: relative; z-index: 1; pointer-events: none; \}/);
  assert.match(css, /\.homework-row:has\(> \.homework-open\) :is\(input, \.homework-check, button:not\(\.homework-open\), a, \.chip\[aria-label\]\) \{ pointer-events: auto; \}/);
  // Both task lists (shared copies with edit rights and personal tasks) render HomeworkActions inside .homework-row.
  for (const marker of ['<HomeworkActions title={item.title}', '<HomeworkActions title={`${item.subject}: ${item.text.slice(0, 80)}`}']) {
    const at = page.indexOf(marker);
    assert.ok(at > 0 && page.lastIndexOf('<div className="homework-row">', at) > page.lastIndexOf("</article>", at), marker);
  }
});
