import assert from "node:assert/strict";
import { test } from "node:test";
import { composeSummary } from "./summary.ts";
import { subgroupIndex } from "./subgroups.ts";
import type { Lesson } from "./types.ts";

function lesson(partial: Partial<Lesson>): Lesson {
  return {
    dayOfWeek: 1, parity: 0, index: 1, timeStart: "09:00", timeEnd: "10:35",
    subjectRaw: "Математика", subjectNormalized: "математика", typeRaw: "лек",
    teacherRaw: "Иванов", classroomRaw: "101;", roomRaw: "", buildingRaw: "",
    ...partial,
  };
}

test("odd, even and both count the same lessons as the phone summary", () => {
  const lessons = [
    lesson({ parity: 1, classroomRaw: "101;" }),
    lesson({ parity: 2, classroomRaw: "102;" }),
    lesson({ parity: 0, classroomRaw: "101;" }),
    lesson({ dayOfWeek: 2, parity: 1, classroomRaw: "101;" }),
  ];
  const both = composeSummary(lessons, {}, 2, false);
  const odd = composeSummary(lessons, {}, 0, false);
  const even = composeSummary(lessons, {}, 1, false);
  assert.equal(both.total, 4);
  assert.equal(odd.total, 3);
  assert.equal(even.total, 2);
  assert.deepEqual(odd.byDay.map(row => row.count), [2, 1, 0, 0, 0, 0]);
  assert.equal(odd.byDay[0].name, "Понедельник");
  assert.equal(both.byType[0].name, "лекция");
  assert.equal(both.bySubject[0].name, "Математика");
  assert.equal(both.byTeacher[0].name, "Иванов");
  assert.deepEqual(even.byRoom.map(row => row.name), ["101 ГК", "102 ГК"]);
  assert.equal(composeSummary([], {}, 2, false).byRoom.length, 0);
});

test("invert swaps which stored week is the odd week", () => {
  const lessons = [lesson({ parity: 1 }), lesson({ parity: 2, teacherRaw: "Петров" })];
  assert.equal(composeSummary(lessons, {}, 0, false).byTeacher[0].name, "Иванов");
  assert.equal(composeSummary(lessons, {}, 0, true).byTeacher[0].name, "Петров");
});

test("a chosen subgroup drops the other teacher's pair from the counts", () => {
  const lessons = [
    lesson({ parity: 0, teacherRaw: "Иванов И.И.", index: 1, timeEnd: "14:15" }),
    lesson({ parity: 1, teacherRaw: "Петров П.П.", index: 2, classroomRaw: "202;", roomRaw: "202", timeEnd: "18:50" }),
  ];
  const stream = subgroupIndex(lessons).streams[0];
  const ivanov = stream.options.find(option => option.id.includes("иванов"));
  assert.ok(ivanov);
  const chosen = composeSummary(lessons, { [stream.id]: ivanov.id }, 2, false);
  assert.equal(chosen.total, 1);
  assert.equal(chosen.byTeacher[0].name, "Иванов И.И.");
  assert.equal(composeSummary(lessons, {}, 2, false).total, 2);
});

test("no group is not a summary of invented counts", () => {
  const empty = composeSummary([], {}, 2, false);
  assert.equal(empty.total, 0);
  assert.deepEqual(empty.byDay.map(row => row.count), [0, 0, 0, 0, 0, 0]);
});
