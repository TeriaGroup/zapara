import { useLayoutEffect, useRef, useState } from "react";
import { useApp } from "./store";
import { authGeneration } from "./api";
import { Sheet } from "./sheet";
import { postponePreview, applyPostponement, undoPostponement, type PostponePreview, type PostponeResult } from "./homework-postpone";
import type { HomeworkItem } from "./types";

export function HomeworkPostpone({items,dateOf}:{items:HomeworkItem[];dateOf:(row:HomeworkItem)=>string|null}){
  const app=useApp();const live=useRef({app,dateOf});live.current={app,dateOf};
  const scope=JSON.stringify([app.session?.user?.userId||"guest",app.session?.familyId,app.groupId]);const owner=useRef(scope);owner.current=scope;
  const [preview,setPreview]=useState<PostponePreview[]|null>(null);const[selected,setSelected]=useState<string[]>([]);const[busy,setBusy]=useState(false);const[result,setResult]=useState<PostponeResult|null>(null);const[undo,setUndo]=useState<PostponePreview[]>([]);const[note,setNote]=useState("");
  const [open,setOpen]=useState(false);
  const active=useRef(true);const pending=useRef(false);useLayoutEffect(()=>{active.current=true;return()=>{active.current=false;};},[]);
  async function run(rows:PostponePreview[],reverse=false){if(pending.current||!rows.length)return;const auth=authGeneration(),captured=scope;const current=()=>active.current&&owner.current===captured&&authGeneration()===auth;pending.current=true;setBusy(true);setNote("");
    const actions={current,read:(id:string)=>live.current.app.homework.find(row=>row.id===id),dateOf:(row:HomeworkItem)=>live.current.dateOf(row),save:(row:HomeworkItem)=>live.current.app.saveHomework(row),checkpoint:()=>new Promise<void>(resolve=>window.setTimeout(resolve,0))};
    const value=await(reverse?undoPostponement(rows,actions):applyPostponement(rows,actions));pending.current=false;if(!current())return;setBusy(false);setResult(reverse?null:value);setSelected(reverse?[]:value.failed);
    if(reverse){setUndo(values=>values.filter(row=>!value.saved.some(saved=>saved.id===row.id)));setNote(`Возвращено к прежнему сроку: ${value.saved.length}. Не возвращено: ${value.failed.length}. Изменённые после переноса задания не перезаписаны.`);}else{setUndo(values=>[...values.filter(row=>!value.saved.some(saved=>saved.id===row.id)),...value.saved]);setNote(`Перенесено на устройстве: ${value.saved.length}. Не сохранилось или изменилось после просмотра: ${value.failed.length}.`);}
  }
  function newPreview(){setPreview(postponePreview(items,id=>live.current.dateOf(id)));setSelected([]);setResult(null);setUndo([]);setNote("");setOpen(true);}
  return <><button className="btn quiet" disabled={!preview&&!items.some(row=>!row.done)} onClick={()=>{if(preview)setOpen(true);else newPreview();}}>{preview?"Результат переноса и отмена":"Перенести личную домашку на следующее занятие"}</button>
    {open&&preview&&<Sheet title="Перенос сроков личной домашки" onClose={()=>{if(!busy)setOpen(false);}}><div className="stack"><p>Номер следующего занятия увеличится на один. Ниже — реальные даты из сохранённого расписания. До 50 заданий; выполненные, десятое занятие и неизвестные даты исключены. Файлы остаются у исходных заданий.</p><p className="muted">Закрытие этой панели сохраняет результат и доступную отмену. Новый перенос сбросит их.</p>{!preview.length&&<p>В текущей выдаче нет заданий с известной следующей датой. Уточните подгруппы или обновите расписание.</p>}
      <fieldset className="stack" disabled={busy}>{preview.map(row=><label className="check" key={row.id}><input type="checkbox" checked={selected.includes(row.id)} onChange={event=>setSelected(values=>event.target.checked?[...values,row.id]:values.filter(id=>id!==row.id))}/><span><b>{row.subject}</b><br/>{row.text}<br/>{row.beforeDay} → {row.afterDay}</span></label>)}</fieldset>
      <button className="btn primary" disabled={busy||!selected.length} onClick={()=>void run(preview.filter(row=>selected.includes(row.id)))}>{busy?"Сохраняем сроки…":`Подтвердить перенос · ${selected.length}`}</button>
      {note&&<p role="status" className="banner">{note}</p>}{result&&!busy&&result.failed.length>0&&<button className="btn" onClick={()=>void run(preview.filter(row=>result.failed.includes(row.id)))}>Повторить неудавшиеся переносы</button>}{undo.length>0&&<button className="btn" disabled={busy} onClick={()=>void run(undo,true)}>Отменить сохранённые переносы · {undo.length}</button>}<button className="btn quiet" disabled={busy} onClick={()=>{if((undo.length>0||result)&&!window.confirm("Начать новый перенос? Текущий результат и возможность отменить сохранённые переносы будут сброшены. Сами задания останутся без изменений."))return;newPreview();}}>Новый перенос</button><button className="btn quiet" disabled={busy} onClick={()=>setOpen(false)}>Закрыть</button>
    </div></Sheet>}
  </>;
}
