import assert from "node:assert/strict";
import { test } from "node:test";
import { readFileSync } from "node:fs";

const people = readFileSync(new URL("./people.tsx", import.meta.url), "utf8");
const css = readFileSync(new URL("./styles.css", import.meta.url), "utf8");

test("#147 (R3-07): the personal-chat message actions button is the borderless toggle, like group chat", () => {
  const button = people.match(/<button className="([^"]+)" type="button" aria-label="Действия с сообщением"[^>]*>/);
  assert.ok(button, "personal chat actions button");
  assert.deepEqual(button[1].split(" ").sort(), ["message-action-toggle", "tool"]);
  assert.match(button[0], /aria-expanded=\{openMenu === message\.messageId\}/);
  // В тёмной теме без класса браузер рисовал рамку outset и серый фон — «битую картинку».
  assert.match(css, /\.message-action-toggle \{ border: 0; background: transparent;/);
});

test("#147: every bubble actions button carries the toggle class", () => {
  for (const file of ["people.tsx", "pages.tsx"]) {
    const src = readFileSync(new URL(`./${file}`, import.meta.url), "utf8");
    for (const m of src.matchAll(/<button className="([^"]*)"[^>]*aria-label=\{?[`"]Действия с сообщением/g))
      assert.match(m[1], /\bmessage-action-toggle\b/, `${file}: ${m[0].slice(0, 80)}`);
  }
});

test("#147: with a mouse the toggle is subdued until hover/focus/open; touch keeps 44px targets", () => {
  const fine = css.match(/@media \(hover: hover\) and \(pointer: fine\) \{([\s\S]*?)\n\}/);
  assert.ok(fine);
  assert.match(fine[1], /\.bubble \.message-action-toggle \{ opacity: 0\.6;/);
  assert.match(fine[1], /\.bubble:hover \.message-action-toggle, \.bubble:focus-within \.message-action-toggle, \.bubble \.message-action-toggle\[aria-expanded="true"\] \{ opacity: 1; \}/);
  assert.doesNotMatch(fine[1], /opacity: 0;|display: none|visibility: hidden/);
  assert.match(css, /@media \(pointer: coarse\) \{ \.bubble \.message-action-toggle, \.bubble > \.meta \.tool \{ min-width: 44px; min-height: 44px; \} \}/);
});
