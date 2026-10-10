import test from "node:test";
import assert from "node:assert/strict";
import { buildGroupChatContext } from "./groupChatContext.ts";
import type { GroupTopic, Lesson, Period } from "./types.ts";

const period: Period = { start: "2026-09-21", weekCount: 2, title: "Осень", timeZone: "Europe/Moscow" };
const lesson: Lesson = {
  dayOfWeek: 1, parity: 0, index: 2, timeStart: "12:40", timeEnd: "14:15",
  subjectRaw: "лаб Физика", subjectNormalized: "Физика", typeRaw: "лаб",
  teacherRaw: "Иванов", classroomRaw: "311", roomRaw: "311", buildingRaw: "А",
};
const topic = (topicId: string | null, kind: "chat" | "ballots", unread: number, activeBallots: number): GroupTopic => ({
  topicId, kind, unread, activeBallots, title: topicId || "Общий", icon: "#",
  lastBody: null, lastAuthor: null, lastAt: null, canDelete: false, canPost: true,
  description: "", accent: "default", pinned: false, writePolicy: "all",
});
const base = {
  communityGroupName: " Н162С ", selectedGroupName: "н162с", timetableAvailable: true,
  lessons: [lesson], subgroupChoices: {}, period, invert: false,
  topics: [topic(null, "chat", 2, 0), topic("polls", "ballots", 0, 1)],
  now: new Date(2026, 8, 21, 11, 0),
};

test("group context shows the next lesson only for the exact selected academic group", () => {
  const match = buildGroupChatContext(base);
  assert.equal(match.nextLesson?.subject, "Физика");
  assert.equal(match.nextLesson?.time, "12:40");
  assert.equal(match.nextLesson?.room, "311");
  assert.equal(match.activeBallots, 1);
  assert.equal(match.unread, 2);

  const otherGroup = buildGroupChatContext({ ...base, communityGroupName: "Н163С" });
  assert.equal(otherGroup.nextLesson, null);
  assert.equal(otherGroup.activeBallots, 1);

  const noTimetable = buildGroupChatContext({ ...base, timetableAvailable: false });
  assert.equal(noTimetable.nextLesson, null);
});

test("group context skips past lessons and synthetic ballot aggregate without inventing activity", () => {
  const future = buildGroupChatContext({ ...base, now: new Date(2026, 8, 21, 13, 0) });
  assert.equal(future.nextLesson?.date.getDate(), 21);
  const ended = buildGroupChatContext({ ...base, now: new Date(2026, 8, 21, 15, 0) });
  assert.equal(ended.nextLesson?.date.getDate(), 28);
  const context = buildGroupChatContext({
    ...base, now: new Date(2026, 8, 21, 13, 0), timetableAvailable: false,
    topics: [topic(null, "ballots", 50, 9), topic("quiet", "chat", 0, 0)],
  });
  assert.equal(context.nextLesson, null);
  assert.equal(context.activeBallots, 0);
  assert.equal(context.unread, 0);
  assert.equal(context.hasContent, false);
});

test("invalid lesson times do not appear as the next group event", () => {
  const context = buildGroupChatContext({
    ...base, lessons: [{ ...lesson, timeStart: "99:99" }], topics: [],
  });
  assert.equal(context.nextLesson, null);
  assert.equal(context.hasContent, false);
});

test("group context shows the lesson in progress, not the first lesson of the day (#35)", () => {
  const early: Lesson = { ...lesson, index: 4, timeStart: "14:55", timeEnd: "16:30", subjectNormalized: "Химия" };
  const late: Lesson = { ...lesson, index: 6, timeStart: "18:30", timeEnd: "20:05", subjectNormalized: "История" };
  const lessons = [early, late];
  const during = buildGroupChatContext({ ...base, lessons, now: new Date(2026, 8, 21, 18, 47) });
  assert.equal(during.nextLesson?.time, "18:30");
  assert.equal(during.nextLesson?.ongoing, true);
  const between = buildGroupChatContext({ ...base, lessons, now: new Date(2026, 8, 21, 17, 0) });
  assert.equal(between.nextLesson?.time, "18:30");
  assert.equal(between.nextLesson?.ongoing, false);
  // Полночь выбранного дня (так раньше передавалось app.date) даёт первую пару — поэтому странице нужно реальное время.
  const midnight = buildGroupChatContext({ ...base, lessons, now: new Date(2026, 8, 21) });
  assert.equal(midnight.nextLesson?.time, "14:55");
});

test("group chat page passes the real clock, not the selected schedule date (#35)", async () => {
  const { readFile } = await import("node:fs/promises");
  const source = await readFile(new URL("./pages.tsx", import.meta.url), "utf8");
  const call = source.slice(source.indexOf("buildGroupChatContext({"), source.indexOf("});", source.indexOf("buildGroupChatContext({")));
  assert.match(call, /\n\s*now,\n/);
  assert.doesNotMatch(call, /now:\s*app\.date/);
});
