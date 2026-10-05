import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { lessonMapContext, routeClassroom } from "./route-context.ts";
import { parseCampusGraph, resolveCampusClassroom } from "./campus-routing.ts";
import { teacherWeek } from "./teachers.ts";

const graph=parseCampusGraph(JSON.parse(readFileSync(new URL("../../src/Vograph.Desktop/Assets/maps/campus-graph.json",import.meta.url),"utf8")));
for(const [raw,room,building] of [["320*;","320","УЛК"],["101;","101","ГК"],["ВЦ 243","243","ВЦ"]])test(`lesson URL preserves canonical route destination ${raw}`,()=>{
  const params=new URLSearchParams(lessonMapContext({classroomRaw:raw,roomRaw:room,buildingRaw:building}));
  const destination=routeClassroom(params.get("classroom"),params.get("room"),params.get("building"));
  assert.equal(destination,raw);assert.equal(resolveCampusClassroom(graph,destination)?.id,resolveCampusClassroom(graph,raw)?.id);assert.ok(resolveCampusClassroom(graph,destination));
});
test("teacher display caption does not replace raw classroom in route link",()=>{
  const row=teacherWeek([{dayOfWeek:1,timeStart:"09:00",parity:0,classroomRaw:"320*;",roomRaw:"320",buildingRaw:"УЛК"}],0,"g","Группа")[0].rows[0];
  assert.equal(row.room,"320 УЛК");const params=new URLSearchParams(lessonMapContext(row));assert.equal(routeClassroom(params.get("classroom"),params.get("room"),params.get("building")),"320*;");
});
test("legacy explicit building captions are safe while ambiguous or unknown rooms stay unresolved",()=>{
  assert.equal(resolveCampusClassroom(graph,routeClassroom(null,"320 УЛК",null))?.building,"УЛК");
  assert.equal(resolveCampusClassroom(graph,routeClassroom(null,"243","ВЦ"))?.building,"ГК");
  assert.equal(routeClassroom(null,"320",null),"");
  assert.equal(routeClassroom(null,"320","Неизвестный корпус"),"");
  assert.equal(routeClassroom(null,"320 УЛК","ГК"),"");
  assert.equal(resolveCampusClassroom(graph,routeClassroom("Неизвестная аудитория",null,null)),null);
});
