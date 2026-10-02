import assert from "node:assert/strict";
import { test } from "node:test";
import { homeworkCalendarEntries, homeworkPlanText } from "./homework-export.ts";

const rows:any[]=[{id:"one",subject:"Математика",text:"Решить 1–3",done:false,created:"",deadlineAt:new Date(2026,9,5).toISOString(),deadlinePrecision:"date",files:[{id:"private-blob",name:"Задачи.pdf",mime:"application/pdf",kind:"document"}]},{id:"two",subject:"История",text:"Прочитать",done:true,created:"",deadlineAt:null}];
test("homework calendar keeps inferred local day without guessed times and exposes missing dates to skip reporting",()=>{
  const entries=homeworkCalendarEntries(rows,"group");assert.equal(entries[0].day,"2026-10-05");assert.equal(entries[1].day,"");assert.match(entries[0].id,/^homework:/);assert.equal(homeworkCalendarEntries([{...rows[0],id:"other"}],"group")[0].id,entries[0].id);
  assert.equal(homeworkCalendarEntries([{...rows[0],deadlinePrecision:"instant"}],"group")[0].day,"");
});
test("plan exports exactly supplied personal rows and filenames, without local blob identifiers",()=>{
  const result=homeworkPlanText(rows.slice(0,1),"Группа");assert.match(result,/Решить 1–3/);assert.match(result,/Задачи.pdf/);assert.doesNotMatch(result,/История|private-blob/);assert.match(homeworkPlanText(rows,"Группа"),/Без известной даты/);
});
