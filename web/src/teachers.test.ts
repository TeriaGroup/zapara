import assert from "node:assert/strict";
import { test } from "node:test";
import { composeSummary } from "./summary.ts";
import { subgroupIndex, visibleLessons } from "./subgroups.ts";
import { lessonsOfGroupTeacher, sameTeacher, teacherCode, teacherRows, teacherWeek } from "./teachers.ts";
import type { Lesson, Teacher, TeacherLesson } from "./types.ts";

const catalog: Teacher[] = [
  { id: "late", name: "Кузнецова Анна Сергеевна", kafedra: "И3", shortName: "Кузнецова А.С." },
  { id: "other", name: "Смирнов Пётр", kafedra: "А1", shortName: "Смирнов П." },
];

function lesson(partial: Partial<Lesson>): Lesson {
  return {
    dayOfWeek: 1, parity: 1, index: 1, timeStart: "09:00", timeEnd: "10:35",
    subjectRaw: "лек История", subjectNormalized: "история", typeRaw: "лек",
    teacherRaw: "Кузнецова А.С.", classroomRaw: "456", roomRaw: "456", buildingRaw: "",
    ...partial,
  };
}

test("a spelled-out catalog name matches the short name from the group", () => {
  assert.equal(sameTeacher("Кузнецова Анна Сергеевна", "Кузнецова А.С."), true);
  assert.equal(sameTeacher("Смирнов Пётр", "Кузнецова А.С."), false);
});

test("search finds a group teacher who is not among the first catalog rows", () => {
  const many = Array.from({ length: 45 }, (_, index) => ({ id: "n" + index, name: "Аааров " + index, kafedra: "А1", shortName: "" }));
  const lessons = [lesson({})];
  const found = teacherRows([...many, ...catalog], lessons, "Кузнецова", false);
  assert.equal(found.rows.some(row => row.id === "late" && row.mine), true);
  const mine = teacherRows([...many, ...catalog], lessons, "", true);
  assert.deepEqual(mine.rows.map(row => row.id), ["late"]);
});

test("a teacher missing from the catalog is still reachable from the group timetable", () => {
  const lessons = [lesson({ teacherRaw: "Новиков Н.Н.", subjectRaw: "пр Физика", typeRaw: "пр" })];
  const found = teacherRows(catalog, lessons, "Новиков", false);
  assert.equal(found.rows.length, 1);
  assert.equal(found.rows[0].mine, true);
  const week = teacherWeek(lessonsOfGroupTeacher(lessons, found.rows[0].name, "3313", "А863С"), teacherCode(1, false), "3313", "А863С");
  assert.equal(week[0].title, "Понедельник");
  assert.equal(week[0].rows[0].subject, "Физика");
  assert.equal(week[0].rows[0].mine, true);
  assert.equal(week[0].rows[0].parityLabel, "Нечётная неделя");
});

test("the teacher week keeps both weeks, then odd, then even", () => {
  const lessons: TeacherLesson[] = [
    { dayOfWeek: 1, timeStart: "09:00", timeEnd: "10:35", parity: 1, subjectRaw: "История", disciplineRaw: "История", classroomRaw: "456", groups: [{ idGroup: "3313", number: "А863С" }] },
    { dayOfWeek: 3, timeStart: "12:40", parity: 2, subjectRaw: "Физика", disciplineRaw: "Физика", classroomRaw: "210", groups: [{ number: "И999" }] },
  ];
  const both = teacherWeek(lessons, 0, "3313", "А863С");
  assert.equal(both.length, 2);
  assert.equal(both[0].rows[0].time, "09:00–10:35");
  assert.equal(both[0].rows[0].subject, "История");
  assert.equal(both[0].rows[0].groups, "А863С");
  assert.equal(both[0].rows[0].room, "456 ГК");
  assert.equal(both[0].rows[0].parityLabel, "Нечётная неделя");
  assert.equal(both[0].rows[0].mine, true);
  assert.equal(teacherWeek(lessons, teacherCode(1, false), "3313", "А863С")[0].rows[0].subject, "История");
  const invertedOdd = teacherWeek(lessons, teacherCode(1, true), "3313", "А863С", true);
  assert.equal(invertedOdd[0].rows[0].subject, "Физика");
  assert.equal(invertedOdd[0].rows[0].parityLabel, "Нечётная неделя");
  assert.equal(teacherWeek(lessons, 2, "3313", "А863С")[0].rows[0].mine, false);
  assert.equal(teacherWeek(lessons, 0, "3313", "А863С", true)[1].rows[0].parityLabel, "Нечётная неделя");
});

