import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

// R2-16: в согласии при регистрации точка после «…персональных данных» стояла одна на строке.
test("R2-16: ссылки в подписи чекбокса строчные — точка не отрывается от последнего слова", () => {
  const css = readFileSync(new URL("./styles.css", import.meta.url), "utf8");
  const rule = css.slice(css.indexOf(".check a {"), css.indexOf("}", css.indexOf(".check a {")));
  assert.match(rule, /display:\s*inline;/);
  assert.doesNotMatch(rule, /inline-flex|inline-block/);
  const pages = readFileSync(new URL("./pages.tsx", import.meta.url), "utf8");
  // точка сразу за ссылкой, без пробела — разрыва между словом и точкой нет
  assert.match(pages, /политику обработки персональных данных<\/Link>\.<\/span>/);
});
