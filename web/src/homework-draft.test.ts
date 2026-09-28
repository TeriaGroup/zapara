import assert from "node:assert/strict";
import { test } from "node:test";
import { HomeworkDraftController, homeworkSaveError, validateHomeworkDraft } from "./homework-draft.ts";

test("blank, preloaded and reverted drafts do not warn; files and changed fields do", () => {
  const store = new HomeworkDraftController("guest:g");
  store.preload("Математика");
  assert.equal(store.dirty, false);
  store.field("text", "Задачи");
  assert.equal(store.dirty, true);
  store.preload("Физика");
  assert.equal(store.draft.subject, "Математика");
  store.field("text", "");
  assert.equal(store.dirty, false);
  store.field("pending", [{ file: new File(["data"], "task.pdf"), kind: "document" }]);
  assert.equal(store.dirty, true);
});

test("single flight survives a routed page remount, retries retain one operation id", () => {
  const store = new HomeworkDraftController("account:g");
  store.field("subject", "Математика");
  store.field("text", "Задачи");
  const first = store.begin()!;
  assert.equal(store.begin(), null);
  store.field("text", "Нельзя изменить во время сохранения");
  assert.equal(store.draft.text, "Задачи");
  first.operation.localSaved = true;
  store.finish(first, "На устройстве сохранено. Нет сети.", false);
  const retry = store.begin()!;
  assert.equal(retry.operation.id, first.operation.id);
  store.finish(retry, "Сохранено", true);
  assert.equal(store.dirty, false);
  assert.notEqual(store.begin()!.operation.id, first.operation.id);
});

test("scope changes clear files and prevent old completion even after returning to the same account", () => {
  const store = new HomeworkDraftController("a:g");
  store.field("pending", [{ file: new File(["data"], "task.pdf"), kind: "document" }]);
  const ticket = store.begin()!;
  store.scope("b:g");
  assert.equal(store.draft.pending.length, 0);
  assert.equal(store.busy, false);
  store.scope("a:g");
  store.field("text", "Новый черновик");
  assert.equal(store.current(ticket), false);
  store.finish(ticket, "Старое сохранение", true);
  assert.equal(store.draft.text, "Новый черновик");
  store.scope("a:other-group");
  assert.equal(store.dirty, false);
});

test("validation explains whitespace and invalid dates; network errors never claim invalid format", () => {
  const store = new HomeworkDraftController("guest:g");
  assert.match(validateHomeworkDraft(store.draft), /предмет/);
  store.field("subject", "  Математика ");
  assert.match(validateHomeworkDraft(store.draft), /задание/);
  store.field("text", " Задачи ");
  store.field("share", true);
  store.field("sharedDeadline", "not-a-date");
  assert.match(validateHomeworkDraft(store.draft), /срок/);
  assert.match(homeworkSaveError(new Error("upload"), true), /На устройстве сохранено/);
  assert.doesNotMatch(homeworkSaveError(new TypeError("Failed to fetch"), false), /Такой файл/);
  assert.match(homeworkSaveError(new Error("bad"), false), /Такой файл/);
});
