import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

// R2-15: заголовок «Мои задания 2 задания» повторял слово; теперь «Мои задания · 2» (для чтения с экрана — «2 задания»).
test("R2-15: заголовок списка заданий без повтора слова", () => {
  const pages = readFileSync(new URL("./pages.tsx", import.meta.url), "utf8");
  assert.match(pages, /Мои задания <span className="muted" aria-label=\{taskCount\(visibleHomework\.length\)\}>· \{visibleHomework\.length\}<\/span>/);
  assert.doesNotMatch(pages, /Мои задания <span className="chip">\{taskCount/);
});
