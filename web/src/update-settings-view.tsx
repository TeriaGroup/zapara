import { useEffect, useState } from "react";
import { publicationMemory, publicationProfile } from "./homework-publication-batch";
import { useApp } from "./store";
import { useHomeworkDraft } from "./homework-draft-context";
import { usePersonalDrafts } from "./personal-composer-context";

export type SupportPendingState = { dirty: boolean; busy: boolean };
export function UpdateSettings({supportState={dirty:false,busy:false}}:{supportState?:SupportPendingState}) {
  const app=useApp();const{controller}=useHomeworkDraft();const drafts=usePersonalDrafts();
  const[,changed]=useState(0);useEffect(()=>publicationMemory.subscribe(()=>changed(value=>value+1)),[]);
  const publications=publicationMemory.pending(publicationProfile(app.session?.authenticated?app.session.user?.userId:null,app.session?.familyId));
  const [message,setMessage]=useState("");const[busy,setBusy]=useState(false);
  const unsent=drafts.length+(controller.dirty?1:0)+(supportState.dirty?1:0)+publications.count;
  const sending=supportState.busy||controller.busy||publications.busy;
  return <article className="card stack"><h2>Обновления</h2><p className="muted">Браузер получает новую версию приложения с сервера.</p>
    <button className="btn" disabled={busy} onClick={()=>{setBusy(true);void fetch("/app/index.html",{cache:"no-store",credentials:"same-origin"}).then(response=>{if(!response.ok)throw Error();setMessage("Сервер доступен. Можно перезагрузить приложение.");}).catch(()=>setMessage("Проверка не удалась. Сохранённые данные остаются на устройстве.")).finally(()=>setBusy(false));}}>Проверить обновление</button>
    {message&&<p role="status">{message}</p>}
    {unsent>0&&<p className="banner">Незавершённых черновиков в памяти: {unsent}. Перезагрузка их закроет.</p>}
    {sending&&<p className="banner" role="status">Дождитесь окончания отправки перед перезагрузкой.</p>}
    {app.privateHomework.pending.length>0&&<p className="muted">Локальных изменений в очереди: {app.privateHomework.pending.length}. Они сохранятся на этом устройстве.</p>}
    {message.startsWith("Сервер доступен")&&<button className="btn primary" disabled={sending} onClick={()=>{
      if(sending)return;
      if(unsent&&!window.confirm(`Перезагрузить приложение и закрыть ${unsent} неотправленных черновиков?`))return;
      window.dispatchEvent(new Event("zapara-confirmed-reload"));window.location.reload();
    }}>Перезагрузить приложение</button>}
  </article>;
}
