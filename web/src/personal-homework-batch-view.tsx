import { useLayoutEffect, useRef, useState } from "react";
import { useApp } from "./store";
import { authGeneration } from "./api";
import { Sheet } from "./sheet";
import { completePersonalBatch, type PersonalBatchResult } from "./personal-homework-batch";
import type { HomeworkItem } from "./types";

export function PersonalHomeworkBatch({items}:{items:HomeworkItem[]}) {
  const app=useApp();const live=useRef(app);live.current=app;
  const [candidates,setCandidates]=useState<HomeworkItem[]|null>(null);const[selected,setSelected]=useState<string[]>([]);
  const [busy,setBusy]=useState(false);const[result,setResult]=useState<PersonalBatchResult|null>(null);
  const active=useRef(true);const pending=useRef(false);
  const scope=JSON.stringify([app.session?.user?.userId||"guest",app.session?.familyId,app.groupId]);const owner=useRef(scope);owner.current=scope;
  useLayoutEffect(()=>{active.current=true;return()=>{active.current=false;};},[]);
  async function complete(ids:string[]){if(pending.current||!ids.length)return;const captured=scope,auth=authGeneration();const current=()=>active.current&&owner.current===captured&&authGeneration()===auth;pending.current=true;setBusy(true);setResult(null);
    const outcome=await completePersonalBatch(ids,{current,read:id=>live.current.homework.find(row=>row.id===id),save:row=>live.current.saveHomework(row),checkpoint:()=>new Promise(resolve=>window.setTimeout(resolve,0))});
    pending.current=false;if(!current())return;setResult(outcome);setSelected(outcome.failed);setBusy(false);
  }
  return <><button className="btn quiet" disabled={!items.some(item=>!item.done)} onClick={()=>{setCandidates(items.filter(item=>!item.done).map(item=>({...item})));setSelected([]);setResult(null);}}>Завершить несколько личных заданий</button>
    {candidates&&<Sheet title="Готовность личных заданий" onClose={()=>{if(!busy)setCandidates(null);}}><div className="stack"><p>Выберите задания из текущей выдачи. Изменится только ваша личная готовность; общая домашка и файлы не копируются.</p><fieldset className="stack" disabled={busy}>{candidates.map(item=><label className="check" key={item.id}><input type="checkbox" checked={selected.includes(item.id)} onChange={event=>setSelected(ids=>event.target.checked?[...ids,item.id]:ids.filter(id=>id!==item.id))}/><span><b>{item.subject}</b><br/>{item.text}</span></label>)}<div className="row"><button className="btn quiet" onClick={()=>setSelected(candidates.map(item=>item.id))}>Выбрать все показанные</button><button className="btn quiet" onClick={()=>setSelected([])}>Снять выбор</button></div></fieldset>
      <button className="btn primary" disabled={busy||!selected.length} onClick={()=>void complete(selected)}>{busy?"Сохраняем отметки…":`Подтвердить готовность · ${selected.length}`}</button>
      {result&&<div role="status" className="banner"><p>На устройстве отмечено: {result.saved.length}. Уже готовы или удалены: {result.skipped.length}. Не сохранилось: {result.failed.length}.</p>{app.session?.authenticated&&<p>Подтверждение сервера придёт через обычную синхронизацию.</p>}{result.failed.length>0&&<button className="btn" disabled={busy} onClick={()=>void complete(result.failed)}>Повторить только неудавшиеся</button>}</div>}
      <button className="btn quiet" disabled={busy} onClick={()=>setCandidates(null)}>Закрыть</button></div></Sheet>}
  </>;
}
