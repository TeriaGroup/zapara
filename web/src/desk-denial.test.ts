import assert from "node:assert/strict";
import { test } from "node:test";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";

const source=await readFile(new URL("./pages.tsx",import.meta.url),"utf8");
const marker=source.indexOf("const ticket = ++deskEpoch.current;");
const start=source.lastIndexOf("  useEffect(() => {",marker);
const suffix="  }, [app.session?.authenticated, communityId]);";
const end=source.indexOf(suffix,marker)+suffix.length;
assert.ok(start>=0&&end>marker,"actual desk effect located");
const effect=source.slice(start,end);
const settle=()=>new Promise(resolve=>setImmediate(resolve));

test("actual desk effect ignores old403 after newer200 and applies current403",async()=>{
 let rejectOld:(error:Error)=>void=()=>{},calls=0,tick:()=>void=()=>{},cleanup:()=>void=()=>{};
 let desk:any=null,draft="private A";const denied:any[]=[];
 const current={mine:["read"],roles:[],marker:"fresh200"};
 const context={Error,app:{session:{authenticated:true,user:{userId:"owner"}}},communityId:"c",deskEpoch:{current:0},
  api:{groupDesk:()=>{calls++;if(calls===1)return new Promise((_,reject)=>{rejectOld=reject;});if(calls===2)return Promise.resolve(current);return Promise.reject(new Error("403"));}},
  useEffect:(callback:()=>()=>void)=>{cleanup=callback();},window:{setInterval:(fn:()=>void)=>{tick=fn;return 1;},clearInterval:()=>{}},
  setDesk:(value:any)=>{desk=value;},revokeGroupDrafts:(scope:any)=>{denied.push(scope);draft="";},viewKeyRef:{current:"group"},selectionEpoch:{current:0},clearLog:()=>{},setHome:()=>{},setChat:()=>{},setBoard:()=>{},setError:()=>{}};
 runInNewContext(effect,context);tick();await settle();assert.equal(desk,current);
 rejectOld(new Error("403"));await settle();assert.equal(desk,current);assert.equal(draft,"private A");assert.equal(denied.length,0);
 tick();await settle();assert.equal(desk,null);assert.equal(draft,"");assert.equal(denied.length,1);assert.deepEqual(JSON.parse(JSON.stringify(denied[0])),{owner:"owner",community:"c"});cleanup();
});
