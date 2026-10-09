// Генератор дизайн-токенов (#11): design/tokens.json → web, desktop, админка.
//   node scripts/design/tokens.mjs          — перезаписать файлы
//   node scripts/design/tokens.mjs --check  — только проверить, что файлы совпадают с tokens.json
// Сгенерированные файлы руками не правят. Контраст проверяет web/src/design-tokens.test.ts.
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

export const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
export const tokens = () => JSON.parse(readFileSync(join(root, "design", "tokens.json"), "utf8"));
const header = () => `Сгенерировано scripts/design/tokens.mjs из design/tokens.json — не править вручную.`;
const themes = ["light", "dark"];

/** "#RRGGBB" или "#RRGGBBAA" (CSS-порядок) → { rgb, alpha }. */
export function parse(hex) {
  const value = hex.replace("#", "");
  if (!/^([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$/.test(value)) throw new Error(`Неверный цвет ${hex}`);
  return { rgb: value.slice(0, 6).toUpperCase(), alpha: value.length === 8 ? value.slice(6).toUpperCase() : "FF" };
}
const css = (hex) => { const { rgb, alpha } = parse(hex); return `#${rgb}${alpha === "FF" ? "" : alpha}`.toLowerCase(); };
// Avalonia/XAML: восьмизначная запись — #AARRGGBB.
const xaml = (hex) => { const { rgb, alpha } = parse(hex); return alpha === "FF" ? `#${rgb}` : `#${alpha}${rgb}`; };
const pascal = (name) => name.split("-").map((part) => part[0].toUpperCase() + part.slice(1)).join("");

function webCss(t) {
  const block = (theme) => [
    ...Object.entries(t.color[theme]).map(([k, v]) => `  --zp-${k}: ${css(v)};`),
    ...Object.entries(t.lesson[theme]).filter(([k]) => !k.startsWith("$")).map(([k, v]) => `  --zp-lesson-${k}: ${css(v)};`),
  ].join("\n");
  const scale = [
    `  --zp-font-family: ${t.font.family}, "Segoe UI", sans-serif;`,
    `  --zp-line-height: ${t.font["line-height"]};`,
    ...Object.entries(t.font.size).map(([k, v]) => `  --zp-font-${k}: ${v}px;`),
    ...Object.entries(t.font.weight).map(([k, v]) => `  --zp-weight-${k}: ${v};`),
    ...t.space.map((v, i) => `  --zp-space-${i + 1}: ${v}px;`),
    ...Object.entries(t.radius).map(([k, v]) => `  --zp-radius-${k}: ${v}px;`),
    `  --zp-control-min-height: ${t.control["min-height"]}px;`,
    `  --zp-disabled-opacity: ${t.control["disabled-opacity"]};`,
  ].join("\n");
  return `/* ${header()} */
/* Тёмная тема — по умолчанию, светлая — :root[data-theme="light"], как в styles.css. */
:root {
${scale}
${block("dark")}
}
:root[data-theme="light"] {
${block("light")}
}
`;
}

function desktopAxaml(t) {
  const block = (theme) => [
    ...Object.entries(t.color[theme]).map(([k, v]) => `      <SolidColorBrush x:Key="Zp.${pascal(k)}" Color="${xaml(v)}" />`),
    ...Object.entries(t.lesson[theme]).filter(([k]) => !k.startsWith("$")).map(([k, v]) => `      <SolidColorBrush x:Key="Zp.Lesson.${pascal(k)}" Color="${xaml(v)}" />`),
  ].join("\n");
  const r = t.radius;
  return `<!-- ${header()} -->
<!-- Ключи Zp.* — общие токены (#11). Прежние Brush.* из Tokens.axaml переводятся на них в задачах эпика G-1. -->
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">
${Object.entries(t.font.size).map(([k, v]) => `  <sys:Double x:Key="Zp.Font.${pascal(k)}">${v}</sys:Double>`).join("\n")}
${t.space.map((v, i) => `  <sys:Double x:Key="Zp.Space.${i + 1}">${v}</sys:Double>`).join("\n")}
  <CornerRadius x:Key="Zp.Radius.Control">${r.control}</CornerRadius>
  <CornerRadius x:Key="Zp.Radius.Card">${r.card}</CornerRadius>
  <CornerRadius x:Key="Zp.Radius.Pill">${r.pill}</CornerRadius>
  <sys:Double x:Key="Zp.Control.MinHeight">${t.control["min-height"]}</sys:Double>
  <sys:Double x:Key="Zp.Control.DisabledOpacity">${t.control["disabled-opacity"]}</sys:Double>

  <ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Light">
${block("light")}
    </ResourceDictionary>
    <ResourceDictionary x:Key="Dark">
${block("dark")}
    </ResourceDictionary>
  </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
`;
}

/**
 * Палитра primary для Filament. Акцент монохромный (DESIGN.md §2), а Color::hex() берёт только оттенок
 * и строит серую шкалу, поэтому шкалу собираем из нейтральных токенов. Кнопка Filament — оттенок 600,
 * наведение — 500; 600 = accent светлой темы.
 */
function filamentPrimary(t) {
  const l = t.color.light, d = t.color.dark;
  return {
    50: l["surface-1"], 100: l["surface-chip"], 200: l["border-subtle"], 300: d["text-secondary"], 400: l["border-control"],
    500: "#333333", 600: l.accent, 700: "#0D0D0D", 800: "#0A0A0A", 900: "#070707", 950: "#000000",
  };
}

function adminPhp(t) {
  const arr = (o, indent) => Object.entries(o).filter(([k]) => !k.startsWith("$"))
    .map(([k, v]) => `${indent}${typeof k === "string" && isNaN(Number(k)) ? `'${k}'` : k} => '${css(v)}',`).join("\n");
  return `<?php

// ${header()}
// AdminPanelProvider берёт отсюда цвета Filament, design-tokens.blade.php — CSS-переменные --zp-*.
return [
    'filament' => [
        'primary' => [
${arr(filamentPrimary(t), "            ")}
        ],
        // Палитру строит Color::hex(), оттенок 600 (фон кнопки Filament) заменяется самим токеном светлой темы.
        'danger' => '${css(t.color.light.danger)}',
        'warning' => '${css(t.color.light.warning)}',
        'success' => '${css(t.color.light.success)}',
        'info' => '${css(t.color.light.info)}',
    ],
${themes.map((theme) => `    '${theme}' => [\n${arr(t.color[theme], "        ")}\n    ],`).join("\n")}
];
`;
}

function adminBlade(t) {
  const block = (theme) => Object.entries(t.color[theme]).map(([k, v]) => `        --zp-${k}: ${css(v)};`).join("\n");
  return `{{-- ${header()} Подключается в AdminPanelProvider через render hook HEAD_END. --}}
<style>
    :root {
${block("light")}
    }

    .dark {
${block("dark")}
    }
</style>
`;
}

export const outputs = (t = tokens()) => ({
  "web/src/tokens.css": webCss(t),
  "src/Vograph.Desktop/Theme/DesignTokens.axaml": desktopAxaml(t),
  "admin-panel/config/design-tokens.php": adminPhp(t),
  "admin-panel/resources/views/filament/design-tokens.blade.php": adminBlade(t),
});

/** Файлы, которые разошлись с tokens.json. */
export function stale(t = tokens()) {
  return Object.entries(outputs(t)).filter(([path, text]) => {
    try { return readFileSync(join(root, path), "utf8") !== text; } catch { return true; }
  }).map(([path]) => path);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  if (process.argv.includes("--check")) {
    const bad = stale();
    if (bad.length) { console.error(`Не совпадают с design/tokens.json: ${bad.join(", ")}. Запустите node scripts/design/tokens.mjs`); process.exit(1); }
    console.log("Токены актуальны.");
  } else {
    for (const [path, text] of Object.entries(outputs())) writeFileSync(join(root, path), text);
    console.log(`Записано: ${Object.keys(outputs()).join(", ")}`);
  }
}
