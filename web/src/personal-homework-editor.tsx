import { useRef, useState } from "react";
import { useApp } from "./store";
import { editPersonalHomework } from "./next-workflows";
import { readCache } from "./api";
import { personalHomeworkDue } from "./planner";
import { visibleLessons } from "./subgroups";
import { Sheet } from "./sheet";
import { Icon } from "./icons";
export type PersonalEditDraft={id:string;owner:string;subject:string;text:string;nth:number};
export function PersonalHomeworkEditor({draft,onChange,onClose}:{draft:PersonalEditDraft;onChange:(value:PersonalEditDraft)=>void;onClose:()=>void}) {
  const app=useApp();const [error,setError]=useState("");
  const original=useRef({...draft}).current;
  const dirty=draft.subject!==original.subject||draft.text!==original.text||draft.nth!==original.nth;
  const close=()=>{if(!dirty||window.confirm("Отбросить изменения этого задания?"))onClose();};
  const row=app.homework.find(item=>item.id===draft.id);
  const period=readCache().lessons[app.groupId]?.period;
  const due=row&&period?personalHomeworkDue({...row,subject:draft.subject,targetNthOccurrence:draft.nth},visibleLessons(app.lessons,app.subgroups[app.groupId]||{}),period,app.invert):null;
  return <Sheet title="Изменить личное задание" onClose={close}><form className="stack mobile-personal-editor" onSubmit={event=>{event.preventDefault();try{const row=app.homework.find(item=>item.id===draft.id);if(!row)throw new Error("Задание больше недоступно.");app.saveHomework(editPersonalHomework(row,draft));onClose();}catch(reason){setError(reason instanceof Error?reason.message:"Изменения не сохранены. Черновик оставлен.");}}}>
    <p className="muted">Дата создания, отметка выполнения и вложения сохранятся.</p>
    <label className="field">Предмет<input required value={draft.subject} onChange={event=>onChange({...draft,subject:event.target.value})}/></label>
    <label className="field">Задание<textarea required value={draft.text} onChange={event=>onChange({...draft,text:event.target.value})}/></label>
    <p className="muted">Предмет — до 256 символов, задание — до 4000. Длинный текст остаётся в редакторе.</p>
    <div className="mobile-homework-deadline"><span className="field-label">К какому занятию</span><div className="mobile-homework-stepper">
      <button className="icon-btn" type="button" aria-label="Уменьшить номер занятия" disabled={draft.nth<=1} onClick={()=>onChange({...draft,nth:Math.max(1,draft.nth-1)})}><Icon name="minus" size={16} /></button>
      <strong>{draft.nth}-е занятие</strong>
      <button className="icon-btn" type="button" aria-label="Увеличить номер занятия" disabled={draft.nth>=10} onClick={()=>onChange({...draft,nth:Math.min(10,draft.nth+1)})}><Icon name="plus" size={16} /></button>
    </div></div>
    <p className="muted">{due?`После сохранения срок: ${due.toLocaleDateString("ru-RU")}`:"Ближайшая дата по выбранному предмету не найдена."}</p>
    {error&&<p role="alert" className="banner">{error}</p>}<div className="mobile-personal-editor-footer"><button className="btn primary" disabled={!dirty}>Сохранить изменения</button><button className="btn quiet" type="button" onClick={close}>Отмена</button></div>
  </form></Sheet>;
}
