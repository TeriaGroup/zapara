import {test} from 'node:test';
import assert from 'node:assert/strict';
import {collectObligations,obligationNeedsMe,withGlobalBallots,GLOBAL_BALLOTS} from './group-obligations.ts';
const topic=(id:string,kind='forms'):any=>({topicId:id,title:id,kind,unread:0,archived:false});
test('obligations cap reads, report partial coverage, never fetch responses, discard revoked rows',async()=>{
 let calls=0;const topics=Array.from({length:26},(_,i)=>topic(String(i)));const previous:any=[{id:'old',source:'0',kind:'form',title:'old',deadline:null,needed:true}];
 const result=await collectObligations(topics,previous,()=>true,async row=>{calls++;if(row.topicId==='0')throw Error('403');if(row.topicId==='1')throw Error('offline');return [{id:row.topicId!,source:row.topicId!,kind:'form',title:'title',deadline:null,needed:true}];});
 assert.equal(calls,23);assert.equal(result!.skipped,3);assert.equal(result!.failed,2);assert.equal(result!.rows.some(row=>row.id==='old'),false);
});
test('owner switch during await discards all output and sends no next request',async()=>{
 let valid=true,calls=0;const result=await collectObligations([topic('a'),topic('b')],[],()=>valid,async()=>{calls++;valid=false;return [];});assert.equal(result,null);assert.equal(calls,1);
});
test('needs-me respects exact instant, answered/completed state and unknown deadline',()=>{
 const row:any={needed:true,deadline:'2026-10-02T10:00:00Z'};assert.equal(obligationNeedsMe(row,new Date('2026-10-02T09:59:59Z')),true);assert.equal(obligationNeedsMe(row,new Date('2026-10-02T10:00:00Z')),false);assert.equal(obligationNeedsMe({...row,deadline:null},new Date()),true);assert.equal(obligationNeedsMe({...row,needed:false,deadline:null},new Date()),false);
});
test('global ballot board participates once inside the same 24-GET budget',async()=>{
 const sources=withGlobalBallots(Array.from({length:30},(_,i)=>topic(String(i))));const visited:string[]=[];
 const result=await collectObligations(sources,[],()=>true,async row=>{visited.push(row.topicId!);return [{id:row.topicId!,source:row.topicId!,kind:'ballot',title:'board',deadline:null,needed:true}];});
 assert.equal(visited[0],GLOBAL_BALLOTS);assert.equal(visited.length,23);assert.equal(visited.filter(id=>id===GLOBAL_BALLOTS).length,1);assert.equal(result!.skipped,8);assert.equal(result!.rows.some(row=>row.source===GLOBAL_BALLOTS),true);
});
