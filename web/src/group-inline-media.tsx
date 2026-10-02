import { useEffect, useRef, useState } from "react";
import * as api from "./api";
import type { GroupMediaDownload } from "./group-media";
import { MediaPlayer, PhotoViewer } from "./media-player";
export function GroupInlineMedia({ download, busy, onDownload }: {
  download: GroupMediaDownload;
  busy: boolean;
  onDownload: () => void;
}) {
  const [visible, setVisible] = useState(false);
  const [source, setSource] = useState<string | null>(null);
  const [error, setError] = useState(false);
  const [retry,setRetry]=useState(0);
  useEffect(()=>{setVisible(false);setSource(null);setError(false);},[download.href]);

  useEffect(() => {
    if (!visible) return;
    setError(false);setSource(null);
    let active = true;
    let objectUrl: string | null = null;
    void api.groupMedia(download).then(blob => {
      if (!active) return;
      if (!blob.size || blob.type === "application/octet-stream") throw new Error("unsupported media");
      objectUrl = URL.createObjectURL(blob);
      setSource(objectUrl);
    }).catch(() => { if (active) setError(true); });
    return () => { active = false; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [visible, download.href, download.kind, download.filename,retry]);

  if(download.kind==="file")return <button className="btn" disabled={busy} onClick={onDownload}>{busy?"Загружаем…":download.label}</button>;

  return (
    <div className="group-inline-media">
      {!visible && <button className="btn" onClick={()=>setVisible(true)}>Загрузить медиа</button>}{visible&&!source&&!error&&<span role="status">Загрузка медиа…</span>}
      {error && <div role="status">Не удалось показать медиа<button className="btn" onClick={()=>setRetry(value=>value+1)}>Повторить загрузку</button></div>}
      {source && !error && download.kind === "image" && <PhotoViewer src={source}/>}
      {source && !error && download.kind === "video" && <MediaPlayer src={source} kind="video"/>}
      {source && !error && download.kind === "voice" && <MediaPlayer src={source}/>}
      {source && !error && download.kind === "circle" && <MediaPlayer src={source} kind="circle"/>}
      <button className="group-media-download" type="button" disabled={busy} onClick={onDownload}>{busy ? "Загрузка…" : download.label}</button>
    </div>
  );
}

export function MaterialMedia({download}:{download:GroupMediaDownload}){
  const [busy,setBusy]=useState(false);const[error,setError]=useState("");const pending=useRef(false);const mounted=useRef(true);const current=useRef(download.href);current.current=download.href;
  useEffect(()=>{mounted.current=true;return()=>{mounted.current=false;};},[]);
  async function save(){if(pending.current)return;pending.current=true;setBusy(true);setError("");const href=download.href;try{const blob=await api.groupMedia(download);if(!mounted.current||current.current!==href)return;const url=URL.createObjectURL(blob);const anchor=document.createElement("a");anchor.href=url;anchor.download=download.filename;anchor.click();window.setTimeout(()=>URL.revokeObjectURL(url),60000);}catch{if(mounted.current&&current.current===href)setError("Этот файл не загрузился. Повторите его загрузку.");}finally{pending.current=false;if(mounted.current)setBusy(false);}}
  return <div className="stack"><GroupInlineMedia download={download} busy={busy} onDownload={()=>void save()}/>{error&&<p role="status">{error}</p>}</div>;
}
