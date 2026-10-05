import { useEffect, useLayoutEffect, useRef, useState } from "react";
import * as api from "./api";
import { useApp } from "./store";
import { Sheet } from "./sheet";
import { HomeworkRecipients, allHomeworkAudience, audienceLabel, useHomeworkAudienceData } from "./homework-audience";
import { audienceMemberIds } from "./homework-audience-policy";
import { scopeLease, scopeLeaseValid } from "./draft-revocation";
import { makePublicationBatch, publishHomeworkBatch, publicationMemory, publicationProfile } from "./homework-publication-batch";
import type { HomeworkAudience, HomeworkItem } from "./types";

export function HomeworkPublication({items,communityId,recipients,dateOf}:{items:HomeworkItem[];communityId:string;recipients:ReturnType<typeof useHomeworkAudienceData>;dateOf:(row:HomeworkItem)=>string|null}){
  const app=useApp();const profile=publicationProfile(app.session?.authenticated?app.session.user?.userId:null,app.session?.familyId);const key=JSON.stringify([app.groupId,communityId]);
  const scope=JSON.stringify([profile,key]);const live=useRef({scope,app,dateOf,recipients});live.current={scope,app,dateOf,recipients};
  const active=useRef(true);const pending=useRef(false);const[,refresh]=useState(0);const[open,setOpen]=useState(false);const[selected,setSelected]=useState<string[]>([]);const[audience,setAudience]=useState<HomeworkAudience>(allHomeworkAudience);const[note,setNote]=useState("");
  useLayoutEffect(()=>{active.current=true;return()=>{active.current=false;};},[]);
  useEffect(()=>publicationMemory.subscribe(()=>refresh(value=>value+1)),[]);
  const batch=publicationMemory.get(profile,key);
  const available=!!app.session?.authenticated&&!!communityId&&recipients.home?.communityId===communityId&&recipients.supported&&!recipients.loading&&!recipients.error;
  const chooseable=items.filter(row=>!row.done).slice(0,50);
  function prepare(){if(!available)return;const rows=selected.map(id=>app.homework.find(row=>row.id===id)).filter((row):row is HomeworkItem=>!!row&&!row.done);if(!rows.length){setNote("Выбранные задания уже выполнены или удалены.");return;}if(audience.kind==="selected"&&(!recipients.home||!recipients.space||!audienceMemberIds(audience,recipients.home,recipients.space).length)){setNote("Выберите хотя бы одного доступного получателя.");return;}publicationMemory.set(profile,key,makePublicationBatch(rows,audience,dateOf));setNote("");}
  async function publish(){if(!batch||pending.current||batch.busy||!available)return;const captured=batch,auth=api.authGeneration();const access={owner:app.session!.user!.userId,community:communityId};const lease=scopeLease(sessionStorage,access);
    const current=()=>active.current&&live.current.scope===scope&&api.authGeneration()===auth&&scopeLeaseValid(sessionStorage,access,lease)&&publicationMemory.get(profile,key)===captured;
    pending.current=true;captured.busy=true;publicationMemory.notify();setNote("");
    try{
      const space=await api.groupSpace(communityId);if(!current())return;const home=await api.groupHome(communityId);if(!current())return;
      if(!space.capabilities.homeworkAudience||home.communityId!==communityId||!home.classmates.some(person=>person.self&&person.userId===access.owner))throw Error("access");
      if(captured.audience.kind==="selected"&&(captured.audience.userIds.some(id=>!home.classmates.some(person=>person.userId===id))||captured.audience.roleIds.some(id=>!space.desk.roles.some(role=>role.roleId===id))||!audienceMemberIds(captured.audience,home,space).length))throw Error("audience");
      await publishHomeworkBatch(captured,{current,read:id=>live.current.app.homework.find(row=>row.id===id),dateOf:row=>live.current.dateOf(row),send:row=>api.shareHomework(communityId,row.payload.title,row.payload.body,row.payload.deadlineAt,null,captured.audience,row.operationId),onChange:()=>publicationMemory.notify()});
      if(current())setNote(`Подтверждено сервером: ${captured.rows.filter(row=>row.status==="published").length} из ${captured.rows.length}.`);
    }catch(error){if(current())setNote(error instanceof Error&&error.message==="audience"?"Состав выбранных получателей изменился. Сначала проверьте группу. Не меняйте исходный запрос неопределённой отправки.":"Публикация не начата или остановлена: не подтверждены доступ, современный протокол или связь. Набор сохранён для проверки и повтора.");}
    finally{pending.current=false;if(publicationMemory.get(profile,key)===captured){captured.busy=false;publicationMemory.notify();}}
  }
  return <><button className="btn quiet" disabled={!batch&&!chooseable.length} onClick={()=>{setOpen(true);setNote("");}}>{batch?"Продолжить публикацию выбранной домашки":"Опубликовать выбранную личную домашку"}</button>
    {open&&<Sheet title="Публикация личной домашки группе" onClose={()=>{if(!batch?.busy)setOpen(false);}}><div className="stack"><p>Группа: {recipients.home?.name||"не подтверждена"}. Создаются общие копии текста и срока. Личные отметки готовности и файлы не передаются.</p>
      {!available&&<div className="banner" role="status">Для безопасного повтора нужны вход, членство в выбранной группе и современная поддержка адресной домашки. {recipients.loading?"Проверяем состав…":"Обновите состав группы и повторите."}<button className="btn" disabled={recipients.loading} onClick={recipients.retry}>Повторить проверку группы</button></div>}
      {!batch?<><p className="muted">До 50 личных невыполненных заданий из текущей выдачи. Выберите записи, затем проверьте неизменяемый снимок публикации.</p><fieldset className="stack" disabled={!available}>{chooseable.map(row=><label className="check" key={row.id}><input type="checkbox" checked={selected.includes(row.id)} onChange={event=>setSelected(ids=>event.target.checked?[...ids,row.id]:ids.filter(id=>id!==row.id))}/><span><b>{row.subject}</b><br/>{row.text}</span></label>)}<HomeworkRecipients communityId={`${communityId}-batch`} value={audience} onChange={setAudience} data={recipients} disabled={!available}/><button className="btn primary" disabled={!selected.length||!available} onClick={prepare}>Предпросмотр публикаций · {selected.length}</button></fieldset></>:
      <><p><b>Получатели снимка: {audienceLabel(batch.audience)}</b></p><p className="muted">Набор хранится в памяти этой вкладки, включая закрытие панели и переходы по приложению. Перезагрузка или выход очищают его. Неопределённые отправки повторяйте здесь; перед новым набором проверьте общую домашку.</p><p className="muted">Известный срок — конец указанного дня по Москве (23:59:59). Неизвестный срок остаётся пустым. После первой попытки повтор использует исходный снимок даже при изменении личного задания.</p>
        {batch.rows.map(row=><article className="card" key={row.operationId}><b>{row.payload.title}</b><p>{row.payload.body}</p><p className="muted">{row.payload.deadlineAt?`Срок: ${new Date(row.payload.deadlineAt).toLocaleString("ru-RU",{timeZone:"Europe/Moscow"})} (Москва)`:"Без срока"} · {row.status==="published"?"Опубликовано":row.status==="uncertain"?"Не подтверждено":row.status==="changed"?"Исходное задание изменилось":"Готово к отправке"}</p>{row.error&&<p role="status">{row.error}</p>}</article>)}
        <button className="btn primary" disabled={!available||batch.busy||batch.rows.every(row=>row.status==="published")} onClick={()=>void publish()}>{batch.busy?"Публикуем…":batch.rows.some(row=>row.attempted)?"Повторить неподтверждённые с прежними номерами":"Подтвердить публикацию группе"}</button>
        <button className="btn quiet" disabled={batch.busy} onClick={()=>{if(batch.rows.some(row=>row.attempted)&&!window.confirm("Некоторые записи могли быть получены группой. Проверьте общую домашку перед новым набором: новый номер отправки создаст новую копию. Отбросить этот набор?"))return;publicationMemory.set(profile,key,null);setSelected([]);setNote("");}}>Начать новый набор</button></>}
      {note&&<p className="banner" role="status">{note}</p>}<button className="btn quiet" disabled={!!batch?.busy} onClick={()=>setOpen(false)}>Закрыть, сохранив набор в памяти</button></div></Sheet>}
  </>;
}
