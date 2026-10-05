import assert from "node:assert/strict";
import { test } from "node:test";
import { createTopicAuthority,selectedTopicAuthority } from "./topic-authority.ts";
test("disposed authority cannot deny a newly authorized same-scope instance; current denial still applies",async()=>{
 let rejectOld:(error:Error)=>void=()=>{},draft="old",denials=0;
 const space=async()=>({topics:[{topicId:"A"}],categories:[],desk:{},capabilities:{}} as any);
 const denied=()=>{denials++;draft="";};
 const old=createTopicAuthority(space,()=>new Promise((_,reject)=>{rejectOld=reject;}),space,()=>{},()=>{},denied);
 const held=old.refresh();await new Promise(resolve=>setImmediate(resolve));old.dispose();
 let rejectCurrent=false;
 const current=createTopicAuthority(space,async()=>{if(rejectCurrent)throw new Error("403");return{topics:[],canManageChannels:false};},space,()=>{},()=>{},denied);
 await current.refresh();draft="new authorized A";rejectOld(new Error("403"));await held;
 assert.equal(draft,"new authorized A");assert.equal(denials,0);
 rejectCurrent=true;await current.refresh();assert.equal(draft,"");assert.equal(denials,1);current.dispose();
});
test("actual authority ignores an old delayed archive after fresh B-only archive and keeps transient failures separate",async()=>{let rows:any[]=[{topicId:"A",title:"Private A"},{topicId:"B",title:"B"}],delay=false,release:(value:any)=>void=()=>{},current:any;const source=createTopicAuthority(async()=>({topics:[],categories:[],desk:{},capabilities:{}} as any),async()=>delay?new Promise(resolve=>{release=resolve;}):{topics:rows,canManageChannels:false},async()=>{throw new Error("legacy not expected");},value=>{current=value;},()=>{});await source.refresh();assert.equal(current.archived.length,2);delay=true;const stale=source.refresh();await new Promise(resolve=>setImmediate(resolve));delay=false;rows=[rows[1]];await source.refresh();release({topics:[{topicId:"A",title:"Private A"},rows[0]],canManageChannels:false});await stale;assert.deepEqual(current.all.map((topic:any)=>topic.topicId),["B"]);const ticket=source.ticket();source.dispose();assert.equal(source.valid(ticket),false);});
test("actual archive-aware selected reader retains authorized archive and closes only when authoritative archive removes it",async()=>{let archived:any[]=[{topicId:"A",archived:true,title:"Archive A",permissions:["read"]}];const active=async()=>({topics:[],canManageChannels:false});const archive=async()=>({topics:archived,canManageChannels:false});assert.equal((await selectedTopicAuthority(active,archive,"A")).selected?.archived,true);archived=[];assert.equal((await selectedTopicAuthority(active,archive,"A")).selected,null);});
