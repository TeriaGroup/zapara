import test from "node:test";
import assert from "node:assert/strict";
import { forecastIntersections, markDescription, marksForLesson, resolveFriendSchedules } from "./intersections.ts";
import type { FriendItem, Lesson, Period, TimetablePayload } from "./types.ts";

const period: Period = { start: "2026-09-21", weekCount: 2, title: "Осень", timeZone: "Europe/Moscow" };
const lesson = (patch: Partial<Lesson> = {}): Lesson => ({
  dayOfWeek: 1, parity: 0, index: 1, timeStart: "09:00", timeEnd: "10:35",
  subjectRaw: "лаб Физика", subjectNormalized: "Физика", typeRaw: "лаб",
  teacherRaw: "", classroomRaw: "326", roomRaw: "326", buildingRaw: "УЛК", ...patch,
});
const friend = (groupName: string, lessons: Lesson[] | null) => ({
  groupName, members: "Иван", enabled: true, lessons,
});

test("forecast keeps an ongoing pair, chooses the best place per friend, and skips a finished pair", () => {
  const own = lesson();
  const sameRoom = lesson({ roomRaw: "326", index: 1 });
  const sameFloor = lesson({ roomRaw: "328", index: 2 });
  const input = {
    mineLessons: [own], period, invert: false, strictness: 75,
    now: new Date(2026, 8, 21, 9, 30), horizonDays: 1,
    friends: [friend("Н162С", [sameFloor, sameRoom])],
  };
  const current = forecastIntersections(input);
  assert.equal(current.encounters.length, 1);
  assert.equal(current.encounters[0].score, 100);
  assert.equal(current.encounters[0].friendGroupName, "Н162С");
  assert.equal(current.encounters[0].members, "Иван");
  assert.equal(current.encounters[0].friendRoom, "326");
  assert.equal(forecastIntersections({ ...input, now: new Date(2026, 8, 21, 11, 0) }).encounters.length, 0);
});

test("strictness and missing cache are explicit; absent marks are optional", () => {
  const own = lesson();
  const distant = lesson({ roomRaw: "111", buildingRaw: "ГК" });
  const base = {
    mineLessons: [own], period, invert: false,
    now: new Date(2026, 8, 21, 8, 0), horizonDays: 1,
    friends: [friend("А4313", [distant]), friend("Н162С", null)],
  };
  assert.equal(forecastIntersections({ ...base, strictness: 25 }).encounters[0].score, 25);
  const strict = forecastIntersections({ ...base, strictness: 50 });
  assert.equal(strict.encounters.length, 0);
  assert.deepEqual(strict.missingGroups, ["Н162С"]);
  assert.equal(strict.checkedGroups, 1);
  assert.deepEqual(marksForLesson(own, new Date(2026, 8, 21), { ...base, strictness: 50 }, false), []);
  assert.deepEqual(marksForLesson(own, new Date(2026, 8, 21), { ...base, strictness: 50 }, true)
    .map(mark => [mark.groupName, mark.present]), [["А4313", false]]);
  assert.equal(markDescription(marksForLesson(own, new Date(2026, 8, 21), { ...base, strictness: 50 }, true)[0]),
    "в вузе · ниже выбранной точности");
});

test("a missing timetable is different from a loaded empty timetable", () => {
  const base = {
    mineLessons: [lesson()], period, invert: false, strictness: 25,
    now: new Date(2026, 8, 21, 8, 0), horizonDays: 1,
  };
  const missing = forecastIntersections({ ...base, friends: [friend("Н162С", null)] });
  assert.equal(missing.checkedGroups, 0);
  assert.deepEqual(missing.missingGroups, ["Н162С"]);
  const empty = forecastIntersections({ ...base, friends: [friend("Н162С", [])] });
  assert.equal(empty.checkedGroups, 1);
  assert.deepEqual(empty.missingGroups, []);
});

test("friend schedules resolve a saved group name through the catalog id cache", () => {
  const saved: FriendItem[] = [
    { id: "one", groupName: "Н162С", members: "Иван", enabled: true, color: "#e0527a" },
    { id: "two", groupName: "нет в каталоге", members: "", enabled: true, color: "#e0527a" },
  ];
  const cache = { "42": { lessons: [lesson()] } as TimetablePayload };
  const resolved = resolveFriendSchedules(saved, [{ id: "42", name: "Н162С" }], cache);
  assert.equal(resolved[0].lessons?.length, 1);
  assert.equal(resolved[1].lessons, null);
});

test("a friend timetable from another snapshot is withheld from intersections", () => {
  const saved: FriendItem[] = [{ id: "one", groupName: "Н162С", members: "", enabled: true, color: "#F2A33C" }];
  const base = { period, group: { id: "42", name: "Н162С", lessonCount: 1 },
    groups: [{ id: "42", name: "Н162С", lessonCount: 1 }] };
  const payload = (snapshotId: string): TimetablePayload => ({ ...base, lessons: [lesson()],
    meta: { snapshotId, fetchedAt: "", publishedAt: "", stale: false, sourceKind: "api" } });
  const own = (snapshotId: string): TimetablePayload => ({ ...payload(snapshotId),
    group: { id: "mine", name: "А863С", lessonCount: 1 } });
  const cache = { mine: own("new"), "42": payload("old") };
  const resolved = resolveFriendSchedules(saved, base.groups, cache, cache.mine);
  assert.equal(resolved[0].lessons, null);
  assert.equal(resolveFriendSchedules(saved, base.groups, cache, own("old"))[0].lessons?.length, 1);
  assert.equal(resolveFriendSchedules(saved, base.groups, cache, payload("old"))[0].lessons, null);
});

test("forecast orders lessons by start time before taking the first three", () => {
  const late = lesson({ timeStart: "12:40", timeEnd: "14:15" });
  const early = lesson({ timeStart: "09:00", timeEnd: "10:35", index: 2 });
  const forecast = forecastIntersections({ mineLessons: [late, early],
    friends: [friend("Н162С", [late, early])], period, invert: false, strictness: 25,
    now: new Date(2026, 8, 21, 8, 0), horizonDays: 1 });
  assert.deepEqual(forecast.encounters.map(row => row.time), ["09:00", "12:40"]);
});