test("a catalog row keeps every group when the timetable has only the other week", () => {
  const onlyMine: TeacherLesson = {
    dayOfWeek: 1, timeStart: "09:00", timeEnd: "10:35", parity: 2,
    disciplineRaw: "лек ВЫСШ. МАТЕМАТ", typeRaw: "лек", classroomRaw: "493;", roomRaw: "493", buildingRaw: "ГК",
    groups: [{ idGroup: "3313", number: "А863С" }],
  };
  const kept = teacherWeek([onlyMine], teacherCode(2, false), "3313", "А863С");
  assert.equal(kept.length, 1);
  assert.equal(kept[0].rows[0].groups, "А863С");
  assert.equal(kept[0].rows[0].mine, true);
  const shared = { dayOfWeek: 1, timeStart: "09:00", timeEnd: "10:35", disciplineRaw: "лек ВЫСШ. МАТЕМАТ", typeRaw: "лек", classroomRaw: "493;", roomRaw: "493", buildingRaw: "ГК", groups: [{ idGroup: "3313", number: "А863С" }, { idGroup: "3314", number: "А864С" }] };
  const catalogLessons: TeacherLesson[] = [{ ...shared, parity: 1 }, { ...shared, parity: 2 }];
  const even = teacherWeek(catalogLessons, teacherCode(2, false), "3313", "А863С");
  assert.equal(even[0].rows[0].groups, "А863С, А864С");
  assert.equal(even[0].rows[0].mine, true);
  assert.equal(even[0].rows[0].parityLabel, "Чётная неделя");
  const seenAsOdd = teacherWeek(catalogLessons, teacherCode(1, true), "3313", "А863С", true);
  assert.equal(seenAsOdd[0].rows[0].groups, "А863С, А864С");
  assert.equal(seenAsOdd[0].rows[0].mine, true);
  assert.equal(seenAsOdd[0].rows[0].parityLabel, "Нечётная неделя");
});

test("a subgroup choice hides the other teacher the way the summary does", () => {
  const own = [
    lesson({ parity: 0, teacherRaw: "Иванов И.И.", index: 1, timeEnd: "14:15" }),
    lesson({ parity: 1, teacherRaw: "Петров П.П.", index: 2, classroomRaw: "202;", roomRaw: "202", timeEnd: "18:50" }),
  ];
  const stream = subgroupIndex(own).streams[0];
  const ivanov = stream.options.find(option => option.id.includes("иванов"));
  assert.ok(ivanov);
  const choices = { [stream.id]: ivanov.id };
  assert.equal(composeSummary(own, choices, 2, false).total, 1);
  const visible = visibleLessons(own, choices);
  const people: Teacher[] = [
    { id: "iv", name: "Иванов И.И.", kafedra: "", shortName: "Иванов И.И." },
    { id: "pe", name: "Петров П.П.", kafedra: "", shortName: "Петров П.П." },
  ];
  assert.deepEqual(teacherRows(people, visible, "", true).rows.map(row => row.id), ["iv"]);
  const kept = teacherWeek(
    [{ dayOfWeek: 1, timeStart: "09:00", parity: 1, subjectRaw: "Математика", groups: [{ idGroup: "3313", number: "А863С" }] }],
    0, "3313", "А863С",
  );
  assert.equal(kept.length, 1);
  assert.equal(kept[0].rows[0].groups, "А863С");
  assert.equal(kept[0].rows[0].mine, true);
});
