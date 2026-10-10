import assert from "node:assert/strict";
import { test } from "node:test";
import { readFileSync } from "node:fs";

// #132: Sheet не портален — открытый лист рендерится внутри .homework-row. Правило строки с действиями
// не должно перебивать .sheet { position: fixed }, иначе лист рисуется внутри строки.
const css = readFileSync(new URL("./styles.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
const rules = [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(m => ({ selectors: m[1].trim().split(/,(?![^(]*\))/).map(s => s.trim()), body: m[2] }));

function position(body: string) { return /(?:^|;)\s*position\s*:\s*([a-z-]+)/.exec(body)?.[1] ?? null; }
/** Специфичность (a, b, c) для простых селекторов этого файла; :not/:is/:has — по самому специфичному аргументу. */
function specificity(selector: string): [number, number, number] {
  let a = 0, b = 0, c = 0;
  let rest = selector.replace(/:(not|is|has)\(((?:[^()]|\([^()]*\))*)\)/g, (_m, _n, args: string) => {
    const best = args.split(/,(?![^(]*\))/).map(x => specificity(x.trim())).sort((x, y) => y[0] - x[0] || y[1] - x[1] || y[2] - x[2])[0];
    a += best[0]; b += best[1]; c += best[2]; return " ";
  });
  rest = rest.replace(/#[\w-]+/g, () => { a++; return " "; }).replace(/\.[\w-]+|\[[^\]]*\]|:(?!:)[\w-]+/g, () => { b++; return " "; });
  c += (rest.match(/(^|[\s>+~])[a-z][\w-]*/gi) ?? []).length;
  return [a, b, c];
}
/** Может ли последний составной селектор совпасть с <div class="sheet">, лежащим прямо в открытой строке задания. */
function matchesSheetInRow(selector: string) {
  const parts = selector.split(/\s*>\s*|\s+/).filter(Boolean);
  const last = parts[parts.length - 1];
  const parent = parts.slice(0, -1).join(" ");
  const not = /:not\(([^)]*)\)/.exec(last)?.[1].split(",").map(x => x.trim()) ?? [];
  const classes = [...last.replace(/:not\([^)]*\)/g, "").matchAll(/\.([\w-]+)/g)].map(m => m[1]);
  const bareNot = last.replace(/:not\([^)]*\)/g, "") === "";
  const self = (classes.length > 0 && classes.every(cls => cls === "sheet")) || (bareNot && not.length > 0);
  if (!self || not.includes(".sheet")) return false;
  return classes.includes("sheet") ? parent === "" : parent.includes(".homework-row");
}

test("the open sheet inside a homework row stays position: fixed", () => {
  const matching = rules.flatMap(rule => rule.selectors.filter(matchesSheetInRow).map(selector => ({ selector, pos: position(rule.body) })))
    .filter(rule => rule.pos !== null);
  assert.ok(matching.some(rule => rule.selector === ".sheet" && rule.pos === "fixed"), ".sheet { position: fixed } на месте");
  const winner = matching.sort((x, y) => {
    const [p, q] = [specificity(x.selector), specificity(y.selector)];
    return q[0] - p[0] || q[1] - p[1] || q[2] - p[2];
  })[0];
  assert.equal(winner.pos, "fixed", `побеждает ${winner.selector} { position: ${winner.pos} }`);
});

test("row rule excludes the sheet and keeps the others above the tap target", () => {
  assert.match(css, /\.homework-row:has\(> \.homework-open\) > :not\(\.homework-open, \.sheet\) \{ position: relative; z-index: 1; pointer-events: none; \}/);
  assert.equal(specificity(".homework-row:has(> .homework-open) > :not(.homework-open)").join(), "0,3,0");
  assert.equal(specificity(".sheet").join(), "0,1,0");
});

// #132, Codex: фокус, вложения и 48px-зона флажка (проверено и в Chromium: Playwright, клики по точкам).
const actionsSource = readFileSync(new URL("./homework-actions.tsx", import.meta.url), "utf8");
const pagesSource = readFileSync(new URL("./pages.tsx", import.meta.url), "utf8");

test("row tap focuses the visible ⋯ button before opening, so closing the sheet returns focus there", () => {
  assert.match(actionsSource, /const openFromRow = \(\) => \{ more\.current\?\.focus\(\); setOpen\(true\); \};/);
  assert.match(actionsSource, /className="homework-open"[^>]*onClick=\{openFromRow\}/);
  assert.match(actionsSource, /<button ref=\{more\} className="icon-btn quiet homework-more"/);
  assert.doesNotMatch(actionsSource, /className="homework-open"[^>]*onClick=\{\(\) => setOpen\(true\)\}/);
});

test("attachments sit above the row opener's inset", () => {
  assert.match(pagesSource, /<div className="row homework-files">/);
  const rule = css.match(/\.homework-task > \.homework-files \{([^}]*)\}/);
  assert.ok(rule, "rule for attachments");
  assert.match(rule[1], /position: relative/);
  assert.match(rule[1], /z-index: 1/);
  // подложка — z-index 0 и выступает на 8px: вложения (z-index 1, позже в потоке) выше неё
  assert.match(css, /\.homework-open \{[^}]*inset: -8px[^}]*z-index: 0/);
});

test("the checkbox label is a 48×48 hit area that takes pointer events", () => {
  assert.match(css, /\.homework-row:has\(> \.homework-open\) :is\(input, \.homework-check, button:not\(\.homework-open\), a, \.chip\[aria-label\]\) \{ pointer-events: auto; \}/);
  assert.match(css, /\.homework-row > \.homework-check \{ position: relative;/);
  const zone = css.match(/\.homework-row > \.homework-check::before \{([^}]*)\}/);
  assert.ok(zone, "48px zone");
  assert.match(zone[1], /width: 48px; height: 48px/);
  assert.match(zone[1], /position: absolute/);
});
