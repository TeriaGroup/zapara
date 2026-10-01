import test from "node:test";import assert from "node:assert/strict";
import {editPersonalHomework,overlapPairs,nextTeacherDay,matchesWords,subgroupUndoCurrent} from "./next-workflows.ts";
test("personal edit keeps identity creation completion and attachments",()=>{const row={id:"1",created:"2026-10-01T00:00:00Z",done:true,subject:"A",text:"Old",files:[{id:"f",name:"a",mime:"text/plain",kind:"document" as const}]};assert.deepEqual(editPersonalHomework(row,{subject:" B ",text:" New ",nth:2}),{...row,subject:"B",text:"New",targetNthOccurrence:2});});
test("personal editor rejects unsynchronizable text without changing the saved task",()=>{
  const row={id:"1",created:"2026-10-01T00:00:00Z",done:false,subject:"A",text:"Old"};
  assert.throws(()=>editPersonalHomework(row,{subject:"x".repeat(257),text:"New",nth:1}));
  assert.throws(()=>editPersonalHomework(row,{subject:"A",text:"x".repeat(4001),nth:1}));
  assert.throws(()=>editPersonalHomework(row,{subject:"A",text:"bad\u0000text",nth:1}));
  assert.throws(()=>editPersonalHomework(row,{subject:"A",text:"bad\ud800text",nth:1}));
  const emoji="😀".repeat(4000);
  assert.equal(editPersonalHomework(row,{subject:"A",text:emoji,nth:1}).text,emoji);
  assert.equal(row.text,"Old");
});
test("overlaps report exact pairs but not adjacent or malformed lessons",()=>{assert.deepEqual(overlapPairs([{subjectRaw:"A",timeStart:"09:00",timeEnd:"10:00"},{subjectRaw:"B",timeStart:"09:30",timeEnd:"11:00"},{subjectRaw:"C",timeStart:"11:00",timeEnd:"12:00"}]),[{left:0,right:1}]);});
test("AND search is normalized and word-order independent",()=>{assert.equal(matchesWords("физика иванов","Иванов Иван","ФИЗИКА"),true);assert.equal(matchesWords("иванов химия","Иванов","Физика"),false);});
test("teacher target follows effective parity over year boundary",()=>{const date=nextTeacherDay(new Date(2026,11,31),1,0,{start:"2026-09-01",weekCount:2,title:"",timeZone:""},false)!;assert.equal(date.getFullYear(),2027);assert.equal(date.getMonth(),0);assert.equal(date.getDate(),4);});
test("subgroup undo is conditional on owner group and unchanged submitted value",()=>{const u={scope:"A",group:"g",stream:"s",before:undefined,after:"b"};assert.equal(subgroupUndoCurrent(u,"A","g",{s:"b"}),true);assert.equal(subgroupUndoCurrent(u,"B","g",{s:"b"}),false);assert.equal(subgroupUndoCurrent(u,"A","g",{s:"c"}),false);});
