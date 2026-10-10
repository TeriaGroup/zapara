import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { runInNewContext } from "node:vm";
import ts from "typescript";
import { dayTimeline, lessonStatuses, nextSummary, breakItem } from "./day-timeline.ts";

const lesson = (timeStart: string, timeEnd: string, room = "") => ({ timeStart, timeEnd, roomRaw: room, classroomRaw: "" });
const friday = [lesson("14:55", "16:30", "334"), lesson("16:45", "18:20", "344"), lesson("18:30", "20:05", "455")];
const day = new Date(2026, 9, 9);

test("breaks and windows sit between lessons in time order; «Окно» only from one pair (90 min) (#13)", () => {
  const items = dayTimeline(friday);
  assert.deepEqual(items.map(item => item.kind === "lesson" ? item.lesson.timeStart : item.label),
    ["14:55", "Перерыв 16:30–16:45", "16:45", "Перерыв 18:20–18:30", "18:30"]);
  assert.equal(breakItem({ start: 600, end: 690, duration: 90 }).kind, "window");
  assert.equal(breakItem({ start: 600, end: 689, duration: 89 }).kind, "break");
  const withWindow = dayTimeline([lesson("09:00", "10:35"), lesson("12:40", "14:15")]);
  assert.equal(withWindow[1].kind, "window");
  assert.equal(withWindow[1].kind !== "lesson" && withWindow[1].label, "Окно 10:35–12:40");
  assert.equal(withWindow[1].kind !== "lesson" && withWindow[1].durationLabel, "2 ч 5 мин");
});

test("current lesson: «Идёт · до ЧЧ:ММ» with progress; earlier past, next marked once (#13)", () => {
  const statuses = lessonStatuses(friday, day, new Date(2026, 9, 9, 18, 47));
  assert.deepEqual(statuses.map(item => item.phase), ["past", "past", "current"]);
  assert.equal(statuses[2].caption, "Идёт · до 20:05");
  assert.ok(Math.abs(statuses[2].progress - 17 / 95) < 0.01);
  assert.deepEqual(lessonStatuses(friday, day, new Date(2026, 9, 9, 16, 35)).map(item => item.phase), ["past", "next", "later"]);
  assert.deepEqual(lessonStatuses(friday, day, new Date(2026, 9, 8, 12, 0)).map(item => item.phase), ["next", "later", "later"]);
  assert.deepEqual(lessonStatuses(friday, day, new Date(2026, 9, 10, 12, 0)).map(item => item.phase), ["past", "past", "past"]);
});

test("pinned summary «Следующая: 18:30, 455» / «Сейчас: до 20:05 · 455» (#13)", () => {
  assert.equal(nextSummary(friday, day, new Date(2026, 9, 9, 18, 25)), "Следующая: 18:30, 455");
  assert.equal(nextSummary(friday, day, new Date(2026, 9, 9, 18, 47)), "Сейчас: до 20:05 · 455");
  assert.equal(nextSummary(friday, day, new Date(2026, 9, 8, 9, 0)), "Первая: 14:55, 334");
  assert.equal(nextSummary(friday, day, new Date(2026, 9, 9, 21, 0)), null);
});

test("lesson sheet offers Карта, Домашка, «Обсудить в чате группы» — no separate «В чат»/«Обсудить» (#13)", async () => {
  const require = createRequire(import.meta.url);
  const React = require("react");
  const { renderToStaticMarkup } = require("react-dom/server");
  const source = await readFile(new URL("./lesson-sheet.tsx", import.meta.url), "utf8");
  const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText;
  const modules: Record<string, unknown> = {
    "react-router-dom": { Link: ({ to, children, ...rest }: any) => React.createElement("a", { href: to, ...rest }, children) },
    "./icons": { Icon: () => null },
    "./sheet": { Sheet: ({ title, children }: any) => React.createElement("section", { "data-title": title }, children) },
    "./share": { ShareMenu: ({ label }: any) => React.createElement("button", null, label) },
  };
  const context: any = { exports: {}, require: (name: string) => modules[name] ?? require(name) };
  runInNewContext(code, context);
  const html = renderToStaticMarkup(React.createElement(context.exports.LessonSheet, {
    lesson: { timeStart: "18:30", timeEnd: "20:05", subjectRaw: "Физика", roomRaw: "455", classroomRaw: "", teacherRaw: "Иванов" },
    dateLabel: "пт, 9 окт.", mapHref: "/maps?x", homeworkHref: "/homework?x", chatHref: "/group?x", share: "card", onClose() {},
  }));
  assert.match(html, /href="\/maps\?x"[^>]*>Карта</);
  assert.match(html, />Домашка</);
  assert.match(html, />Обсудить в чате группы</);
  assert.match(html, />Отправить другу</);
  assert.doesNotMatch(html, />В чат<|>Обсудить</);
});

test("schedule pages: card opens the sheet, one inline «Карта» only for current/next, no «Открыть карту» (#13)", async () => {
  const pages = await readFile(new URL("./pages.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(pages, /Открыть карту/);
  assert.doesNotMatch(pages, /<Link className="btn quiet" to=\{`\/group\?\$\{context\}`\}>.*Обсудить<\/Link>/);
  assert.match(pages, /status\.phase === "current" \|\| status\.phase === "next"/);
  assert.match(pages, /onOpen=\{\(\) => setSheet\(\{ lesson, date: app\.date \}\)\}/);
  assert.match(pages, /className="week-lesson"[^>]*onClick=\{\(\) => setSheet/);
  const card = pages.slice(pages.indexOf("function LessonCard("), pages.indexOf("function lessonContext("));
  assert.doesNotMatch(card, /ShareMenu/);
});
