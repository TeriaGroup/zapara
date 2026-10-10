import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

// #27 (W-18): у всех пунктов настроек разные иконки.
test("settings sections use distinct icons", () => {
  const source = readFileSync(new URL("./pages.tsx", import.meta.url), "utf8");
  const icons = [...source.matchAll(/\{ id: "(\w+)" as const, icon: "(\w+)" as const/g)].map(m => [m[1], m[2]]);
  assert.ok(icons.length >= 8, `found ${icons.length}`);
  const seen = new Map<string, string>();
  for (const [id, icon] of icons) { assert.ok(!seen.has(icon), `${id} reuses «${icon}» from ${seen.get(icon)}`); seen.set(icon, id); }
  assert.equal(Object.fromEntries(icons).notifications, "bell");
  assert.equal(Object.fromEntries(icons).updates, "upload");
});
