import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

// R2-03: в сетке из 7 колонок пятница вылезала за край карточки («…ПР.МОД.МИРТС», плашки перерывов),
// а «Сегодня» уезжала под дату. Проверяем правила, которые это держат.
const css = readFileSync(new URL("./styles.css", import.meta.url), "utf8");
const rule = (selector: string) => {
  const at = css.lastIndexOf(selector + " {");
  assert.ok(at >= 0, `нет правила ${selector}`);
  return css.slice(at, css.indexOf("}", at));
};

test("R2-03: дорожка пар дня не раздувается длинным словом", () => {
  assert.match(rule(".week-day-lessons"), /grid-template-columns:\s*minmax\(0,\s*1fr\)/);
  assert.ok(css.includes(".week-grid .week-day { min-width: 0; }"), "карточка дня может сжиматься");
  const words = rule(".week-lesson-subject, .week-lesson-room, .week-grid .free-gap > span:first-child");
  assert.match(words, /overflow-wrap:\s*anywhere/);
  assert.match(words, /min-width:\s*0/);
  assert.match(rule(".week-grid .free-gap"), /flex-wrap:\s*wrap/);
  assert.match(rule(".week-grid .free-gap-duration"), /white-space:\s*nowrap/);
});

test("R2-03: «Сегодня» остаётся в строке с датой; в узкой колонке — точка с текстом для чтения с экрана", () => {
  assert.match(rule(".week-grid .week-day-head"), /flex-wrap:\s*nowrap/);
  assert.match(css, /@media \(min-width: 1280px\) \{ \.week-grid \.week-day \{ container-type: inline-size; \} \}/);
  const narrow = css.slice(css.indexOf("@container (max-width: 150px)"));
  assert.match(narrow, /\.chip\.week-today \{[^}]*font-size:\s*0/);
  assert.match(narrow, /\.chip\.week-today \{[^}]*border-radius:\s*50%/);
  assert.match(rule('.week-grid .week-day[aria-current="date"]'), /box-shadow:\s*inset 0 0 0 2px var\(--accent\)/);
});
