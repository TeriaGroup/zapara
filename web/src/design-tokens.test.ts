// #11: пороги контраста пар токенов и актуальность сгенерированных файлов. Падает в CI, если пара не проходит.
import assert from "node:assert/strict";
import { test } from "node:test";
// @ts-ignore — генератор на JS, общий для web, desktop и админки.
import { parse, stale, tokens } from "../../scripts/design/tokens.mjs";

type Palette = Record<string, string>;
const channel = (value: number) => { const c = value / 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
function luminance(hex: string) {
  const { rgb, alpha } = parse(hex);
  assert.equal(alpha, "FF", `${hex}: для проверки контраста нужен непрозрачный цвет`);
  const [r, g, b] = [0, 2, 4].map(i => parseInt(rgb.slice(i, i + 2), 16));
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}
export function contrast(a: string, b: string) {
  const [x, y] = [luminance(a), luminance(b)].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
}

const t = tokens();
const thresholds: Record<string, number> = { text: 4.5, border: 3, indicator: 3 };

test("contrast helper matches WCAG reference values", () => {
  assert.equal(contrast("#FFFFFF", "#000000").toFixed(2), "21.00");
  assert.equal(contrast("#767676", "#FFFFFF").toFixed(2), "4.54");
});

for (const theme of ["light", "dark"] as const) {
  test(`${theme}: every listed token pair meets its contrast threshold`, () => {
    const palette: Palette = t.color[theme];
    const failures: string[] = [];
    for (const [kind, pairs] of Object.entries(t.contrast) as [string, [string, string[]][]][]) {
      if (kind.startsWith("$")) continue;
      for (const [fg, backgrounds] of pairs)
        for (const bg of backgrounds) {
          assert.ok(palette[fg] && palette[bg], `${theme}: нет токена ${palette[fg] ? bg : fg}`);
          const ratio = contrast(palette[fg], palette[bg]);
          if (ratio < thresholds[kind]) failures.push(`${fg} на ${bg}: ${ratio.toFixed(2)} < ${thresholds[kind]}`);
        }
    }
    assert.deepEqual(failures, []);
  });

  test(`${theme}: required roles exist`, () => {
    for (const role of ["text-primary", "text-secondary", "border-control", "surface-0", "surface-1", "surface-2", "accent", "on-accent", "danger", "warning", "success"])
      assert.ok(t.color[theme][role], role);
  });
}

test("secondary text is at least 13 px, spacing and radii follow the scale", () => {
  assert.ok(Math.min(...Object.values(t.font.size) as number[]) >= 13);
  assert.deepEqual(t.space, [4, 8, 12, 16, 24, 32]);
  assert.equal(t.radius.control, 8);
  assert.equal(t.radius.card, 12);
});

test("lesson type colors are the same set for both themes", () => {
  assert.deepEqual(Object.keys(t.lesson.light).sort(), Object.keys(t.lesson.dark).sort());
});

test("generated web, desktop and admin files are up to date with design/tokens.json", () => {
  assert.deepEqual(stale(), [], "запустите node scripts/design/tokens.mjs");
});

// #8 (G-1): «в коде нет захардкоженных цветов для текста, границ полей и опасных действий». Цвет текста и границ в
// styles.css — только переменные токенов. Исключения — то, что не меняется с темой: кнопки входа VK ID / Яндекс ID
// (вид по правилам брендов) и подписи поверх видео.
test("styles.css sets text and border colors through token variables only", async () => {
  const { readFile } = await import("node:fs/promises");
  const css = await readFile(new URL("./styles.css", import.meta.url), "utf8");
  const allowed = [/^\.id-btn\b/, /^\.circle \.(play|time)\b/];
  const hits: string[] = [];
  for (const rule of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    const selector = rule[1].trim().replace(/^\/\*[\s\S]*?\*\/\s*/g, "");
    for (const d of rule[2].matchAll(/(?:^|;)\s*((?:-webkit-text-fill-)?color|caret-color|border(?:-[a-z]+)*|outline(?:-color)?|--danger-ink|--field-[a-z]+)\s*:\s*([^;]+)/g))
      if (/#[0-9a-f]{3,8}\b|rgba?\(/i.test(d[2]) && !allowed.some(a => a.test(selector))) hits.push(`${selector} { ${d[1]}: ${d[2].trim()} }`);
  }
  assert.deepEqual(hits, []);
});
