import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";
import ts from "typescript";
const source=ts.transpileModule(await readFile(new URL("./private-sync.ts",import.meta.url),"utf8"),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
const item={id:"a",subject:"Предмет",text:"Старый текст",done:true,created:"2026-10-01T00:00:00Z",files:[{id:"blob"}]};
function harness(owner="owner",revision=0){
  const profile={owner,items:[item],records:revision?[{entityType:"homework",entityId:"a",revision,tombstone:false}]:[],pending:revision?[]:[{opId:"upsert",type:"homework",id:"a",revision:0,value:{}},{opId:"completion",type:"completion",id:"a",revision:0,value:{done:true}}]};
  const memory=new Map([[`zapara.private-homework.${owner}`,JSON.stringify(profile)]]);const slots:any[]=[];let cursor=0,rejectWrite=false,rejectRead=false;
  const react={useState:(initial:any)=>{const i=cursor++;if(!(i in slots))slots[i]=typeof initial==="function"?initial():initial;return[slots[i],(v:any)=>{slots[i]=typeof v==="function"?v(slots[i]):v;}];},useRef:(value:any)=>{const i=cursor++;if(!(i in slots))slots[i]={current:value};return slots[i];},useEffect:()=>{}};
  const context={exports:{} as any,require:(id:string)=>id==="react"?react:{canonicalUtc:String,syncSubjectKey:String},localStorage:{getItem:(key:string)=>{if(rejectRead)throw Error("unreadable");return memory.get(key)||null;},setItem:(key:string,value:string)=>{if(rejectWrite)throw Error("full");memory.set(key,value);}},crypto:{randomUUID:()=>"delete-op"}};
  runInNewContext(source,context);
  return{render:(next=owner)=>{cursor=0;return context.exports.usePrivateHomework(next);},data:()=>JSON.parse(memory.get(`zapara.private-homework.${owner}`)!),writeFail:(v:boolean)=>{rejectWrite=v;},readFail:(v:boolean)=>{rejectRead=v;},inflight:()=>slots.find(x=>x?.current instanceof Set)?.current ?? slots.find(x=>Object.prototype.toString.call(x?.current)==="[object Set]")?.current};
}
test("queued never-acked personal delete cancels both operations without R0 delete",()=>{const h=harness();h.render().remove("a");assert.deepEqual(h.data().items,[]);assert.deepEqual(h.data().pending,[]);});
test("acknowledged personal delete uses exact ID and revision and retains recovery metadata",()=>{const h=harness("owner",7);h.render().remove("a");const op=h.data().pending[0];assert.equal(op.action,"delete");assert.equal(op.id,"a");assert.equal(op.revision,7);assert.equal(op.value,null);assert.deepEqual(op.deletedItem,item);});
test("storage failure and old owner callback never delete task",()=>{const h=harness();const old=h.render();h.writeFail(true);assert.throws(()=>old.remove("a"));assert.equal(h.data().items.length,1);h.writeFail(false);h.render("other");assert.throws(()=>old.remove("a"));assert.equal(h.data().items.length,1);});
test("inflight never-acked task is retained with retry feedback",()=>{const h=harness();const hook=h.render();h.inflight().add("owner:upsert");assert.throws(()=>hook.remove("a"),/синхронизируется/);assert.equal(h.data().pending.length,2);assert.equal(h.data().items.length,1);});
test("unreadable cache is not empty success and retry restores its tasks",()=>{const h=harness();h.readFail(true);let hook=h.render();assert.equal(hook.readFailed,true);assert.equal(hook.ready,false);h.readFail(false);hook.retryRead();hook=h.render();assert.equal(hook.readFailed,false);assert.equal(hook.items.length,1);});
