import { addDays, parityOf, weekday } from "./parity.ts";
import { clockMinutes } from "./planner.ts";
import { scalarInput } from "./scalar-input.ts";
import type { HomeworkItem, Lesson, Period } from "./types.ts";

export function editPersonalHomework(current: HomeworkItem, draft: {subject:string;text:string;nth:number}): HomeworkItem {
  if (!Number.isInteger(draft.nth) || draft.nth < 1 || draft.nth > 10) throw new Error("Укажите номер пары от 1 до 10.");
  if (!draft.subject.trim() || !draft.text.trim()) throw new Error("Укажите предмет и текст задания.");
  if (!scalarInput(draft.subject.trim(),1,256,false)) throw new Error("Предмет: до 256 символов, без недопустимых символов.");
  if (!scalarInput(draft.text.trim(),1,4000,false)) throw new Error("Задание: до 4000 символов, без недопустимых символов.");
  return {...current,subject:draft.subject.trim(),text:draft.text.trim(),targetNthOccurrence:Math.max(1,Math.min(10,draft.nth))};
}
export function overlapPairs(lessons: Pick<Lesson,"timeStart"|"timeEnd"|"subjectRaw">[]) {
  const result:{left:number;right:number}[]=[];
  lessons.forEach((left,i)=>lessons.slice(i+1).forEach((right,j)=>{
    const a=clockMinutes(left.timeStart),b=clockMinutes(left.timeEnd),c=clockMinutes(right.timeStart),d=clockMinutes(right.timeEnd);
    if(a!==null&&b!==null&&c!==null&&d!==null&&b>a&&d>c&&a<d&&c<b)result.push({left:i,right:i+j+1});
  }));
  return result;
}
export function nextTeacherDay(now:Date,day:number,parity:number,period:Period,invert:boolean):Date|null {
  if(day<1||day>7)return null;
  for(let i=0;i<56;i++){const date=addDays(now,i);if(weekday(date)===day&&(!parity||parityOf(date,period.start,period.weekCount,invert)===parity))return date;}
  return null;
}
export const settingsAliases:Record<string,string>={account:"аккаунт вход профиль имя пароль устройства",study:"учёба учеба группа расписание подгруппа четность чётность",appearance:"оформление тема тёмная темная светлая анимации",notifications:"уведомления напоминания утро вечер время",data:"данные синхронизация импорт офлайн копия конфликт",help:"помощь поддержка ошибка баг версия обновление документы политика соглашение"};
export function normalizedWords(value:string){return value.trim().toLocaleLowerCase("ru-RU").replaceAll("ё","е").split(/\s+/).filter(Boolean);}
export function matchesWords(query:string,...fields:string[]){const haystack=fields.join(" ").toLocaleLowerCase("ru-RU").replaceAll("ё","е");return normalizedWords(query).every(word=>haystack.includes(word));}
export type SubgroupUndo={scope:string;group:string;stream:string;before:string|undefined;after:string|undefined};
export function subgroupUndoCurrent(undo:SubgroupUndo,scope:string,group:string,choices:Record<string,string>){return undo.scope===scope&&undo.group===group&&choices[undo.stream]===undo.after;}
