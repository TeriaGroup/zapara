import {addDays,isoDay,lessonsOn} from './parity.ts';
import {clockMinutes as minutesOf} from './planner.ts';
import {compareStudyWeeks,type StudyDay} from './study-overviews.ts';
import {resolveCampusClassroom,type CampusGraph} from './campus-routing.ts';
import {routeClassroom} from './route-context.ts';
import type {Lesson,Period,Group,TimetablePayload} from './types.ts';

export const rawLessonKey=(row:Lesson)=>JSON.stringify([row.timeStart,row.timeEnd,row.subjectRaw,row.typeRaw??'',row.teacherRaw??'',row.classroomRaw??'']);
export function exactLessonIndex(rows:Lesson[],key:string){const matches=rows.flatMap((row,index)=>rawLessonKey(row)===key?[index]:[]);return matches.length===1?matches[0]:-1;}
function assessment(row:Lesson){
 const value=(row.typeRaw?.trim()||row.subjectRaw.trim().match(/^(экзамен|экз\.?|зач[её]т|зач\.?|диф\.?\s*зач[её]т)\s*[:.\-]?\s/iu)?.[1]||'').toLowerCase().replace(/[.\s]/g,'').replace(/ё/g,'е');
 return ['экз','экзамен','зач','зачет','дифзач','дифзачет'].includes(value);
}
export function assessmentPlan(lessons:Lesson[],start:Date,period:Period|undefined,invert:boolean,available:boolean){
 const seen=new Set<string>();const rows:{day:string;lesson:Lesson}[]=[];let unknownDays=0;
 for(let offset=0;offset<28;offset++){const date=addDays(start,offset),day=isoDay(date);if(!available||!period||day<period.start.slice(0,10)){unknownDays++;continue;}
  for(const lesson of lessonsOn(lessons,date,period.start,period.weekCount,invert)){const key=JSON.stringify([lesson.dayOfWeek,lesson.parity,rawLessonKey(lesson)]);if(!assessment(lesson)||seen.has(key))continue;seen.add(key);rows.push({day,lesson:{...lesson}});}
 }
 rows.sort((a,b)=>a.day.localeCompare(b.day)||(minutesOf(a.lesson.timeStart)??Infinity)-(minutesOf(b.lesson.timeStart)??Infinity));return {rows,unknownDays};
}
export function roomActivity(graph:CampusGraph,nodeId:string,date:Date,groups:Group[],cache:Record<string,TimetablePayload>,invert:boolean){
 const rows:{group:Group;lesson:Lesson;fetchedAt:string}[]=[];let checked=0,unmapped=0;const known=new Map(groups.map(group=>[group.id,group]));
 if(!graph.nodes.some(node=>node.id===nodeId&&node.kind==='room'))return {rows,checked,unmapped,known:known.size};
 for(const group of known.values()){const payload=cache[group.id]||Object.values(cache).find(item=>item.group?.id===group.id);if(!payload?.period||!Array.isArray(payload.lessons)||isoDay(date)<payload.period.start.slice(0,10))continue;checked++;
  for(const lesson of lessonsOn(payload.lessons,date,payload.period.start,payload.period.weekCount,invert)){const node=resolveCampusClassroom(graph,routeClassroom(lesson.classroomRaw,lesson.roomRaw,lesson.buildingRaw));if(!node){unmapped++;continue;}if(node.id===nodeId)rows.push({group,lesson,fetchedAt:payload.meta?.fetchedAt||''});}
 }
 rows.sort((a,b)=>(minutesOf(a.lesson.timeStart)??Infinity)-(minutesOf(b.lesson.timeStart)??Infinity)||a.group.name.localeCompare(b.group.name));return {rows,checked,unmapped,known:known.size};
}
export class WeekChanges {
 private previous:{scope:string;source:string;selection:string;days:StudyDay[]}|null=null;
 private result:ReturnType<typeof compareStudyWeeks>|null=null;
 observe(scope:string,days:StudyDay[],source:string,selection:string,loading:boolean,failed:boolean){
  if(this.previous?.scope!==scope){this.previous=null;this.result=null;}
  if(days.length!==7||days.some(day=>!day.known)){this.previous=null;this.result=null;return null;}
  const choiceChanged=!!this.previous&&this.previous.selection!==selection;
  if(loading||failed&&!choiceChanged)return this.result;
  if(this.previous&&(this.previous.source!==source||choiceChanged))this.result=compareStudyWeeks(this.previous.days,days);
  this.previous={scope,source,selection,days:days.map(day=>({...day,date:new Date(day.date),lessons:day.lessons.map(row=>({...row}))}))};return this.result;
 }
}
export function subgroupCandidate(choices:Record<string,string>,stream:string,option:string){const next={...choices};if(next[stream]===option)delete next[stream];else next[stream]=option;return next;}
export type TechnicalSnapshot={version:string;available:boolean;cacheGroups:number;cacheAgeDays:number|null;maps:number|null;pending:number;conflicts:number};
export function technicalSummary(value:TechnicalSnapshot){
 const count=(n:number|null)=>n!==null&&Number.isFinite(n)&&n>=0?String(Math.floor(n)):'неизвестно';
 const version=/^[a-zA-Z0-9._-]{1,64}$/.test(value.version)?value.version:'неизвестна';
 return ['Расписание военмех — техническая сводка','Платформа: веб','Сборка: '+version,'Текущая копия расписания: '+(value.available?'доступна':'недоступна'),'Копий групп в браузере: '+count(value.cacheGroups),'Возраст текущей копии, дней: '+count(value.cacheAgeDays),'Планов в каталоге сервера: '+count(value.maps),'Локальных изменений в очереди: '+count(value.pending),'Конфликтов: '+count(value.conflicts)].join('\n');
}
