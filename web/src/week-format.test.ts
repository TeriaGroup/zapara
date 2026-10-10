import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { runInNewContext } from "node:vm";
import ts from "typescript";
import { dayHeading, formatRuDate, parseRuDate, weekParity, weekRange } from "./week-format.ts";

test("week header: «5–11 окт · чётная», across months «28 сен – 4 окт» (#14)", () => {
  assert.equal(weekRange(new Date(2026, 9, 5), new Date(2026, 9, 11)), "5–11 окт");
  assert.equal(weekRange(new Date(2026, 9, 5), new Date(2026, 9, 11), true), "5–11 октября");
  assert.equal(weekRange(new Date(2026, 8, 28), new Date(2026, 9, 4)), "28 сен – 4 окт");
  const period = { start: "2026-09-01", weekCount: 2 };
  assert.equal(weekParity(new Date(2026, 8, 1), period, false), "нечётная");
  assert.equal(weekParity(new Date(2026, 8, 8), period, false), "чётная");
  assert.equal(weekParity(new Date(2026, 8, 8), period, true), "нечётная");
  assert.equal(weekParity(new Date(2026, 8, 8), null, false), "");
});

test("day headings are «Пт, 9 окт», never «Вчера/Сегодня/Завтра» (#14)", () => {
  assert.equal(dayHeading(new Date(2026, 9, 9)), "Пт, 9 окт");
  assert.equal(dayHeading(new Date(2026, 9, 11)), "Вс, 11 окт");
  assert.equal(dayHeading(new Date(2026, 10, 2)), "Пн, 2 ноя");
});

test("date field format is дд.мм.гггг regardless of browser locale (#14, W-04)", () => {
  assert.equal(formatRuDate(new Date(2026, 9, 9)), "09.10.2026");
  assert.equal(parseRuDate("09.10.2026")?.getTime(), new Date(2026, 9, 9).getTime());
  assert.equal(parseRuDate("9.10.2026")?.getTime(), new Date(2026, 9, 9).getTime());
  assert.equal(parseRuDate("31.02.2026"), null);
  assert.equal(parseRuDate("10/09/2026"), null);
  assert.equal(parseRuDate("09.10."), null);
});

test("RuDateField renders a text field «дд.мм.гггг», native date input only as hidden picker (#14)", async () => {
  const require = createRequire(import.meta.url);
  const React = require("react");
  const { renderToStaticMarkup } = require("react-dom/server");
  const source = await readFile(new URL("./ru-date-field.tsx", import.meta.url), "utf8");
  const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText;
  const parity = await import("./parity.ts"), planner = await import("./planner.ts"), format = await import("./week-format.ts");
  const modules: Record<string, unknown> = { "./icons": { Icon: () => null }, "./parity": parity, "./planner": planner, "./week-format": format };
  const context: any = { exports: {}, require: (name: string) => modules[name] ?? require(name) };
  runInNewContext(code, context);
  const html = renderToStaticMarkup(React.createElement(context.exports.RuDateField, { label: "Неделя по дате", value: new Date(2026, 9, 9), onChange() {} }));
  assert.match(html, /<input id="ru-date-input"[^>]*placeholder="дд.мм.гггг"[^>]*value="09.10.2026"/);
  assert.match(html, /aria-label="Открыть календарь"/);
  assert.match(html, /class="ru-date-native" type="date" tabindex="-1" aria-hidden="true"/);
});

test("week page: sticky bar with navigation, «Сегодня», search and «⋯»; tools inside the menu (#14)", async () => {
  const pages = await readFile(new URL("./pages.tsx", import.meta.url), "utf8");
  const week = pages.slice(pages.indexOf("export function WeekPage()"), pages.indexOf("const summarySegments"));
  const bar = week.slice(week.indexOf('className="week-bar"'), week.indexOf('className="week-more-menu'));
  assert.doesNotMatch(bar, /TimetableExportTools|Первый учебный день|Планирование недели|type="date"/);
  const menu = week.slice(week.indexOf('className="week-more-menu'), week.indexOf("</details>"));
  for (const item of ["RuDateField", "Первый учебный день", "Скрыть дни без пар", "TimetableExportTools", "Планирование недели"]) assert.ok(menu.includes(item), item);
  assert.doesNotMatch(week, /Учебное время|Открыть день|dayTitle\(|type="date"/);
  assert.match(week, /dayHeading\(date\)\}<\/Link>\{isToday && <span className="chip week-today">Сегодня<\/span>\}/);
  assert.match(week, /"Нет пар"/);
  const css = await readFile(new URL("./styles.css", import.meta.url), "utf8");
  assert.match(css, /\.week-bar \{ position: sticky; top: 0;/);
  assert.match(css, /\.week\.week-grid \{ grid-template-columns: repeat\(7, minmax\(0, 1fr\)\)/);
});
