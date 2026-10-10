import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

// #27 (X-02): боковое меню web и desktop — один порядок и одни значки (design/navigation.md).
const web = readFileSync(new URL("./App.tsx", import.meta.url), "utf8");
const desktop = readFileSync(new URL("../../src/Vograph.Desktop/Shell/ShellViewModel.cs", import.meta.url), "utf8");
const doc = readFileSync(new URL("../../design/navigation.md", import.meta.url), "utf8");

const webKey: Record<string, string> = { schedule: "Schedule", week: "Week", summary: "Summary", teachers: "Teachers",
  maps: "Maps", friends: "Friends", homework: "Homework", chat: "Chat", community: "Community", group: "Group", settings: "Settings" };

const webItems = [...web.slice(web.indexOf("const items"), web.indexOf("];", web.indexOf("const items")))
  // подпись пункта — литерал или ключ каталога S.* (G-3, #98)
  .matchAll(/\["(\w+)", (?:"[^"]+"|S\.\w+), "(\w+)"\]/g)].map(m => ({ key: webKey[m[1]], icon: m[2].toLowerCase() }));
const desktopItems = [...desktop.matchAll(/Make\(SectionKey\.(\w+), "\w+", "Icon\.(\w+)"/g)]
  .map(m => ({ key: m[1], icon: m[2].toLowerCase() }));

test("#27: web and desktop sidebars list the same sections in the same order with the same icons", () => {
  assert.equal(webItems.length, 11);
  assert.deepEqual(desktopItems, webItems);
});

test("#27: every section has its own icon on both clients", () => {
  assert.equal(new Set(webItems.map(i => i.icon)).size, webItems.length);
  assert.equal(new Set(desktopItems.map(i => i.icon)).size, desktopItems.length);
});

test("#27: the agreed list is written down", () => {
  for (const icon of ["Icon.Calendar", "Icon.Chat", "Icon.Community", "Icon.Users", "Icon.Settings"]) assert.ok(doc.includes(icon), icon);
});
