import assert from "node:assert/strict";
import { test } from "node:test";
import { createMaterialHistory, type MaterialHistory } from "./material-history.ts";
import type { ChatMessage } from "./types";
const row=(id:number):ChatMessage=>({messageId:String(id).padStart(3,"0"),conversationId:"conversation",senderId:"person",senderName:"Участник",body:`Материал ${id}`,kind:"text",createdAt:new Date(2026,8,26,10,0,0,id).toISOString()});
const rows=(start:number,end:number)=>Array.from({length:end-start+1},(_,i)=>row(start+i));
test("materials load over 50 entries and polling preserves fetched history while updating known records",async()=>{
    let latest=rows(51,100);let state:MaterialHistory={messages:null,hasOlder:false,loadingOlder:false};const cursors:any[]=[];
    const history=createMaterialHistory(async cursor=>{cursors.push(cursor);return cursor?.before?{messages:rows(1,50),hasMore:false}:{messages:latest,hasMore:true};},value=>{state=value;},error=>{throw error;});
    await history.poll();assert.equal(state.messages!.length,50);assert.equal(state.hasOlder,true);await history.earlier();assert.equal(cursors[1].before,"051");assert.equal(state.messages!.length,100);assert.equal(state.hasOlder,false);
    latest=[...rows(52,99),{...row(100),body:"Обновлённый материал"},row(101)];await history.poll();assert.equal(state.messages!.length,101);assert.equal(state.messages![0].messageId,"001");assert.equal(state.messages!.find(row=>row.messageId==="100")!.body,"Обновлённый материал");assert.equal(state.hasOlder,false);
});
test("permission revocation invalidates an in-flight older material page",async()=>{
    let state:MaterialHistory={messages:null,hasOlder:false,loadingOlder:false};let denied=false;let release:(page:{messages:ChatMessage[];hasMore:boolean})=>void=()=>{};
    const history=createMaterialHistory(async cursor=>{if(cursor?.before)return new Promise(resolve=>{release=resolve;});if(denied)throw new Error("403");return {messages:rows(51,100),hasMore:true};},value=>{state=value;},()=>{});
    await history.poll();const earlier=history.earlier();denied=true;await history.poll();assert.deepEqual(state.messages,[]);release({messages:rows(1,50),hasMore:false});await earlier;assert.deepEqual(state.messages,[]);
});
