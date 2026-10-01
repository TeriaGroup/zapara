import assert from "node:assert/strict";
import { test } from "node:test";
import { canUndoPersonalHomework, personalHomeworkUndo } from "./homework-undo.ts";
import type { HomeworkItem } from "./types.ts";

const original: HomeworkItem = { id: "one", subject: "Математика", text: "Задачи", done: false, created: "2026-09-30" };

test("completion undo is limited to five seconds and the exact owner and saved task state", () => {
  const undo = personalHomeworkUndo(original, "account-a/group-1", 100);
  const changed = { ...original, done: true };
  assert.equal(canUndoPersonalHomework(undo, changed, "account-a/group-1", 5099), true);
  assert.equal(canUndoPersonalHomework(undo, changed, "account-a/group-1", 5100), false);
  assert.equal(canUndoPersonalHomework(undo, changed, "account-b/group-1", 101), false);
  assert.equal(canUndoPersonalHomework(undo, changed, "account-a/group-2", 101), false);
  assert.equal(canUndoPersonalHomework(undo, undefined, "account-a/group-1", 101), false);
  assert.equal(canUndoPersonalHomework(undo, { ...changed, text: "Новое задание" }, "account-a/group-1", 101), false);
  assert.equal(canUndoPersonalHomework(undo, original, "account-a/group-1", 101), false);
});
