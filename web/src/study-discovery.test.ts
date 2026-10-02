import {test} from 'node:test';
import assert from 'node:assert/strict';
import {assessmentPlan, exactLessonIndex, rawLessonKey, roomActivity, WeekChanges, subgroupCandidate, technicalSummary} from './study-discovery.ts';
import {actualStudyWeek} from './study-overviews.ts';
const period={start:'2026-09-01',weekCount:2,title:'Осень',timeZone:'Europe/Moscow'};
const lesson:any={dayOfWeek:1,parity:0,index:1,timeStart:'9:00',timeEnd:'10:30',subjectRaw:'Алгебра',typeRaw:'зачёт',teacherRaw:'Иванов',classroomRaw:'101;',roomRaw:'101',buildingRaw:'ГК'};
test('assessment horizon uses nearest raw slot, narrow types and distinguishes same-time rooms',()=>{
 const result=assessmentPlan([lesson,{...lesson,classroomRaw:'102;',timeStart:'10:40'},{...lesson,typeRaw:'контрольная'}],new Date(2026,11,28),period,false,true);
 assert.equal(result.rows.length,2);assert.equal(result.rows[0].day,'2026-12-28');assert.equal(result.unknownDays,0);
 assert.equal(assessmentPlan([lesson],new Date(2026,7,30),period,false,true).unknownDays,2);
 assert.equal(assessmentPlan([lesson],new Date(),period,false,false).unknownDays,28);
 assert.equal(exactLessonIndex([lesson],rawLessonKey({...lesson,classroomRaw:'102;'})),-1);
 assert.equal(exactLessonIndex([lesson],rawLessonKey(lesson)),0);
 assert.equal(exactLessonIndex([lesson,{...lesson}],rawLessonKey(lesson)),-1);
});
test('room coverage deduplicates cache aliases, distinguishes buildings, excludes unknown dates',()=>{
 const graph:any={nodes:[{id:'g',kind:'room',building:'ГК',floor:1,room:'101',label:'101'},{id:'u',kind:'room',building:'УЛК',floor:1,room:'101',label:'101'}],edges:[],blocked:[],version:1,buildings:[]};
 const payload:any={group:{id:'a',name:'A'},period,lessons:[lesson,{...lesson,classroomRaw:'101*;'},{...lesson,classroomRaw:'онлайн'}],meta:{fetchedAt:'2026-10-01'}};
 const result=roomActivity(graph,'g',new Date(2026,9,5),[{id:'a',name:'A'},{id:'b',name:'B'}] as any,{a:payload,A:payload},false);
 assert.equal(result.known,2);assert.equal(result.checked,1);assert.equal(result.rows.length,1);assert.equal(result.unmapped,1);
 assert.equal(roomActivity(graph,'g',new Date(2026,7,31),[{id:'a',name:'A'}] as any,{a:payload},false).checked,0);
});
test('same-week journal freezes old rows, ignores failed refresh and clears across scope',()=>{
 const journal=new WeekChanges();const week=(rows:any[])=>actualStudyWeek(rows,new Date(2026,9,5),period,false,true);
 const first=week([lesson,lesson]);assert.equal(journal.observe('a',first,'0','x',false,false),null);
 first[0].lessons[0].teacherRaw='MUTATED';
 journal.observe('a',week([]),'1','x',true,false);
 assert.equal(journal.observe('a',week([]),'1','x',false,true),null);
 const diff=journal.observe('a',week([{...lesson,teacherRaw:'Иванов'}]),'2','x',false,false)!;
 assert.equal(diff.removed.length,1);assert.equal(diff.removed[0].lesson.teacherRaw,'Иванов');
 assert.equal(journal.observe('b',week([]),'3','x',false,false),null);
 assert.equal(journal.observe('b',week([]),'4','x',false,false)!.added.length,0);
});
test('candidate choice toggles without mutating saved selection; diagnostic whitelist excludes content',()=>{
 const choices={x:'a',y:'z'};assert.deepEqual(subgroupCandidate(choices,'x','a'),{y:'z'});assert.deepEqual(choices,{x:'a',y:'z'});
 const text=technicalSummary({version:'deadbeef',available:true,cacheGroups:2,cacheAgeDays:3,maps:9,pending:1,conflicts:0,username:'secret',body:'private',path:'C:/secret'} as any);
 assert.match(text,/deadbeef/);assert.doesNotMatch(text,/secret|private|C:\//);
 assert.doesNotMatch(technicalSummary({version:'C:/private',available:false,cacheGroups:-1,cacheAgeDays:NaN,maps:null,pending:0,conflicts:0}),/private/);
});
test('assessment includes day 28, excludes day 29 and applies parity before nearest-slot deduplication',()=>{
 const start=new Date(2026,11,8);const day28={...lesson,dayOfWeek:1};
 const inside=assessmentPlan([day28],start,{...period,start:'2027-01-04'},false,true);assert.equal(inside.rows[0].day,'2027-01-04');assert.equal(inside.unknownDays,27);
 assert.equal(assessmentPlan([{...day28,dayOfWeek:2}],start,{...period,start:'2027-01-05'},false,true).rows.length,0);
 const first=assessmentPlan([{...lesson,parity:1}],new Date(2026,9,5),period,false,true).rows[0].day;
 const inverted=assessmentPlan([{...lesson,parity:1}],new Date(2026,9,5),period,true,true).rows[0].day;assert.notEqual(first,inverted);
});
