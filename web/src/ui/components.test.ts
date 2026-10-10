// #11: базовые компоненты — разметка, доступность и «только токены» в ui.css.
import assert from "node:assert/strict";
import { test } from "node:test";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { buildSync } from "esbuild";

const require = createRequire(import.meta.url);
const React = require("react");
const { renderToStaticMarkup } = require("react-dom/server");
const bundle = buildSync({ entryPoints: [new URL("./components.tsx", import.meta.url).pathname], bundle: true, write: false,
  format: "cjs", platform: "node", jsx: "automatic", external: ["react", "react-dom"] }).outputFiles[0].text;
const module = { exports: {} as Record<string, any> };
new Function("module", "exports", "require", bundle)(module, module.exports, require);
const ui = module.exports;
const html = (type: unknown, props: Record<string, unknown> = {}, ...children: unknown[]) => renderToStaticMarkup(React.createElement(type, props, ...children));

test("Button renders the four variants as real buttons", () => {
  for (const variant of ["primary", "secondary", "ghost", "danger"])
    assert.match(html(ui.Button, { variant }, "Сохранить"), new RegExp(`^<button type="button" class="zp-btn zp-btn-${variant}">Сохранить</button>$`));
  assert.equal(html(ui.Button, { type: "submit", variant: "primary", disabled: true }, "Войти"), '<button disabled="" type="submit" class="zp-btn zp-btn-primary">Войти</button>');
});

test("Input keeps a visible label and ties hint and error to the field", () => {
  const out = html(ui.Input, { id: "login", label: "Логин", hint: "Латиница и цифры", error: "Логин занят" });
  assert.match(out, /<label class="zp-label" for="login">Логин<\/label>/);
  assert.match(out, /aria-invalid="true" aria-describedby="login-hint login-error"/);
  assert.match(out, /role="alert">Логин занят</);
  assert.doesNotMatch(html(ui.Input, { id: "x", label: "Имя" }), /aria-invalid|aria-describedby/);
});

test("SegmentedControl is a radiogroup with exactly one checked option", () => {
  const out = html(ui.SegmentedControl, { label: "Тема", value: "dark", onChange: () => {}, options: [{ value: "light", label: "Светлая" }, { value: "dark", label: "Тёмная" }] });
  assert.match(out, /role="radiogroup" aria-label="Тема"/);
  assert.equal(out.match(/aria-checked="true"/g)?.length, 1);
  assert.match(out, /aria-checked="true" class="zp-segment zp-segment-on">Тёмная/);
});

test("Chip maps lesson types to the shared words and keeps unknown text uncolored", () => {
  assert.equal(html(ui.Chip, { lesson: "лек" }), '<span class="zp-chip zp-lesson-lecture"><i aria-hidden="true"></i>Лекция</span>');
  assert.equal(html(ui.Chip, { lesson: "лабораторная работа" }), '<span class="zp-chip zp-lesson-lab"><i aria-hidden="true"></i>Лаба</span>');
  assert.equal(html(ui.Chip, { lesson: "Семинар" }), '<span class="zp-chip">Семинар</span>');
  assert.equal(html(ui.Chip, { tone: "danger" }, "Просрочено"), '<span class="zp-chip zp-chip-danger"><i aria-hidden="true"></i>Просрочено</span>');
});

test("Card, LessonRow and EmptyState render their content and state text", () => {
  assert.match(html(ui.Card, { title: "Аккаунт" }, "Текст"), /^<section class="zp-card"><h2 class="zp-card-title">Аккаунт<\/h2>Текст<\/section>$/);
  const row = html(ui.LessonRow, { start: "10:50", end: "12:20", subject: "Физика", type: "пр", room: "323", state: "now" });
  assert.match(row, /class="zp-lesson zp-lesson-now" aria-label="10:50–12:20, Физика, идёт сейчас"/);
  assert.match(row, /Идёт сейчас/);
  assert.match(row, /zp-lesson-practice.*Практика/);
  assert.doesNotMatch(html(ui.LessonRow, { start: "9:00", end: "10:30", subject: "Химия", type: "лек", state: "past" }), /Прошла</);
  assert.match(html(ui.EmptyState, { title: "Заданий пока нет", hint: "Добавьте первое задание" }), /role="status".*Заданий пока нет.*Добавьте первое задание/);
});

test("ConfirmDialog renders only when open, names the action and uses danger styling", () => {
  const props = { title: "Удалить задание?", body: "Его нельзя будет восстановить.", confirmLabel: "Удалить", danger: true, onConfirm: () => {}, onCancel: () => {} };
  assert.equal(html(ui.ConfirmDialog, { ...props, open: false }), "");
  const out = html(ui.ConfirmDialog, { ...props, open: true });
  assert.match(out, /role="alertdialog" aria-modal="true" aria-labelledby="([^"]+)"><h2 class="zp-dialog-title" id="\1">Удалить задание\?/);
  assert.match(out, /zp-btn-ghost">Отмена<\/button><button type="button" class="zp-btn zp-btn-danger">Удалить<\/button>/);
});

test("ui.css uses only tokens for colors, font sizes and radii", async () => {
  const css = (await readFile(new URL("./ui.css", import.meta.url), "utf8")).replace(/\/\*[\s\S]*?\*\//g, "");
  assert.doesNotMatch(css, /#[0-9a-f]{3,8}\b/i, "цвет вне токенов");
  assert.doesNotMatch(css, /\b(rgb|hsl|oklch)a?\(/i, "цвет вне токенов");
  for (const property of ["font-size", "border-radius", "color", "background", "border-color", "font-weight"])
    for (const match of css.matchAll(new RegExp(`(?:^|[;{\\s])${property}:([^;}]+)`, "g")))
      assert.match(match[1], /var\(--zp-|transparent|inherit|^\s*0\s*$|50%|calc\(var\(--zp-/, `${property}:${match[1]}`);
});
