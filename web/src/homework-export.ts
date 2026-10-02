import { calendarOccurrenceId, type AllDayCalendarEntry } from "./calendar-export.ts";
import { isoDay } from "./parity.ts";
import type { PersonalBrowseItem } from "./homework-browse.ts";

function knownDay(item: PersonalBrowseItem) {
  const date=item.deadlineAt?new Date(item.deadlineAt):null;
  return item.deadlinePrecision==="date"&&date&&Number.isFinite(date.getTime())?isoDay(date):"";
}
export function homeworkCalendarEntries(items: PersonalBrowseItem[], groupId: string): AllDayCalendarEntry[] {
  return items.map(item=>({id:"homework:"+calendarOccurrenceId(groupId,item.subject,item.text,""),day:knownDay(item),summary:`${item.subject} — домашнее задание`,description:[item.text,item.done?"Выполнено":"Не выполнено",...(item.files||[]).map(file=>`Файл: ${file.name}`)].join("\n")}));
}
export function homeworkPlanText(items: PersonalBrowseItem[], groupName: string): string {
  return [`Личная домашка · ${groupName}`,`Заданий в выбранной выдаче: ${items.length}`,"",...items.flatMap((item,index)=>[`${index+1}. ${item.subject} · ${knownDay(item)||"Без известной даты"} · ${item.done?"Выполнено":"Не выполнено"}`,item.text,...(item.files||[]).map(file=>`Файл: ${file.name}`),""])].join("\n");
}
