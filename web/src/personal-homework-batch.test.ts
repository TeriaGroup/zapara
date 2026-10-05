import assert from "node:assert/strict";
import { test } from "node:test";
import { completePersonalBatch, duplicatePersonalHomework } from "./personal-homework-batch.ts";

const item=(id:string,done=false):any=>({id,subject:"Математика",text:"Решить задачи",done,created:"2026-10-01",files:[{id:`blob-${id}`,name:"Файл.pdf"}]});
test("bulk completion reports partial failures, skips deleted/done rows and preserves each current task's fields",async()=>{
  const rows=new Map([item("ok"),item("fail"),item("done",true)].map(row=>[row.id,row]));const writes:any[]=[];
  const result=await completePersonalBatch(["ok","fail","gone","done","ok"],{current:()=>true,read:id=>rows.get(id),save:row=>{if(row.id==="fail")throw Error("storage full");writes.push(row);rows.set(row.id,row);}});
  assert.deepEqual(result.saved,["ok"]);assert.deepEqual(result.failed,["fail"]);assert.deepEqual(result.skipped,["gone","done"]);assert.equal(writes.length,1);assert.equal(writes[0].files[0].id,"blob-ok");assert.equal(writes[0].text,"Решить задачи");assert.equal(writes[0].done,true);
});
test("bulk completion stops before the next task after scope change and uses latest task data",async()=>{
  let scope=true;const writes:any[]=[];const latest={...item("a"),text:"Сохранённая новая редакция"};
  const result=await completePersonalBatch(["a","b"],{current:()=>scope,read:id=>id==="a"?latest:item(id),save:row=>{writes.push(row);},checkpoint:async()=>{scope=false;}});
  assert.equal(result.stopped,true);assert.deepEqual(writes.map(row=>row.id),["a"]);assert.equal(writes[0].text,latest.text);
});
test("duplicate confirmation matches canonical subject, trimmed case-insensitive text and the actual nullable day",()=>{
  const rows=[{...item("done",true),subject:"  МАТЕМАТИКА  ",text:" Решить ЗАДАЧИ "}];
  assert.equal(duplicatePersonalHomework(rows,{subject:"Математика",text:"решить задачи"},"2026-10-05",()=>"2026-10-05")?.id,"done");
  assert.equal(duplicatePersonalHomework(rows,{subject:"Математика",text:"решить  задачи"},"2026-10-05",()=>"2026-10-05"),undefined);
  assert.equal(duplicatePersonalHomework(rows,{subject:"Математика",text:"решить задачи"},"2026-10-06",()=>"2026-10-05"),undefined);
  assert.equal(duplicatePersonalHomework(rows,{subject:"Математика",text:"решить задачи"},null,()=>null)?.id,"done");
});
