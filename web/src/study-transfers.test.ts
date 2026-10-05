import assert from "node:assert/strict";
import { test } from "node:test";
import { lessonTransfers } from "./study-transfers.ts";
import { parseCampusGraph } from "./campus-routing.ts";
const graph=parseCampusGraph({version:1,buildings:["ГК"],nodes:[{id:"a",kind:"room",room:"101",building:"ГК",floor:1,x:0,y:0},{id:"b",kind:"room",room:"102",building:"ГК",floor:1,x:1,y:0}],edges:[{from:"a",to:"b",kind:"walk",seconds:400,oneWay:false}],blocked:[]});
const first:any={subjectRaw:"Первый",timeStart:"09:00",timeEnd:"10:00",classroomRaw:"101;"};const next:any={subjectRaw:"Второй",timeStart:"10:05",timeEnd:"11:00",classroomRaw:"102;"};
test("actual authored seconds compare against the real break, with unknown rooms never marked as fitting",()=>{
  const tight=lessonTransfers([next,first],graph)[0];assert.equal(tight.status,"tight");assert.equal(tight.availableSeconds,300);assert.equal(tight.routeSeconds,400);
  assert.equal(lessonTransfers([first,{...next,timeStart:"10:10"}],graph)[0].status,"fits");
  assert.equal(lessonTransfers([first,{...next,classroomRaw:"дистанционно"}],graph)[0].status,"unknown");
});
test("overlapping alternative lessons do not invent a feasible transfer from one arbitrary room",()=>{
  const rows=lessonTransfers([first,{...first,classroomRaw:"102;"},{...next,timeStart:"10:30"}],graph);assert.equal(rows[0].status,"overlap");assert.equal(rows[1].status,"unknown");
});
test("valid single-digit hours are compared chronologically rather than lexicographically",()=>{
  const row=lessonTransfers([next,{...first,timeStart:"9:00"}],graph)[0];assert.equal(row.previous.subjectRaw,"Первый");assert.equal(row.status,"tight");
});
