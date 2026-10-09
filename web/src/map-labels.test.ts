import { test } from "node:test";
import assert from "node:assert/strict";
import { buildingName, findRoom, lessonRoom, nextLessonCaption } from "./map-labels.ts";
import type { Lesson, MapPlan } from "./types.ts";

const plans: MapPlan[] = [
  { id: "gk-2", building: "ГК", floor: 2, url: "" }, { id: "gk-4", building: "ГК", floor: 4, url: "" },
  { id: "ulk-2", building: "УЛК", floor: 2, url: "" }, { id: "ulk-3", building: "УЛК", floor: 3, url: "" },
];
const lesson = (over: Partial<Lesson>) => ({ timeStart: "18:30", timeEnd: "20:00", roomRaw: "268", classroomRaw: "268 (Фесто);", buildingRaw: "УЛК", ...over }) as Lesson;

test("building codes have full names", () => {
  assert.equal(buildingName("ГК"), "Главный корпус");
  assert.equal(buildingName("улк"), "Учебно-лабораторный корпус");
  assert.equal(buildingName("ХЗ"), "ХЗ");
});

test("next lesson caption shows time, room and plan; while running it says «Сейчас»", () => {
  const before = nextLessonCaption(lesson({}), plans, new Date(2026, 9, 12, 17, 0));
  assert.equal(before?.label, "Следующая пара");
  assert.equal(before?.text, "18:30 · 268 (Фесто) · УЛК, 2 этаж");
  assert.equal(before?.plan?.id, "ulk-2");
  const running = nextLessonCaption(lesson({}), plans, new Date(2026, 9, 12, 19, 0));
  assert.equal(running?.label, "Сейчас");
  assert.equal(running?.text, "до 20:00 · 268 (Фесто) · УЛК, 2 этаж");
  assert.equal(nextLessonCaption(null, plans, new Date()), null);
  const unmarked = nextLessonCaption(lesson({ roomRaw: "Дистанционно", classroomRaw: "Дистанционно", buildingRaw: "" }), plans, new Date(2026, 9, 12, 17, 0));
  assert.equal(unmarked?.plan, null);
  assert.equal(lessonRoom(lesson({ classroomRaw: "372*;" })), "372");
});

test("room search opens the matching building and floor", () => {
  assert.deepEqual(findRoom(plans, "УЛК 320"), { plan: plans[3], message: "Аудитория 320: Учебно-лабораторный корпус, 3 этаж" });
  assert.equal(findRoom(plans, "493").plan?.id, "gk-4");
  assert.equal(findRoom(plans, "").message, "");
  assert.match(findRoom(plans, "Фесто").message, /номер аудитории/);
  assert.equal(findRoom(plans, "999").plan, null);
});
