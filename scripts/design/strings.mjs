#!/usr/bin/env node
// Общий каталог строк (#12): design/strings/ru.json → web/src/strings.gen.ts, Vograph.Core/Services/SharedStrings.g.cs
// и android/app/src/main/res/values/strings_catalog.xml (ключи "android" у строки — имена ресурсов Android).
// Запуск: node scripts/design/strings.mjs        — перегенерировать
//         node scripts/design/strings.mjs --check — упасть, если файлы устарели
import { readFileSync, readdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const catalog = JSON.parse(readFileSync(join(root, "design/strings/ru.json"), "utf8"));
const header = "Сгенерировано scripts/design/strings.mjs из design/strings/ru.json — не править вручную.";

const web = Object.fromEntries(Object.entries(catalog.strings).map(([key, value]) => [key, value.ru]));
const desktop = {};
for (const value of Object.values(catalog.strings)) for (const alias of value.desktop ?? []) desktop[alias] = value.ru;
const subjects = Object.fromEntries(Object.entries(catalog.subjects).map(([key, value]) => [key, { short: value.short, full: value.full ?? value.short }]));

const json = value => JSON.stringify(value, null, 2);
export const webFile = `// ${header}
export const S = ${json(web)} as const;
export type StringKey = keyof typeof S;
export const lessonTypes: Record<string, string> = ${json(catalog.lessonTypes)};
export const subjectDictionary: Record<string, { short: string; full: string }> = ${json(subjects)};
export const abbreviations: readonly string[] = ${JSON.stringify(catalog.abbreviations)};
export const lowerWords: readonly string[] = ${JSON.stringify(catalog.lowerWords)};
export const leadingAbbreviations: readonly string[] = ${JSON.stringify(catalog.leadingAbbreviations)};
export const yo: Record<string, string> = ${json(catalog.yo)};
`;

const cs = value => JSON.stringify(value);
const dict = entries => entries.map(([k, v]) => `        [${cs(k)}] = ${cs(v)},`).join("\n");
export const csFile = `// ${header}
namespace Vograph.Core.Services;

public static class SharedStrings
{
    /// <summary>Ключ каталога → строка.</summary>
    public static readonly IReadOnlyDictionary<string, string> Catalog = new Dictionary<string, string>
    {
${dict(Object.entries(web))}
    };

    /// <summary>Ключ I18nService → строка каталога; I18nService берёт эти строки поверх своих.</summary>
    public static readonly IReadOnlyDictionary<string, string> Desktop = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
${dict(Object.entries(desktop))}
    };

    public static readonly IReadOnlyDictionary<string, string> LessonTypes = new Dictionary<string, string>
    {
${dict(Object.entries(catalog.lessonTypes))}
    };

    public static readonly IReadOnlyDictionary<string, (string Short, string Full)> Subjects = new Dictionary<string, (string Short, string Full)>
    {
${Object.entries(subjects).map(([k, v]) => `        [${cs(k)}] = (${cs(v.short)}, ${cs(v.full)}),`).join("\n")}
    };

    public static readonly IReadOnlySet<string> Abbreviations = new HashSet<string> { ${catalog.abbreviations.map(cs).join(", ")} };
    public static readonly IReadOnlySet<string> LowerWords = new HashSet<string> { ${catalog.lowerWords.map(cs).join(", ")} };
    /// <summary>Аббревиатуры, совпадающие с предлогом: заглавными только в начале названия («ПО мех. роб. сист.»).</summary>
    public static readonly IReadOnlySet<string> LeadingAbbreviations = new HashSet<string> { ${catalog.leadingAbbreviations.map(cs).join(", ")} };

    public static readonly IReadOnlyDictionary<string, string> Yo = new Dictionary<string, string>
    {
${dict(Object.entries(catalog.yo))}
    };
}
`;

// Android: {0} → %1$s, экранирование aapt (\' \" & <). Имя ресурса задаётся только здесь —
// если оно осталось в другом файле values/, сборка упала бы на дубликате, поэтому генератор падает раньше.
const androidDir = "android/app/src/main/res/values";
const androidCatalog = "strings_catalog.xml";
const android = [];
for (const [key, value] of Object.entries(catalog.strings)) for (const name of value.android ?? []) android.push([name, value.ru, key]);
const androidText = value => value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/'/g, "\\'").replace(/"/g, '\\"')
  .replace(/^([@?])/, "\\$1").replace(/\{(\d+)\}/g, (_, index) => `%${Number(index) + 1}$s`);
export const androidFile = `<?xml version="1.0" encoding="utf-8"?>
<!-- ${header} -->
<resources>
${android.map(([name, value, key]) => `    <string name="${name}">${androidText(value)}</string> <!-- ${key} -->`).join("\n")}
</resources>
`;
export function androidDuplicates(dir = join(root, androidDir)) {
  const names = new Set(android.map(([name]) => name));
  const found = [];
  let files = [];
  try { files = readdirSync(dir).filter(file => file.endsWith(".xml") && file !== androidCatalog); } catch { return found; }
  for (const file of files) for (const match of readFileSync(join(dir, file), "utf8").matchAll(/<string\s+name="([^"]+)"/g))
    if (names.has(match[1])) found.push(`${file}: ${match[1]}`);
  return found;
}

const outputs = [["web/src/strings.gen.ts", webFile], ["src/Vograph.Core/Services/SharedStrings.g.cs", csFile], [`${androidDir}/${androidCatalog}`, androidFile]];
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const check = process.argv.includes("--check");
  let stale = false;
  const duplicates = androidDuplicates();
  if (duplicates.length) { console.error(`строки Android из каталога заданы ещё и вручную (удалите их там):\n  ${duplicates.join("\n  ")}`); process.exit(1); }
  for (const [path, text] of outputs) {
    const full = join(root, path);
    let current = "";
    try { current = readFileSync(full, "utf8"); } catch { /* new file */ }
    if (current === text) continue;
    if (check) { console.error(`устарел: ${path}`); stale = true; } else { writeFileSync(full, text); console.log(`записан: ${path}`); }
  }
  if (stale) process.exit(1);
}
