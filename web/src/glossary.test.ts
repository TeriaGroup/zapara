import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { S } from "./strings.gen.ts";

// G-3: одно понятие — одно название на web и desktop (общий каталог design/strings/ru.json).
test("G-3: названия разделов из общего каталога", () => {
  assert.equal(S.navFriends, "Друзья");
  assert.equal(S.navChats, "Чаты");
  assert.equal(S.navCommunities, "Сообщества");
  const app = readFileSync(new URL("./App.tsx", import.meta.url), "utf8");
  assert.match(app, /\["friends", S\.navFriends,/);
  assert.match(app, /\["chat", S\.navChats,/);
  assert.match(app, /\["community", S\.navCommunities,/);
});

test("G-3: устаревших вариантов нет в интерфейсе", () => {
  const dir = new URL("./", import.meta.url);
  const forbidden = /"Пересечения"|title="Сообщество"|"Сообщество"\]|Чатик|Нет занятий|"Сдано"/;
  const hits = readdirSync(dir).filter(f => /\.tsx?$/.test(f) && !f.endsWith(".test.ts") && f !== "strings.gen.ts")
    .flatMap(f => readFileSync(new URL(f, dir), "utf8").split("\n").map((line, i) => [f, i + 1, line] as const))
    .filter(([, , line]) => forbidden.test(line) && !line.trim().startsWith("//"))
    .map(([f, i]) => `${f}:${i}`);
  assert.deepEqual(hits, []);
});
