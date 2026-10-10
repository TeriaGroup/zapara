import { subjectCount } from "./homework-counts";
import { Link } from "react-router-dom";
import { useBrowseValue } from "./ux300-controls";
import { actualStudyWeek, compareStudyWeeks, weekHomework, homeworkSubjectOverview } from "./study-overviews";
import { addDays, isoDay } from "./parity";
import { localDay, absoluteDate } from "./planner";
import { roomLabel } from "./summary";
import type { HomeworkItem, Lesson, Period } from "./types";
import { formatRange } from "./schedule-text";

export function WeekComparison({lessons,date,period,invert,available}:{lessons:Lesson[];date:Date;period?:Period;invert:boolean;available:boolean}){
  const [otherDate,setOtherDate]=useBrowseValue("week-compare-date",isoDay(addDays(date,7)));const other=localDay(otherDate);
  const first=actualStudyWeek(lessons,date,period,invert,available),second=other?actualStudyWeek(lessons,other,period,invert,available):[];const diff=compareStudyWeeks(first,second);
  const range=(rows:typeof first)=>rows.length?formatRange(rows[0].date,rows[6].date):"дата не выбрана";
  return <details className="card stack"><summary>Сравнить две недели</summary><p className="muted">Две реальные недели по одной текущей сохранённой копии и выбранным подгруппам. Это не история изменений источника.</p><label className="field">Дата второй недели<input type="date" value={otherDate} onChange={event=>setOtherDate(event.target.value)}/></label><p>Первая: {range(first)}<br/>Вторая: {range(second)}</p>
    {!diff.known?<p role="status">Для полного сравнения нужны данные всех семи дней обеих недель. Выберите даты в известном учебном периоде.</p>:<><p role="status">Во второй неделе добавлено: {diff.added.length} · отсутствует: {diff.removed.length}</p>{!diff.added.length&&!diff.removed.length&&<p>Состав пар совпадает по дням, времени, предметам, типам, преподавателям и аудиториям.</p>}{([["Нет во второй неделе",diff.removed],["Добавлено во второй неделе",diff.added]] as const).map(([title,rows])=>rows.length>0&&<section className="stack" key={title}><h3>{title}</h3>{rows.map((row,index)=><article className="card" key={index}><b>{absoluteDate(row.date)} · {row.lesson.timeStart}–{row.lesson.timeEnd}</b><p>{row.lesson.subjectRaw} · {row.lesson.typeRaw}<br/>{row.lesson.teacherRaw} · {roomLabel(row.lesson)}</p></article>)}</section>)}</>}
  </details>;
}
export function WeekHomework({items,days,dateOf}:{items:HomeworkItem[];days:Date[];dateOf:(row:HomeworkItem)=>string|null}){
  const result=weekHomework(items,days.map(isoDay),dateOf);
  return <details className="card stack"><summary>Личные сроки на этой неделе · {result.total}</summary><p className="muted">{isoDay(days[0])} — {isoDay(days[6])}. Все личные задания, включая выполненные; поиск пар выше этот список не скрывает.</p>{result.unknown>0&&<p>Без известной даты: {result.unknown}. Они не размещены на произвольный день.</p>}{!result.total&&<p>Личных сроков с известной датой на выбранную неделю нет.</p>}{result.days.filter(day=>day.items.length>0).map(day=><section className="stack" key={day.day}><h3>{absoluteDate(localDay(day.day)!)}</h3>{day.items.map(row=><Link className="btn quiet" key={row.id} to={`/homework?id=${encodeURIComponent(row.id)}`}><span><b>{row.subject}</b><br/>{row.text}<br/>{row.done?"Выполнено":"Не выполнено"}</span></Link>)}</section>)}</details>;
}
export function HomeworkSubjectOverview({items,dateOf,today,onPick}:{items:HomeworkItem[];dateOf:(row:HomeworkItem)=>string|null;today:string;onPick:(subject:string,key:string)=>void}){
  const rows=homeworkSubjectOverview(items,dateOf,today);
  return <details className="card stack"><summary>Личная домашка по предметам · {subjectCount(rows.length)}</summary><p className="muted">Обзор всех личных заданий. Просроченные, ближайшая дата и неизвестные сроки посчитаны среди невыполненных; сегодняшний срок не считается просроченным.</p>{!rows.length&&<p>Личных заданий пока нет.</p>}{rows.map(row=><article className="card stack" key={row.key}><h3>{row.subject||"Без предмета"}</h3><p>Активно: {row.active} · выполнено: {row.done} · просрочено: {row.overdue}</p><p className="muted">Ближайший известный срок: {row.nearest||"нет"} · без даты: {row.noDate}</p><button className="btn quiet" onClick={()=>onPick(row.subject,row.key)}>Открыть задания предмета · {row.ids.length}</button></article>)}</details>;
}
