import assert from "node:assert/strict";
import { test } from "node:test";
import { answerDraftChanged, ballotDraftProblem, copyFormQuestion, chooseBuildingPlan, safeAppReturn, dueBucket, browseForms, browseDevices, summaryRows, noteSearch, dayLoad, choiceCounts } from "./ux300.ts";

test("answer comparison ignores ordering but distinguishes a cleared optional answer", () => {
  const saved = [{ questionId: "a", text: null, choices: ["Пн", "Вт"] }];
  assert.equal(answerDraftChanged([{ questionId: "a", text: "", choices: ["Вт", "Пн"] }], saved), false);
  assert.equal(answerDraftChanged([{ questionId: "a", text: null, choices: [] }], saved), true);
});
test("question duplication keeps the original identity and isolates options", () => {
  const original = [{ questionId: "one", title: "Выбор", kind: "singleChoice" as const, required: true, options: ["Да", "Нет"] }];
  const copy = copyFormQuestion(original, "one", "two", 20);
  assert.deepEqual(copy.map(q => q.questionId), ["one", "two"]);
  copy[1].options.push("Позже");
  assert.equal(original[0].options.length, 2);
  assert.equal(copyFormQuestion(original, "one", "one", 20), original);
  assert.equal(copyFormQuestion(original, "one", "two", 1), original);
});
test("ballot validation rejects incomplete and normalized duplicate options", () => {
  assert.match(ballotDraftProblem("Когда?", ["Утром", " утром "]), /повтор/);
  assert.match(ballotDraftProblem("Когда?", ["Утром", ""]), /вариант/);
  assert.equal(ballotDraftProblem("Когда?", ["Утром", "Вечером"]), "");
});
test("building change keeps the floor and never invents a plan", () => {
  const plans = [{ id: "a1", building: "ГК", floor: 1, url: "a" }, { id: "a4", building: "ГК", floor: 4, url: "b" }];
  assert.equal(chooseBuildingPlan(plans, "ГК", 4)?.id, "a4");
  assert.equal(chooseBuildingPlan(plans, "ГК", 3)?.id, "a4");
  assert.equal(chooseBuildingPlan(plans, "УЛК", 4), null);
});
test("return destinations retain useful context without allowing external redirects", () => {
  assert.equal(safeAppReturn("/homework?subject=Мат#task"), "/homework?subject=Мат#task");
  for (const bad of ["//other.test", "https://other.test", "/unknown", "/\\other.test", "javascript:alert(1)"]) assert.equal(safeAppReturn(bad), "/schedule");
});
test("deadline boundaries use the local calendar and invalid dates stay unknown", () => {
  const now = new Date(2026, 9, 1, 12);
  assert.equal(dueBucket(new Date(2026, 9, 1, 11).toISOString(), now), "overdue");
  assert.equal(dueBucket(new Date(2026, 9, 1, 18).toISOString(), now), "today");
  assert.equal(dueBucket(null, now), "none");
  assert.equal(dueBucket("bad", now), "none");
});
test("answer-needed forms exclude closed and answered records", () => {
  const rows = [ { formId:"a",title:"Учёба",description:"Планы",canRespond:true,ownResponse:null }, { formId:"b",title:"Учёба",description:"Планы",canRespond:false,ownResponse:null }, { formId:"c",title:"Учёба",description:"Планы",canRespond:true,ownResponse:{answers:[]} } ];
  assert.deepEqual(browseForms(rows, "УЧЕБА планы", "needed").map(x=>x.formId), ["a"]);
});
test("device filtering cannot mistake the current device for another session", () => {
  const rows = [{familyId:"a",deviceName:"Телефон",platform:"android",isCurrent:true,lastSeenAt:"2026-10-01"}, {familyId:"b",deviceName:"Рабочий ПК",platform:"windows",isCurrent:false,lastSeenAt:"2026-09-30"}];
  assert.deepEqual(browseDevices(rows,"ПК windows",true).map(x=>x.familyId),["b"]);
  assert.equal(browseDevices(rows,"Телефон",true).length,0);
});
test("summary search keeps stable count ties and Russian normalization", () => {
  const rows=[{name:"Зачёт",count:3},{name:"Лекция",count:3},{name:"Практика",count:1}];
  assert.deepEqual(summaryRows(rows,"","count"),rows);
  assert.equal(summaryRows(rows,"зачет","name")[0].name,"Зачёт");
  assert.equal(noteSearch("иванов 312", "312", "Иванов"),true);
});
test("day load merges simultaneous subgroup rows rather than doubling time", () => {
  assert.deepEqual(dayLoad([{timeStart:"09:00",timeEnd:"10:35"},{timeStart:"09:00",timeEnd:"10:35"},{timeStart:"10:45",timeEnd:"12:20"},{timeStart:"bad",timeEnd:"12:00"}]), {minutes:190, span:200, gaps:10});
});
test("choice counts aggregate only loaded answers without leaking anonymous identity", () => {
  const result = choiceCounts([{questionId:"q",title:"День",kind:"multipleChoice",required:false,options:["Пн","Вт"]}], [{respondentId:null,updatedAt:"",answers:[{questionId:"q",text:null,choices:["Пн","Пн"]}]}]);
  assert.deepEqual(result,[{title:"День",options:[{label:"Пн",count:1},{label:"Вт",count:0}],loaded:1}]);
});
