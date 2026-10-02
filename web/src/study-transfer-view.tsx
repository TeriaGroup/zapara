import { useEffect, useRef, useState } from "react";
import * as api from "./api";
import { parseCampusGraph, type CampusGraph } from "./campus-routing";
import { lessonTransfers } from "./study-transfers";
import type { Lesson } from "./types";

export function StudyTransferCheck({lessons}:{lessons:Lesson[]}) {
  const [graph,setGraph]=useState<CampusGraph|null>(null);const [busy,setBusy]=useState(false);const[note,setNote]=useState("");
  const active=useRef(true);const abort=useRef<AbortController|null>(null);
  useEffect(()=>{active.current=true;return()=>{active.current=false;abort.current?.abort();};},[]);
  async function load(){if(busy)return;setBusy(true);setNote("");const request=new AbortController();abort.current=request;
    try{const manifest=await api.loadMaps();if(!active.current)return;const asset=manifest.graph;if(!asset)throw Error("missing");const url=new URL(asset.url,window.location.origin);if(url.origin!==window.location.origin||!url.pathname.startsWith("/api/v1/maps/assets/"))throw Error("url");const response=await fetch(url,{credentials:"same-origin",signal:request.signal});if(!response.ok)throw Error("response");const buffer=await response.arrayBuffer();if(!active.current)return;if(buffer.byteLength!==asset.bytes)throw Error("size");if(crypto.subtle&&asset.sha256){const digest=await crypto.subtle.digest("SHA-256",buffer);if([...new Uint8Array(digest)].map(value=>value.toString(16).padStart(2,"0")).join("").toLowerCase()!==asset.sha256.toLowerCase())throw Error("hash");}const next=parseCampusGraph(JSON.parse(new TextDecoder().decode(buffer)));if(active.current)setGraph(next);
    }catch{if(active.current)setNote("Оценка переходов недоступна. Расписание сохранено; повторите загрузку данных карты.");}finally{if(active.current)setBusy(false);}
  }
  const rows=graph?lessonTransfers(lessons,graph):[];
  return <details className="card stack"><summary>Хватит ли перерыва на переход</summary><p className="muted">Сравнение перерыва с длительностью пути по схеме кампуса. Скорость ходьбы и текущая обстановка неизвестны; запас времени не гарантирован.</p>
    {!graph&&<button className="btn" disabled={busy||lessons.length<2} onClick={()=>void load()}>{busy?"Проверяем переходы…":"Оценить переходы"}</button>}{note&&<p role="status">{note}</p>}{lessons.length<2&&<p>На выбранную дату нужно хотя бы две пары.</p>}
    {rows.map((row,index)=><div className={row.status==="tight"||row.status==="overlap"?"banner":"card"} key={index}><b>{row.previous.subjectRaw} → {row.next.subjectRaw}</b><p>{row.previous.timeEnd}–{row.next.timeStart}{row.availableSeconds!==null?` · перерыв ${Math.max(0,row.availableSeconds/60)} мин`:""}{row.routeSeconds!==null?` · путь около ${Math.ceil(row.routeSeconds/60)} мин`:""}</p><p>{row.status==="tight"?"Расчётный путь длиннее перерыва. Проверьте аудитории и время заранее.":row.status==="overlap"?"Занятия пересекаются по времени. Проверьте подгруппы.":row.status==="unknown"?"Оценки нет: аудитория, путь или однозначная последовательность занятий неизвестны.":"По схеме путь укладывается в перерыв. Оставьте запас на реальный переход."}</p></div>)}
  </details>;
}
