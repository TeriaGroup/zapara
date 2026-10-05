import assert from "node:assert/strict";
import { test } from "node:test";
import { browseHomework, homeworkEmptyKind } from "./homework-browse.ts";
import type { GroupHomeworkCopy, HomeworkItem } from "./types.ts";
test("deadline/source/file facets compose and keep stable original totals", () => {
  const personal:HomeworkItem[]=[{id:"late",subject:"Мат",text:"Лист",done:false,created:"2026-09-01",deadlineAt:"2026-10-01T08:00:00Z",files:[{id:"f",kind:"document",name:"Задачи.pdf",mime:"application/pdf"}]},{id:"none",subject:"Ист",text:"Прочитать",done:false,created:"2026-09-01"}];
  const result=browseHomework(personal,[],{subject:null,query:"задачи.pdf",status:"all",target:null,source:"personal",deadline:"overdue",onlyFiles:true,now:new Date("2026-10-01T12:00:00Z")});
  assert.deepEqual(result.local.map(row=>row.id),["late"]);
  assert.equal(result.total,2);
});

const local = [
  { id: "open", subject: "Математика", text: "Решить пример", done: false, created: "2026-09-01" },
  { id: "done", subject: "Математика", text: "Прочитать главу", done: true, created: "2026-09-01" },
] satisfies HomeworkItem[];
const shared = [
  { homeworkId: "shared-done", title: "Математика", body: "Групповой доклад", completed: true, revision: 1, completionRevision: 1 },
  { homeworkId: "shared-open", title: "Физика", body: "Решить задачи", completed: false, revision: 1, completionRevision: 0 },
] satisfies GroupHomeworkCopy[];

test("searches subject and task text across personal and shared homework with stable total", () => {
  const result = browseHomework(local, shared, { subject: null, query: "  ГРУППОВОЙ   ДОКЛАД  ", status: "all", target: null });
  assert.deepEqual(result.copies.map(item => item.homeworkId), ["shared-done"]);
  assert.equal(result.shown, 1);
  assert.equal(result.total, 4);
  assert.deepEqual(browseHomework(local, shared, { subject: null, query: "МАТЕ", status: "active", target: null }).local.map(item => item.id), ["open"]);
});

test("a requested completed task stays visible despite active, subject and search filters", () => {
  const options = { subject: "Физика", query: "ничего", status: "active" as const };
  assert.deepEqual(browseHomework(local, shared, { ...options, target: { kind: "local", id: "done" } }).local.map(item => item.id), ["done"]);
  assert.deepEqual(browseHomework(local, shared, { ...options, target: { kind: "shared", id: "shared-done" } }).copies.map(item => item.homeworkId), ["shared-done"]);
  assert.equal(browseHomework(local, shared, { ...options, target: { kind: "shared", id: "missing" } }).shown, 0);
});

test("empty state gives a missing deep link priority over zero or filtered results", () => {
  assert.equal(homeworkEmptyKind(0, 0, true), "missing");
  assert.equal(homeworkEmptyKind(0, 0, false), "empty");
  assert.equal(homeworkEmptyKind(4, 0, false), "filtered");
  assert.equal(homeworkEmptyKind(4, 1, true), null);
});
