import assert from "node:assert/strict";
import { test } from "node:test";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";
import ts from "typescript";
const source=ts.transpileModule(await readFile(new URL("./private-sync.ts",import.meta.url),"utf8"),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
const row=(id:string)=>({entityType:"homework",entityId:id,revision:id==="A"?1:2,tombstone:false,changedAt:"2026-09-26T12:00:00Z",value:{subjectRaw:"Предмет",subjectKey:"предмет",text:id,targetNthOccurrence:1,createdAtUtc:"2026-09-26T12:00:00Z",legacyCreatedLocalDate:null}});
const page=(id:string,hasMore:boolean,next:number)=>({metadata:{syncEpoch:"epoch",currentSequence:2},changes:[{record:row(id)}],hasMore,nextAfterSequence:next});
const settle=()=>new Promise(resolve=>setImmediate(resolve));
function harness(changes:(epoch:string,after:number)=>Promise<unknown>){
    const memory=new Map<string,string>(),effects:(()=>void|(()=>void))[]=[],timers:(()=>void)[]=[],refs:{current:any}[]=[],listeners=new Map<string,()=>void>();let rejectWrite=false;
    const react={useState:(initial:any)=>[typeof initial==="function"?initial():initial,()=>{}],useRef:(value:any)=>{const ref={current:value};refs.push(ref);return ref;},useEffect:(effect:any)=>effects.push(effect)};
    const api={beginSyncSnapshot:async()=>({syncEpoch:"epoch",manifestId:"manifest",highWater:0}),syncSnapshotPage:async()=>({items:[],hasMore:false,nextAfterOrdinal:0}),privateChanges:changes};
    const context={exports:{} as any,require:(id:string)=>id==="react"?react:id==="./api.ts"?api:{canonicalUtc:String,syncSubjectKey:String},localStorage:{getItem:(key:string)=>memory.get(key)||null,setItem:(key:string,value:string)=>{if(rejectWrite)throw new Error("storage unavailable");memory.set(key,value);}},window:{setInterval:(fn:()=>void)=>(timers.push(fn),timers.length),clearInterval:()=>{},addEventListener:(name:string,fn:()=>void)=>listeners.set(name,fn),removeEventListener:(name:string)=>listeners.delete(name)},document:{hidden:false,addEventListener:()=>{},removeEventListener:()=>{}},crypto:{randomUUID:()=>"uuid"}};
    runInNewContext(source,context);context.exports.usePrivateHomework("owner");effects[0]();const cleanup=effects[1]() as ()=>void;
    return{timers,effects,cleanup,wake:(name:string)=>listeners.get(name)?.(),cursor:()=>refs.find(ref=>ref.current && typeof ref.current === "object" && "sequence" in ref.current)!.current,ids:()=>JSON.parse(memory.get("zapara.private-homework.owner")!).records.map((record:any)=>record.entityId),writesFail(value:boolean){rejectWrite=value;}};
}
test("online wake starts a private change poll without waiting for the interval",async()=>{
    const requested:number[]=[];const app=harness(async(_epoch,after)=>{requested.push(after);return{metadata:{syncEpoch:"epoch",currentSequence:0},changes:[],hasMore:false,nextAfterSequence:after};});
    await settle();app.wake("online");await settle();assert.deepEqual(requested,[0]);app.cleanup();
});
test("actual private hook retries from committed cursor after second-page failure",async()=>{
    const requests:number[]=[];let failure=true;const app=harness(async(_epoch,after)=>{requests.push(after);if(after===0)return page("A",true,1);if(failure){failure=false;throw new Error("second page offline");}return page("B",false,2);});
    await settle();app.timers[0]();await settle();assert.deepEqual(app.ids(),[]);assert.equal(app.cursor().sequence,0);app.timers[0]();await settle();assert.deepEqual(app.ids(),["A","B"]);assert.equal(app.cursor().sequence,2);assert.deepEqual(requests,[0,1,0,1]);
});
test("actual private hook keeps cursor/profile paired on cancellation after first page",async()=>{
    let release:(value:unknown)=>void=()=>{};let delayed=true;const app=harness(async(_epoch,after)=>after===0?page("A",true,1):delayed?new Promise(resolve=>{release=resolve;}):page("B",false,2));
    await settle();app.timers[0]();await settle();assert.equal(app.cursor().sequence,0);app.cleanup();delayed=false;release(page("B",false,2));await settle();assert.deepEqual(app.ids(),[]);assert.equal(app.cursor().sequence,0);app.effects[1]();await settle();assert.deepEqual(app.ids(),["A","B"]);assert.equal(app.cursor().sequence,2);
});
test("actual private hook cannot advance cursor when completed batch persistence fails",async()=>{
    const app=harness(async(_epoch,after)=>after===0?page("A",true,1):page("B",false,2));await settle();app.writesFail(true);app.timers[0]();await settle();assert.deepEqual(app.ids(),[]);assert.equal(app.cursor().sequence,0);app.writesFail(false);app.timers[0]();await settle();assert.deepEqual(app.ids(),["A","B"]);assert.equal(app.cursor().sequence,2);
});
