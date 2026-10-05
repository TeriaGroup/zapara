import assert from "node:assert/strict";
import { test } from "node:test";
import { friendRoomMark, openingDate, smartDate, teacherLessonLabel } from "./parity.ts";
import { subgroupIndex } from "./subgroups.ts";
import type { Lesson } from "./types.ts";

test("sunday opens the next monday", () => {
  const opened = smartDate(new Date(2026, 8, 20, 10, 0, 0), []);
  assert.equal(opened.getFullYear(), 2026);
  assert.equal(opened.getMonth(), 8);
  assert.equal(opened.getDate(), 21);
});

test("fifteen minutes after the last pair opens the next day", () => {
  const opened = smartDate(new Date(2026, 8, 14, 17, 0, 0), [{ dayOfWeek: 1, timeEnd: "16:30" }]);
  assert.equal(opened.getDate(), 15);
});

test("the day stays open exactly fifteen minutes after the last pair", () => {
  const opened = smartDate(new Date(2026, 8, 14, 16, 45, 0), [{ dayOfWeek: 1, timeEnd: "16:30" }]);
  assert.equal(opened.getDate(), 14);
});

test("a pair that runs past 18:00 keeps today", () => {
  const opened = smartDate(new Date(2026, 8, 14, 18, 0, 0), [{ dayOfWeek: 1, timeEnd: "19:40" }]);
  assert.equal(opened.getDate(), 14);
});

test("saturday after the last pair skips sunday", () => {
  const opened = smartDate(new Date(2026, 8, 19, 17, 0, 0), [{ dayOfWeek: 6, timeEnd: "16:30" }]);
  assert.equal(opened.getDate(), 21);
});

function lesson(partial: Partial<Lesson>): Lesson {
  return {
    dayOfWeek: 1,
    parity: 0,
    index: 1,
    timeStart: "09:00",
    timeEnd: "10:35",
    subjectRaw: "пр ИН. ЯЗ.",
    subjectNormalized: "ин. яз.",
    typeRaw: "пр",
    teacherRaw: "Иванов И.И.",
    classroomRaw: "101;",
    roomRaw: "101",
    buildingRaw: "УЛК",
    ...partial,
  };
}

const period = { start: "2026-09-01", weekCount: 2, invert: false };

test("an even-week pair does not keep an odd monday open", () => {
  const lessons = [
    lesson({ parity: 1, timeEnd: "14:15", teacherRaw: "Иванов И.И." }),
    lesson({ parity: 2, timeEnd: "18:50", teacherRaw: "Петров П.П.", classroomRaw: "202;", roomRaw: "202" }),
  ];
  const opened = openingDate(new Date(2026, 8, 14, 15, 0, 0), lessons, {}, period);
  assert.equal(opened.getFullYear(), 2026);
  assert.equal(opened.getMonth(), 8);
  assert.equal(opened.getDate(), 15);
});

test("another subgroup's later pair does not keep today open", () => {
  const lessons = [
    lesson({ parity: 0, timeEnd: "14:15", teacherRaw: "Иванов И.И.", index: 1 }),
    lesson({ parity: 1, timeEnd: "18:50", teacherRaw: "Петров П.П.", index: 2, classroomRaw: "202;", roomRaw: "202" }),
  ];
  const stream = subgroupIndex(lessons).streams[0];
  const ivanov = stream.options.find(option => option.id.includes("иванов"));
  assert.ok(ivanov);
  const opened = openingDate(new Date(2026, 8, 14, 15, 0, 0), lessons, { [stream.id]: ivanov.id }, period);
  assert.equal(opened.getDate(), 15);
});

test("a friend in the room at another hour is not marked, and a later friend who is there is", () => {
  const mine = { timeStart: "09:00", timeEnd: "10:35", roomRaw: "326", buildingRaw: "УЛК" };
  const friends = [
    { enabled: true, lessons: [{ timeStart: "14:00", timeEnd: "15:35", roomRaw: "326", buildingRaw: "УЛК" }] },
    { enabled: true, lessons: [{ timeStart: "09:00", timeEnd: "10:35", roomRaw: "326", buildingRaw: "УЛК" }] },
  ];
  assert.equal(friendRoomMark(mine, [friends[0]]), undefined);
  assert.equal(friendRoomMark(mine, friends), "друг в аудитории");
});

test("two lessons at 09:00 on different days stay distinguishable", () => {
  const monday = teacherLessonLabel({ dayOfWeek: 1, parity: 1, timeStart: "09:00" });
  const wednesday = teacherLessonLabel({ dayOfWeek: 3, parity: 2, timeStart: "09:00" });
  assert.equal(monday, "пн · нечётная · 09:00");
  assert.notEqual(monday, wednesday);
  assert.match(wednesday, /ср/);
  assert.match(wednesday, /чётная/);
});
