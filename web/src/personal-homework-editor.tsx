import { useRef, useState } from "react";
import { useApp } from "./store";
import { editPersonalHomework } from "./next-workflows";
import { readCache } from "./api";
import { personalHomeworkDue } from "./planner";
import { visibleLessons } from "./subgroups";
export type PersonalEditDraft={id:string;owner:string;subject:string;text:string;nth:number};
export function PersonalHomeworkEditor({draft,onChange,onClose}:{draft:PersonalEditDraft;onChange:(value:PersonalEditDraft)=>void;onClose:()=>void}) {
  const app=useApp();const [error,setError]=useState("");
  const original=useRef({...draft}).current;
  const dirty=draft.subject!==original.subject||draft.text!==original.text||draft.nth!==original.nth;
  const row=app.homework.find(item=>item.id===draft.id);
  const period=readCache().lessons[app.groupId]?.period;
  const due=row&&period?personalHomeworkDue({...row,subject:draft.subject,targetNthOccurrence:draft.nth},visibleLessons(app.lessons,app.subgroups[app.groupId]||{}),period,app.invert):null;
  return <form className="card stack" onSubmit={event=>{event.preventDefault();try{const row=app.homework.find(item=>item.id===draft.id);if(!row)throw new Error("Задание больше недоступно.");app.saveHomework(editPersonalHomework(row,draft));onClose();}catch(reason){setError(reason instanceof Error?reason.message:"Изменения не сохранены. Черновик оставлен.");}}}>
    <h2>Изменить личное задание</h2><p className="muted">Дата создания, отметка выполнения и вложения сохранятся.</p>
    <label className="field">Предмет<input required value={draft.subject} onChange={event=>onChange({...draft,subject:event.target.value})}/></label>
    <label className="field">Задание<textarea required value={draft.text} onChange={event=>onChange({...draft,text:event.target.value})}/></label>
    <p className="muted">Предмет — до 256 символов, задание — до 4000. Длинный текст остаётся в редакторе.</p>
    <label className="field">К какому занятию<select value={draft.nth} onChange={event=>onChange({...draft,nth:Number(event.target.value)})}>{Array.from({length:10},(_,i)=><option key={i+1} value={i+1}>{i+1}-е занятие</option>)}</select></label>
    <p className="muted">{due?`После сохранения срок: ${due.toLocaleDateString("ru-RU")}`:"Ближайшая дата по выбранному предмету не найдена."}</p>
    {error&&<p role="alert">{error}</p>}<div className="row"><button className="btn primary" disabled={!dirty}>Сохранить изменения</button><button className="btn quiet" type="button" onClick={()=>{if(!dirty||window.confirm("Отбросить изменения этого задания?"))onClose();}}>Отмена</button></div>
  </form>;
}
