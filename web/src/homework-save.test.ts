import assert from "node:assert/strict";
import { test } from "node:test";
import { HomeworkDraftController } from "./homework-draft.ts";
import { runHomeworkSave, checkHomeworkUpload } from "./homework-save.ts";

function fixture() {
  const controller = new HomeworkDraftController("account:g");
  controller.field("subject", "Математика"); controller.field("text", "Задачи"); controller.field("share", true);
  controller.field("pending", [{ file: new File(["data"], "task.pdf"), kind: "document" }]);
  const events: string[] = []; const rows = new Set<string>();
  const storedRows = new Map<string, any>();
  let uploadFails = false, shareFails = false;
  const dependencies = {
    signedIn: true, communityId: "group", isCurrent: (ticket: any) => controller.current(ticket),
    prepare: async (item: any) => ({ blob: item.file, name: item.file.name, mime: "application/pdf" }),
    put: async (id: string) => { events.push(`put:${id}`); },
    remove: async (id: string) => { events.push(`remove:${id}`); },
    readLocal: (id: string) => storedRows.get(id),
    saveLocal: (row: any) => { rows.add(row.id); storedRows.set(row.id, row); events.push("local"); },
    upload: async () => { events.push("upload"); if (uploadFails) throw new Error("upload"); },
    share: async () => { events.push("share"); if (shareFails) throw new Error("down"); },
  };
  return { controller, events, rows, storedRows, dependencies, failUpload: (value: boolean) => { uploadFails = value; }, failShare: (value: boolean) => { shareFails = value; } };
}

test("upload failure preserves saved blobs and retry reuses local row and files, before sharing", async () => {
  const f = fixture(); f.failUpload(true);
  const first = f.controller.begin()!;
  await assert.rejects(runHomeworkSave(first, f.dependencies), /upload/);
  assert.equal(first.operation.localSaved, true);
  assert.deepEqual(f.events.slice(1), ["local", "upload"]);
  f.controller.finish(first, "failure", false); f.failUpload(false);
  await runHomeworkSave(f.controller.begin()!, f.dependencies);
  assert.equal(f.rows.size, 1);
  assert.equal(f.events.filter(row => row === "local").length, 1);
  assert.equal(f.events.filter(row => row.startsWith("put:")).length, 1);
  assert.deepEqual(f.events.slice(-2), ["upload", "share"]);
  assert.equal(f.events.some(row => row.startsWith("remove:")), false);
});

test("share failure retains draft checkpoint; retry never uploads again", async () => {
  const f = fixture(); f.failShare(true); const ticket = f.controller.begin()!;
  const failed = await runHomeworkSave(ticket, f.dependencies);
  assert.equal(failed.success, false);
  f.controller.finish(ticket, failed.outcome.note, failed.success); f.failShare(false);
  assert.equal((await runHomeworkSave(f.controller.begin()!, f.dependencies)).success, true);
  assert.equal(f.events.filter(row => row === "upload").length, 1);
  assert.equal(f.events.filter(row => row === "share").length, 1);
  assert.equal(f.rows.size, 1);
});

test("a lost share response is checkpointed before the request and local retry cannot create another group copy", async () => {
  const f = fixture(); const first = f.controller.begin()!;
  const groupCopies: string[] = [];
  const dependencies = { ...f.dependencies, share: async () => {
    assert.equal(first.operation.shareAttempted, true);
    groupCopies.push("server-created-copy");
    throw new TypeError("Response lost after server commit");
  } };
  const result = await runHomeworkSave(first, dependencies);
  assert.equal(first.operation.shareAttempted, true);
  assert.equal(result.success, false);
  assert.match(result.outcome.note, /сохранено.*Проверьте/s);
  f.controller.finish(first, result.outcome.note, result.success);
  const retry = await runHomeworkSave(f.controller.begin()!, dependencies);
  assert.equal(retry.success, true);
  assert.equal(retry.outcome.sent, false);
  assert.match(retry.outcome.note, /Проверьте/);
  assert.equal(groupCopies.length, 1);
  assert.equal(f.rows.size, 1);
  assert.equal(f.events.filter(event => event === "local").length, 1);
});

