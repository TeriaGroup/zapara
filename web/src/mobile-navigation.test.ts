import test from "node:test";
import assert from "node:assert/strict";
import { mobileTabForPath, studyGroupCaption } from "./mobile-navigation.ts";

test("mobile navigation selects the Android destination for main and nested chat routes", () => {
  for (const path of ["/", "/schedule", "/schedule/"]) assert.equal(mobileTabForPath(path), "schedule");
  assert.equal(mobileTabForPath("/maps"), "maps");
  assert.equal(mobileTabForPath("/homework"), "homework");
  for (const path of ["/chat", "/chat/people", "/chat/person/a", "/group"]) assert.equal(mobileTabForPath(path), "chat");
});

test("secondary and unrelated prefix routes keep the sections destination", () => {
  for (const path of ["/week", "/settings", "/friends", "/legal/policy", "/chatty", "/maps-extra", "/group-extra", "/missing"])
    assert.equal(mobileTabForPath(path), "sections");
});

test("group badge follows timetable parity including the user's inversion", () => {
  const period = { start: "2026-09-01", weekCount: 2 };
  assert.equal(studyGroupCaption("Н162С", period, new Date(2026, 8, 1), false), "Н162С · нечёт.");
  assert.equal(studyGroupCaption("Н162С", period, new Date(2026, 8, 1), true), "Н162С · чёт.");
  assert.equal(studyGroupCaption("Н162С", period, new Date(2026, 8, 8), false), "Н162С · чёт.");
});

test("missing group or invalid period does not invent parity", () => {
  const now = new Date(2026, 9, 5);
  assert.equal(studyGroupCaption(undefined, null, now, false), "Выбрать группу");
  assert.equal(studyGroupCaption("   ", null, now, false), "Выбрать группу");
  assert.equal(studyGroupCaption("Н162С", null, now, false), "Н162С");
  assert.equal(studyGroupCaption("Н162С", { start: "bad", weekCount: 2 }, now, false), "Н162С");
  assert.equal(studyGroupCaption("Н162С", { start: "2026-09-01", weekCount: 0 }, now, false), "Н162С");
  assert.equal(studyGroupCaption("Н162С", { start: "2026-09-01", weekCount: 2 }, new Date(NaN), false), "Н162С");
});
