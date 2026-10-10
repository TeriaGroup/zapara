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
