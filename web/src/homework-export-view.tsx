import { useLayoutEffect, useRef, useState } from "react";
import { createAllDayCalendarExport } from "./calendar-export";
import { homeworkCalendarEntries, homeworkPlanText } from "./homework-export";
import type { PersonalBrowseItem } from "./homework-browse";
import { authGeneration } from "./api";
import { Sheet } from "./sheet";

export function HomeworkExportTools({items,groupId,groupName}:{items:PersonalBrowseItem[];groupId:string;groupName:string}) {
  const [preview,setPreview]=useState<{kind:"calendar"|"text";items:PersonalBrowseItem[];text:string}|null>(null);
  const [note,setNote]=useState("");const [busy,setBusy]=useState(false);
  const active=useRef(true);const pending=useRef(false);const live=useRef(preview);live.current=preview;
  useLayoutEffect(()=>{active.current=true;return()=>{active.current=false;};},[]);
  function open(kind:"calendar"|"text"){setNote("");const snapshot=items.map(item=>({...item,files:item.files?.map(file=>({...file}))}));setPreview({kind,items:snapshot,text:homeworkPlanText(snapshot,groupName)});}
  async function download(){if(!preview||pending.current)return;const captured=preview,auth=authGeneration();const current=()=>active.current&&live.current===captured&&authGeneration()===auth;pending.current=true;setBusy(true);setNote("");
    try{const result=await createAllDayCalendarExport(homeworkCalendarEntries(captured.items,groupId),`Домашка ${groupName}`);if(!current())return;if(!result.eventCount){setNote("Нет заданий с известной календарной датой. Файл не создан.");return;}
      const url=URL.createObjectURL(new Blob([result.content],{type:"text/calendar;charset=utf-8"}));if(!current()){URL.revokeObjectURL(url);return;}const anchor=document.createElement("a");anchor.href=url;anchor.download="voenmeh-homework.ics";anchor.click();window.setTimeout(()=>URL.revokeObjectURL(url),60000);setNote(`Событий: ${result.eventCount} · пропущено без корректной даты или повторов: ${result.skippedCount}. Это снимок, без автоматических обновлений.`);
    }catch{if(current())setNote("Календарь не создан. Задания остались на устройстве.");}finally{pending.current=false;if(current())setBusy(false);}
  }
  return <div className="stack"><div className="row"><button className="btn quiet" disabled={!items.length} onClick={()=>open("calendar")}>Календарь личной домашки</button><button className="btn quiet" disabled={!items.length} onClick={()=>open("text")}>Текст выбранной домашки</button></div>
    {preview&&<Sheet title={preview.kind==="calendar"?"Календарь сроков домашки":"Предпросмотр плана домашки"} onClose={()=>{if(!busy)setPreview(null);}}><div className="stack"><p>Выбрано личных заданий: {preview.items.length}. Общая домашка в этот файл не входит. Вложения представлены только именами.</p>
      {preview.kind==="calendar"&&<p className="muted">Сроки будут событиями на весь день. Задания без известной даты пропускаются; время занятия не выдумывается.</p>}
      <label className="field">Содержимое снимка<textarea readOnly rows={12} value={preview.text}/></label>
      {preview.kind==="calendar"?<button className="btn primary" disabled={busy} onClick={()=>void download()}>{busy?"Готовим календарь…":"Скачать .ics"}</button>:<button className="btn primary" onClick={()=>{const captured=preview;void navigator.clipboard.writeText(captured.text).then(()=>{if(active.current&&live.current===captured)setNote("План скопирован.");}).catch(()=>{if(active.current&&live.current===captured)setNote("Буфер обмена недоступен. Текст можно выделить и скопировать вручную.");});}}>Скопировать план</button>}
      {note&&<p role="status">{note}</p>}<button className="btn quiet" disabled={busy} onClick={()=>setPreview(null)}>Вернуться к заданиям</button></div></Sheet>}
  </div>;
}
