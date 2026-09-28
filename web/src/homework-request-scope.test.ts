import assert from "node:assert/strict";
import { test } from "node:test";
import { HomeworkRequestScope, loadScopedHomework, scopedValue, subjectHomework } from "./homework-request-scope.ts";
import type { GroupHomeworkCopy } from "./types.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}

test("late 409 reload cannot publish another account's homework or clear its active operation", async () => {
  const guard = new HomeworkRequestScope();
  guard.scope("owner-a/group/community-a");
  const oldAction = guard.begin();
  const response = deferred<GroupHomeworkCopy[]>();
  const seen: string[] = [];
  const reload = loadScopedHomework(guard, oldAction, () => response.promise, rows => seen.push(rows[0].homeworkId));
  guard.scope("owner-b/group/community-b");
  const currentAction = guard.begin();
  response.resolve([{ homeworkId: "old-private-copy" } as GroupHomeworkCopy]);
  await reload;
  assert.deepEqual(seen, []);
  assert.equal(guard.active(oldAction), false);
  assert.equal(guard.active(currentAction), true);
  assert.deepEqual(scopedValue(["old-private-copy"], "owner-a", "owner-b", []), []);
});

test("subject conflict reload keeps other subjects out and ignores old topic responses", async () => {
  const rows = [{ homeworkId: "math", title: "Математика" }, { homeworkId: "physics", title: "Физика" }] as GroupHomeworkCopy[];
  assert.deepEqual(subjectHomework(rows, "Математика").map(row => row.homeworkId), ["math"]);
  const guard = new HomeworkRequestScope();
  guard.scope("owner/community/topic-math");
  const old = guard.capture();
  const request = deferred<GroupHomeworkCopy[]>();
  const seen: string[][] = [];
  const pending = loadScopedHomework(guard, old, () => request.promise, value => seen.push(subjectHomework(value, "Математика").map(row => row.homeworkId)));
  guard.scope("owner/community/topic-physics");
  request.resolve(rows);
  await pending;
  assert.deepEqual(seen, []);
});
