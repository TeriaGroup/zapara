import assert from "node:assert/strict";
import { test } from "node:test";
import { scheduleCalendarEntries, schedulePlainText } from "./schedule-export.ts";
import type { Lesson } from "./types.ts";
const lesson:Lesson={dayOfWeek:4,parity:1,index:1,timeStart:"09:00",timeEnd:"10:35",subjectRaw:"Математика",subjectNormalized:"математика",typeRaw:"лекция",teacherRaw:"Петров",classroomRaw:"320*;",roomRaw:"320",buildingRaw:"УЛК"};
test("export uses each actual selected date and canonical raw identity",()=>{
 const days=[{date:new Date(2026,9,1),lessons:[lesson]},{date:new Date(2026,9,2),lessons:[lesson]}];const rows=scheduleCalendarEntries(days,"3313");
 assert.equal(rows[0].start,"2026-10-01T06:00:00.000Z");assert.equal(rows[1].start,"2026-10-02T06:00:00.000Z");assert.equal(rows[0].id,rows[1].id);assert.match(rows[0].id,/320\*;/);
 assert.match(schedulePlainText(days,"А863С"),/09:00–10:35/);assert.match(schedulePlainText(days,"А863С"),/Петров/);
});
test("unknown timetable days are not presented as free days in exported text",()=>{const days=[{date:new Date(2026,8,1),lessons:[],known:false}];assert.match(schedulePlainText(days,"Группа"),/нет данных/);assert.equal(scheduleCalendarEntries(days,"g").length,0);});
