import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { S } from "./strings.gen.ts";

const root = fileURLToPath(new URL("../../", import.meta.url));
const read = (path: string) => readFileSync(join(root, path), "utf8");
const catalog = JSON.parse(read("design/strings/ru.json"));

function files(dir: string, ok: (name: string) => boolean): string[] {
  return readdirSync(join(root, dir)).flatMap(name => {
    const path = join(dir, name);
    if (statSync(join(root, path)).isDirectory()) return name === "node_modules" || name === "bin" || name === "obj" ? [] : files(path, ok);
    return ok(name) ? [path] : [];
  });
}

test("generated web and desktop string files match design/strings/ru.json", async () => {
  const generator = await import(join(root, "scripts/design/strings.mjs"));
  assert.equal(read("web/src/strings.gen.ts"), generator.webFile, "запустите node scripts/design/strings.mjs");
  assert.equal(read("src/Vograph.Core/Services/SharedStrings.g.cs"), generator.csFile, "запустите node scripts/design/strings.mjs");
});

test("X-01 terms come from one catalog and desktop keys are not duplicated in I18nService", () => {
  const i18n = read("src/Vograph.Core/Services/I18nService.cs");
  for (const [key, value] of Object.entries<any>(catalog.strings)) {
    assert.ok(value.ru.trim().length > 0, key);
    for (const alias of value.desktop ?? []) assert.ok(!i18n.includes(`["${alias}"] =`), `${alias} должен браться из каталога, а не из I18nService`);
  }
  assert.ok(i18n.includes("SharedStrings.Desktop"), "I18nService накладывает строки каталога");
});

// Lint: «...» вместо «…» и известные слова без «ё» в видимых строках.
const yoWords = Object.keys(catalog.yo);
const literal = /"((?:[^"\\\n]|\\.)*)"|`((?:[^`\\]|\\.)*)`|'((?:[^'\\\n]|\\.)*)'/g;
function problems(path: string, text: string): string[] {
  const found: string[] = [];
  text.split("\n").forEach((line, index) => {
    if (/===|!==|\bor\b|\bcase\b|aliases|Aliases|\bswitch\b|\.includes\(/.test(line)) return; // сравнения со строками фида и поисковые синонимы
    for (const match of line.matchAll(literal)) {
      const value = match[1] ?? match[2] ?? match[3] ?? "";
      if (!/[А-Яа-яЁё]/.test(value)) continue;
      if (/[А-Яа-яЁё]\.\.\./.test(value)) found.push(`${path}:${index + 1}: «...» → «…»: ${value.slice(0, 60)}`);
      const words = value.toLowerCase().match(/[а-яё]+/g) ?? [];
      for (const word of words) for (const plain of yoWords) {
        if (word.startsWith(plain) && !value.toLowerCase().includes(catalog.yo[plain])) found.push(`${path}:${index + 1}: «${word}» без ё`);
      }
    }
  });
  return found;
}

test("no «...» and no known words without ё in visible strings (catalog, desktop, web)", () => {
  const catalogText = Object.values<any>(catalog.strings).map(value => JSON.stringify(value.ru)).join("\n");
  const sources = [
    "src/Vograph.Core/Services/I18nService.cs",
    ...files("src/Vograph.Desktop", name => name.endsWith(".axaml")),
    ...files("web/src", name => /\.tsx?$/.test(name) && !name.endsWith(".test.ts") && !name.endsWith(".gen.ts")),
  ];
  const found = [...problems("design/strings/ru.json", catalogText), ...sources.flatMap(path => problems(path, read(path)))];
  assert.deepEqual(found, []);
});

test("lint catches the mistakes it is meant to catch", () => {
  assert.equal(problems("x.ts", `const a = "Загрузка...";`).length, 1);
  assert.equal(problems("x.ts", `const a = "четная неделя";`).length, 1);
  assert.equal(problems("x.ts", `const a = "чётная неделя";`).length, 0);
  assert.equal(problems("x.ts", `if (value === "зачет") return;`).length, 0);
});

test("product name is one catalog constant and keeps the name the app already shows", () => {
  assert.equal(S.productName, "Расписание военмех");
  assert.match(read("web/src/App.tsx"), /className="brand">\{S\.productName\}</);
});
