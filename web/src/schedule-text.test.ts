import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { metaLine, roomText, subjectText, teacherText, typeLabel, lessonKindOf, formatDateTime, formatRange, formatDay, t } from "./schedule-text.ts";

const cases = JSON.parse(readFileSync(new URL("../../design/strings/schedule-cases.json", import.meta.url), "utf8"));

test("subjects from the feed: type prefix removed, readable case, dictionary names", () => {
  for (const [raw, type, short, full] of cases.subjects) assert.deepEqual(subjectText(raw, type), { short, full }, raw);
});
test("rooms: no asterisks or semicolons, «268 (Фесто)»", () => {
  for (const [raw, expected] of cases.rooms) assert.equal(roomText(raw), expected, raw);
});
test("teachers: «Фамилия И. О.», several joined by comma", () => {
  for (const [raw, expected] of cases.teachers) assert.equal(teacherText(raw), expected, raw);
});
test("meta line has no empty fields or lone «·»", () => {
  for (const [parts, expected] of cases.meta) assert.equal(metaLine(...parts), expected);
});
test("lesson type chip labels are sentence case", () => {
  assert.equal(typeLabel("пр"), "Практика");
  assert.equal(typeLabel("лаб"), "Лабораторная");
  assert.equal(typeLabel("зачет"), "Зачёт");
  assert.equal(typeLabel("семинар"), "Семинар");
  assert.equal(lessonKindOf("ЛЕК"), "lecture");
});
test("date formats follow the glossary", () => {
  const now = new Date(2026, 9, 9, 12);
  assert.equal(formatDateTime(new Date(2026, 9, 9, 18, 31), now), "9 окт., 18:31");
  assert.equal(formatRange(new Date(2026, 9, 5), new Date(2026, 9, 11), now), "5–11 окт.");
  assert.equal(formatRange(new Date(2026, 8, 28), new Date(2026, 9, 4), now), "28 сент.–4 окт.");
  assert.equal(formatDay(new Date(2025, 11, 30), now), "30 дек. 2025");
  assert.equal(t("floorN", 3), "3 этаж");
});
