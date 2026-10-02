import { calendarOccurrenceId, moscowLessonInstant, type CalendarEntry } from "./calendar-export.ts";
import { isoDay } from "./parity.ts";
import { roomLabel } from "./summary.ts";
import type { Lesson } from "./types.ts";
export type ExportDay={date:Date;lessons:Lesson[];known?:boolean};
export function scheduleCalendarEntries(days:ExportDay[],groupId:string):CalendarEntry[]{return days.filter(day=>day.known!==false).flatMap(day=>day.lessons.map(lesson=>({id:calendarOccurrenceId(groupId,lesson.subjectRaw,lesson.teacherRaw??"",lesson.classroomRaw??""),start:moscowLessonInstant(isoDay(day.date),lesson.timeStart)||"",end:moscowLessonInstant(isoDay(day.date),lesson.timeEnd)||"",summary:lesson.subjectRaw,location:roomLabel(lesson),description:[lesson.typeRaw,lesson.teacherRaw].filter(Boolean).join(" · ")})));}
export function schedulePlainText(days:ExportDay[],groupName:string){return [`Расписание · ${groupName}`,...days.flatMap(day=>[day.date.toLocaleDateString("ru-RU",{weekday:"long",day:"numeric",month:"long",year:"numeric"}),...(day.known===false?["В сохранённой копии нет данных"]:day.lessons.length?day.lessons.map(lesson=>`${lesson.timeStart}–${lesson.timeEnd} · ${lesson.subjectRaw} · ${roomLabel(lesson)}${lesson.teacherRaw?` · ${lesson.teacherRaw}`:""}`):["Без пар"])])].join("\n");}
