import assert from "node:assert/strict";
import { test } from "node:test";
import { makePublicationBatch, publishHomeworkBatch, PublicationMemory } from "./homework-publication-batch.ts";
const task=(id:string):any=>({id,subject:" Математика ",text:" Решить ",done:false,created:"2026-10-01",targetNthOccurrence:1,files:[{id:"never-upload"}]});
const audience={kind:"selected" as const,roleIds:["role"],userIds:["user"]};
test("batch snapshot fixes audience, Moscow end-of-day deadline and unique per-row IDs without attachments or readiness",()=>{
  let serial=0;const selection={...audience,roleIds:[...audience.roleIds]};const batch=makePublicationBatch([task("a"),task("b")],selection,()=>"2026-10-05",()=>`operation-${++serial}`);selection.roleIds.push("changed");
  assert.equal(batch.rows[0].payload.deadlineAt,"2026-10-05T20:59:59.000Z");assert.equal(batch.rows[0].payload.title,"Математика");assert.deepEqual(batch.audience.roleIds,["role"]);assert.notEqual(batch.rows[0].operationId,batch.rows[1].operationId);assert.equal("files" in batch.rows[0].payload,false);assert.equal("done" in batch.rows[0].payload,false);
  assert.equal(makePublicationBatch([task("a"),task("a")],audience,()=>null).rows.length,1);
});
test("uncertain retry replays the immutable payload and ID, never already-confirmed rows",async()=>{
  let serial=0,fail=true;const tasks=new Map([task("a"),task("b")].map(row=>[row.id,row]));const batch=makePublicationBatch([...tasks.values()],audience,()=>null,()=>`op-${++serial}`);const calls:any[]=[];
  const actions={current:()=>true,read:(id:string)=>tasks.get(id),dateOf:()=>null,onChange:()=>{},send:async(row:any)=>{calls.push(JSON.parse(JSON.stringify(row)));if(row.sourceId==="b"&&fail)throw Error("network");}};
  await publishHomeworkBatch(batch,actions);assert.equal(batch.rows[0].status,"published");assert.equal(batch.rows[1].status,"uncertain");tasks.set("b",{...task("b"),text:"Новая редакция"});fail=false;await publishHomeworkBatch(batch,actions);assert.equal(calls.length,3);assert.deepEqual(calls[1].payload,calls[2].payload);assert.equal(calls[1].operationId,calls[2].operationId);assert.equal(batch.rows[1].status,"published");
});
test("changed local source blocks first send and profile change stops the next publication",async()=>{
  const batch=makePublicationBatch([task("a"),task("b"),task("c")],audience,()=>null,()=>crypto.randomUUID());let current=true;const sent:string[]=[];
  await publishHomeworkBatch(batch,{current:()=>current,read:id=>id==="a"?{...task(id),done:true}:task(id),dateOf:()=>null,onChange:()=>{},send:async row=>{sent.push(row.sourceId);current=false;}});assert.deepEqual(sent,["b"]);assert.equal(batch.rows[0].attempted,false);assert.equal(batch.rows[1].status,"uncertain");assert.equal(batch.rows[2].attempted,false);
});
test("in-memory publication retries survive route remount but are not exposed to another session profile",()=>{
  const memory=new PublicationMemory();memory.profile("a");const batch=makePublicationBatch([task("a")],audience,()=>null,()=>"id");memory.set("a","group",batch);assert.equal(memory.get("a","group"),batch);assert.equal(memory.get("b","group"),null);memory.profile("b");memory.profile("a");assert.equal(memory.get("a","group"),null);
});
