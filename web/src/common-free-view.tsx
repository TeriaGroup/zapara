import { useBrowseValue } from "./ux300-controls";
import { commonFreeIntervals } from "./study-planning";
import { isoDay, lessonsOn } from "./parity";
import { localDay } from "./planner";
import type { FriendSchedule } from "./intersections";
import type { Lesson, Period } from "./types";

export function CommonFreeTime({mine,friends,period,date,onDate,invert,known}:{mine:Lesson[];friends:FriendSchedule[];period?:Period;date:Date;onDate:(value:Date)=>void;invert:boolean;known:boolean}) {
  const [selected,setSelected]=useBrowseValue("common-free-friend","");
  const friend=friends.find(row=>row.enabled&&row.groupName===selected);
  const available=known&&!!period&&isoDay(date)>=period.start.slice(0,10)&&!!friend?.lessons;
  const first=available?lessonsOn(mine,date,period!.start,period!.weekCount,invert):[];
  const second=available?lessonsOn(friend!.lessons!,date,period!.start,period!.weekCount,invert):[];
  const windows=commonFreeIntervals(first.map(row=>({start:row.timeStart,end:row.timeEnd})),second.map(row=>({start:row.timeStart,end:row.timeEnd})));
  return <details className="card stack"><summary>Общие окна с группой друга</summary><p className="muted">Свободные промежутки от 15 минут внутри известных учебных часов обеих групп. Для вашей группы учтены выбранные подгруппы; для друга — все занятия группы. Это расписание, а не обещание присутствия.</p>
    <div className="row"><label className="field">Дата<input type="date" value={isoDay(date)} onChange={event=>{const next=localDay(event.target.value);if(next)onDate(next);}}/></label><label className="field">Группа друга<select value={friend?.groupName||""} onChange={event=>setSelected(event.target.value)}><option value="">Выберите группу</option>{friends.filter(row=>row.enabled).map(row=><option key={row.groupName}>{row.groupName}</option>)}</select></label></div>
    {!friend?<p>Выберите одну включённую группу друга.</p>:!available?<p role="status">Нет совместимых сохранённых расписаний на эту дату. Обновите расписания выше.</p>:!first.length||!second.length?<p>У одной из групп нет известных занятий. Свободный день автоматически не предполагается.</p>:windows.length?<ul>{windows.map(row=><li key={row.start}>{row.start}–{row.end} · {row.minutes} мин</li>)}</ul>:<p>В общих учебных часах не найдено свободного промежутка от 15 минут.</p>}
  </details>;
}
