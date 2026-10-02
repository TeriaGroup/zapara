import assert from "node:assert/strict";
import { test } from "node:test";
import { postponePreview, applyPostponement, undoPostponement } from "./homework-postpone.ts";
const item=(id:string,n=1):any=>({id,subject:"Мат",text:"Задача",created:"2026-10-01",done:false,targetNthOccurrence:n,files:[{id:`blob-${id}`}]});
const date=(row:any)=>row.id==="unknown"?null:`2026-10-${String(2+(row.targetNthOccurrence??1)).padStart(2,"0")}`;
test("postpone preview only admits active tasks below limit with a real later occurrence",()=>{
  const rows=postponePreview([item("ok"),item("limit",10),item("unknown"),{...item("done"),done:true}],date);
  assert.deepEqual(rows.map(row=>row.id),["ok"]);assert.equal(rows[0].beforeDay,"2026-10-03");assert.equal(rows[0].afterDay,"2026-10-04");
  assert.equal(postponePreview([item("same")],()=>"2026-10-03").length,0);
});
test("postpone uses original-state and recomputed-date CAS, preserves latest files and undoes only unchanged applied rows",async()=>{
  const map=new Map([item("ok"),item("changed"),item("clock")].map(row=>[row.id,row]));const preview=postponePreview([...map.values()],date);map.set("changed",{...map.get("changed"),text:"Новая редакция"});map.set("ok",{...map.get("ok"),files:[{id:"new-file"}]});
  const actions={current:()=>true,read:(id:string)=>map.get(id),dateOf:(row:any)=>row.id==="clock"?"2026-12-01":date(row),save:(row:any)=>{map.set(row.id,row);}};
  const result=await applyPostponement(preview,actions);assert.deepEqual(result.saved.map(row=>row.id),["ok"]);assert.deepEqual(result.failed,["changed","clock"]);assert.equal(map.get("ok").files[0].id,"new-file");assert.equal(map.get("ok").targetNthOccurrence,2);
  map.set("ok",{...map.get("ok"),done:true});const declined=await undoPostponement(result.saved,actions);assert.equal(declined.saved.length,0);assert.deepEqual(declined.failed,["ok"]);
  map.set("ok",{...map.get("ok"),done:false});const undone=await undoPostponement(result.saved,actions);assert.equal(undone.saved.length,1);assert.equal(map.get("ok").targetNthOccurrence,1);assert.equal(map.get("ok").files[0].id,"new-file");
});
test("postponement stops on profile change before touching another task",async()=>{
  const preview=postponePreview([item("a"),item("b")],date);let current=true;const written:string[]=[];
  const result=await applyPostponement(preview,{current:()=>current,read:item,dateOf:date,save:row=>{written.push(row.id);},checkpoint:async()=>{current=false;}});assert.deepEqual(written,["a"]);assert.equal(result.stopped,true);
});
