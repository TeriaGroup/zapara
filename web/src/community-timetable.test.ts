import assert from "node:assert/strict";
import { test } from "node:test";
import { createCommunityTimetableLoader, resolveAcademicGroup, timetableSubjects, type CommunityTimetableState } from "./community-timetable.ts";
import type { GroupsPayload, TimetablePayload } from "./types";
const catalog:GroupsPayload={period:{start:"2026-09-01",weekCount:2,title:"Осень",timeZone:"Europe/Moscow"},meta:{snapshotId:"s",fetchedAt:"2026-09-26T10:00:00Z",publishedAt:"2026-09-26T10:00:00Z",stale:false,sourceKind:"test"},groups:[{id:"personal",name:"Н111",lessonCount:1},{id:"community",name:"Н162С",lessonCount:1}]};
const payload=(id:string,subject:string):TimetablePayload=>({...catalog,group:catalog.groups.find(group=>group.id===id)!,lessons:[{dayOfWeek:1,parity:0,index:1,timeStart:"09:00",timeEnd:"10:30",subjectRaw:subject,subjectNormalized:subject,typeRaw:"Лекция",teacherRaw:null,classroomRaw:null,roomRaw:null,buildingRaw:null}]});
test("community timetable and subject options do not use personal selection",async()=>{
    const states:CommunityTimetableState[]=[];const requested:string[]=[];const saved:string[]=[];
    const loader=createCommunityTimetableLoader(" н162с ",{catalog,loadGroups:async()=>{throw new Error("cached catalog is sufficient");},loadTimetable:async id=>{requested.push(id);return payload(id,"Предмет другой группы");},cached:id=>id==="personal"?payload(id,"Мой предмет"):null,save:id=>saved.push(id)},state=>states.push(state));
    await loader.reload();assert.deepEqual(requested,["community"]);assert.deepEqual(saved,["community"]);assert.deepEqual(timetableSubjects(states.at(-1)!.payload),["Предмет другой группы"]);
});
test("transient refresh failure preserves the matching community last-good",async()=>{
    const states:CommunityTimetableState[]=[];const last=payload("community","Последняя успешная копия");
    const loader=createCommunityTimetableLoader("Н162С",{catalog,loadGroups:async()=>catalog,loadTimetable:async()=>{throw new Error("offline");},cached:id=>id==="community"?last:null,save:()=>{throw new Error("failed fetch cannot save");}},state=>states.push(state));
    await loader.reload();assert.equal(states[0].payload,last);assert.equal(states.at(-1)!.payload,last);assert.equal(states.at(-1)!.loading,false);assert.match(states.at(-1)!.error,/сохранённая копия группы/);
});
test("catalog lookup validates response identity and never substitutes personal data",async()=>{
    const states:CommunityTimetableState[]=[];let lookups=0;
    const loader=createCommunityTimetableLoader("Н162С",{catalog:{...catalog,groups:[catalog.groups[0]]},loadGroups:async()=>{lookups++;return catalog;},loadTimetable:async()=>payload("personal","Нельзя показывать"),cached:()=>null,save:()=>{throw new Error("wrong group cannot save");}},state=>states.push(state));
    await loader.reload();assert.equal(lookups,1);assert.equal(states.at(-1)!.payload,null);assert.equal(states.at(-1)!.groupId,"community");assert.ok(states.at(-1)!.error);
    assert.equal(resolveAcademicGroup("Н162С",[catalog.groups[1],{...catalog.groups[1],id:"duplicate"}]),null);
});
test("superseded and disposed loads abort and cannot publish or save stale responses",async()=>{
    const states:CommunityTimetableState[]=[];const signals:AbortSignal[]=[];const saved:TimetablePayload[]=[];const replies:((value:TimetablePayload)=>void)[]=[];
    const loader=createCommunityTimetableLoader("Н162С",{catalog,loadGroups:async()=>catalog,loadTimetable:(_id,signal)=>{signals.push(signal);return new Promise(resolve=>replies.push(resolve));},cached:()=>null,save:(_id,value)=>saved.push(value)},state=>states.push(state));
    const first=loader.reload();const second=loader.reload();assert.equal(signals[0].aborted,true);replies[1](payload("community","Новое"));await second;const count=states.length;replies[0](payload("community","Старое"));await first;assert.equal(states.length,count);assert.deepEqual(timetableSubjects(saved[0]),["Новое"]);
    const third=loader.reload();loader.dispose();assert.equal(signals[2].aborted,true);const disposedCount=states.length;replies[2](payload("community","После закрытия"));await third;assert.equal(states.length,disposedCount);assert.equal(saved.length,1);
});
