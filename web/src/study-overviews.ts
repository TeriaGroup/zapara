import { calendarWeek } from "./ux-navigation.ts";
import { isoDay, lessonsOn, weekday } from "./parity.ts";
import { localDay } from "./planner.ts";
import { syncSubjectKey } from "./utc.ts";
import type { HomeworkItem, Lesson, Period } from "./types.ts";
export type StudyDay={date:Date;known:boolean;lessons:Lesson[]};
export function actualStudyWeek(lessons:Lesson[],date:Date,period:Period|undefined,invert:boolean,available:boolean):StudyDay[]{
  return calendarWeek(date).map(day=>{const known=available&&!!period&&isoDay(day)>=period.start.slice(0,10);return {date:day,known,lessons:known?lessonsOn(lessons,day,period!.start,period!.weekCount,invert):[]};});
}
export function compareStudyWeeks(first:StudyDay[],second:StudyDay[]){
  const empty={known:false,added:[] as {date:Date;lesson:Lesson}[],removed:[] as {date:Date;lesson:Lesson}[]};if(first.length!==7||second.length!==7||[...first,...second].some(day=>!day.known))return empty;
  const slots=(days:StudyDay[])=>days.flatMap(day=>day.lessons.map(lesson=>({date:day.date,lesson,key:JSON.stringify([weekday(day.date),lesson.timeStart,lesson.timeEnd,lesson.subjectRaw,lesson.typeRaw??"",lesson.teacherRaw??"",lesson.classroomRaw??""])})));
  const left=slots(first),right=slots(second);const matched=new Set<number>();const removed=left.filter(row=>{const i=right.findIndex((candidate,index)=>!matched.has(index)&&candidate.key===row.key);if(i<0)return true;matched.add(i);return false;});
  return {known:true,removed,added:right.filter((_,index)=>!matched.has(index))};
}
export function weekHomework(items:HomeworkItem[],days:string[],dateOf:(row:HomeworkItem)=>string|null){
  let unknown=0;const byDay=new Map(days.map(day=>[day,[] as HomeworkItem[]]));for(const row of items){const day=dateOf(row);if(!day||!localDay(day)){unknown++;continue;}byDay.get(day)?.push(row);}
  return {unknown,total:[...byDay.values()].reduce((total,rows)=>total+rows.length,0),days:days.map(day=>({day,items:byDay.get(day)!}))};
}
export function homeworkSubjectOverview(items:HomeworkItem[],dateOf:(row:HomeworkItem)=>string|null,today:string){
  const groups=new Map<string,{key:string;subject:string;active:number;done:number;overdue:number;noDate:number;nearest:string|null;ids:string[]}>();
  for(const item of items){const key=syncSubjectKey(item.subject);const row=groups.get(key)||{key,subject:item.subject.trim(),active:0,done:0,overdue:0,noDate:0,nearest:null,ids:[]};row.ids.push(item.id);if(item.done)row.done++;else{row.active++;const due=dateOf(item);if(!due||!localDay(due))row.noDate++;else{if(due<today)row.overdue++;if(!row.nearest||due<row.nearest)row.nearest=due;}}groups.set(key,row);}
  return [...groups.values()].sort((a,b)=>a.subject.localeCompare(b.subject,"ru"));
}