test("updated server retry reuses one operation and rejects changed publication", async () => {
  const f = fixture();
  const attempts: string[] = [];
  const deadlines: (string | null)[] = [];
  let first = true;
  let dueCalls = 0;
  const dependencies = { ...f.dependencies, audienceSupported: true, personalDue: () => { dueCalls++; return dueCalls === 1 ? "2026-10-01T12:00:00.000Z" : "2026-10-08T12:00:00.000Z"; }, share: async (_title: string, _body: string, deadline: string | null, _topic: string | null, _audience: unknown, operationId: string) => {
    attempts.push(operationId);
    deadlines.push(deadline);
    if (first) { first = false; throw new TypeError("lost response"); }
  } };
  const initial = f.controller.begin()!;
  const uncertain = await runHomeworkSave(initial, dependencies);
  assert.equal(uncertain.success, false);
  f.controller.finish(initial, uncertain.outcome.note, false);
  f.controller.field("text", "Новая версия");
  const changed = f.controller.begin()!;
  await assert.rejects(runHomeworkSave(changed, dependencies), /share-changed/);
  f.controller.finish(changed, "Сохраните прежний текст", false);
  f.controller.field("text", "Задачи");
  const retried = await runHomeworkSave(f.controller.begin()!, dependencies);
  assert.equal(retried.outcome.sent, true);
  assert.deepEqual(attempts, [initial.operation.id, initial.operation.id]);
  assert.deepEqual(deadlines, ["2026-10-01T12:00:00.000Z", "2026-10-01T12:00:00.000Z"]);
  assert.equal(dueCalls, 1);
  assert.equal(f.events.filter(row => row === "upload").length, 1);
});

test("a failed local write cleans every prepared blob including the last one", async () => {
  const f = fixture();
  await assert.rejects(runHomeworkSave(f.controller.begin()!, { ...f.dependencies, saveLocal: () => { throw new Error("disk"); } }), /disk/);
  assert.equal(f.events.filter(row => row.startsWith("remove:")).length, 1);
  assert.equal(f.events.some(row => row === "upload"), false);
});

test("scope change during preparation cleans blob and blocks local/upload/share effects", async () => {
  const f = fixture();
  await assert.rejects(runHomeworkSave(f.controller.begin()!, { ...f.dependencies, put: async (id: string) => { f.events.push(`put:${id}`); f.controller.scope("new:g"); } }), /scope/);
  assert.equal(f.rows.size, 0);
  assert.equal(f.events.filter(row => row.startsWith("remove:")).length, 1);
  assert.equal(f.events.some(row => row === "upload" || row === "share"), false);
});

test("all HTTP failures are rejected; 413 is quota and success is accepted", () => {
  assert.throws(() => checkHomeworkUpload({ ok: false, status: 413 }), /quota/);
  for (const status of [401, 403, 500, 502]) assert.throws(() => checkHomeworkUpload({ ok: false, status }), /upload/);
  assert.doesNotThrow(() => checkHomeworkUpload({ ok: true, status: 201 }));
});

test("identity change during upload keeps committed blobs but never sends or clears the new draft", async () => {
  const f = fixture(); const ticket = f.controller.begin()!;
  await assert.rejects(runHomeworkSave(ticket, { ...f.dependencies, upload: async () => { f.controller.scope("new:g"); f.controller.field("text", "Новое задание"); } }), /scope/);
  f.controller.finish(ticket, "Старый результат", true);
  assert.equal(f.controller.draft.text, "Новое задание");
  assert.equal(f.events.some(row => row.startsWith("remove:") || row === "share"), false);
});

test("guest save keeps files offline and never calls upload or share", async () => {
  const f = fixture(); const ticket = f.controller.begin()!;
  const result = await runHomeworkSave(ticket, { ...f.dependencies, signedIn: false });
  assert.equal(result.success, true);
  assert.equal(f.rows.size, 1);
  assert.equal(f.events.some(row => row === "upload" || row === "share"), false);
});

test("retry reads live completion after attachment awaits and preserves a task marked done elsewhere", async () => {
  const f = fixture(); f.failUpload(true); const first = f.controller.begin()!;
  await assert.rejects(runHomeworkSave(first, f.dependencies), /upload/);
  f.controller.finish(first, "upload failed", false); f.failUpload(false);
  f.controller.field("text", "Изменённое задание");
  f.controller.field("nth", 2);
  f.controller.field("pending", [...f.controller.draft.pending, { file: new File(["new"], "new.pdf"), kind: "document" }]);
  const result = await runHomeworkSave(f.controller.begin()!, { ...f.dependencies, prepare: async (item: any) => {
    // Represents a Summary action while the routed editor is unmounted and processing a file.
    const existing = f.storedRows.get(first.operation.id);
    f.storedRows.set(first.operation.id, { ...existing, done: true });
    return { blob: item.file, name: item.file.name, mime: "application/pdf" };
  } });
  assert.equal(result.success, true);
  assert.equal(f.rows.size, 1);
  const saved = f.storedRows.get(first.operation.id);
  assert.equal(saved.done, true);
  assert.equal(saved.text, "Изменённое задание");
  assert.equal(saved.targetNthOccurrence, 2);
  assert.equal(saved.files.length, 2);
});
