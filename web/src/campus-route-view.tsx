import { useEffect, useMemo, useRef, useState } from "react";
import { campusPlaces, computeCampusRoute, parseCampusGraph, resolveCampusClassroom, type CampusGraph, type CampusNode, type CampusRoute } from "./campus-routing";
import type { MapPlan, PublicMapAsset } from "./types";
import { Sheet } from "./sheet";
import { SearchField, useBrowseValue, useClock } from "./ux300-controls";
import { rememberRoute, validRouteHistory, togglePinnedPlace, validPinnedPlaces, type RememberedRoute } from "./route-memory";

export function CampusRouteView({ asset, plan, plans, classroom, onPlan, onMark, onRoute }: {
  asset: PublicMapAsset; plan: MapPlan | null; plans: MapPlan[]; classroom: string;
  onPlan: (plan: MapPlan) => void; onMark: (node: CampusNode) => void; onRoute: (route: CampusRoute | null) => void;
}) {
  const [graph,setGraph]=useState<CampusGraph|null>(null);
  const [error,setError]=useState(""); const [retry,setRetry]=useState(0);
  const [from,setFrom]=useState(""); const [to,setTo]=useState("");
  const [field,setField]=useState<"from"|"to"|null>(null);
  const [query,setQuery]=useState(""); const [floorOnly,setFloorOnly]=useState(false);
  const [recent,setRecent]=useState<string[]>([]); const [limit,setLimit]=useState(20);
  const [undo,setUndo]=useState<{field:"from"|"to";id:string}|null>(null);
  const [step,setStep]=useState(0); const [copyNote,setCopyNote]=useState("");
  const [history,setHistory]=useBrowseValue<RememberedRoute[]>("campus-route-history",[]);
  const [pinned,setPinned]=useBrowseValue<string[]>("campus-pinned-places",[]);
  const now=useClock(); const routeRef=useRef(onRoute); routeRef.current=onRoute;
  useEffect(()=>{let active=true;const abort=new AbortController();setError("");
    const url=new URL(asset.url,window.location.origin);
    if(url.origin!==window.location.origin || !url.pathname.startsWith("/api/v1/maps/assets/")){setError("Адрес данных маршрута недоступен. Планы можно смотреть вручную.");return;}
    void fetch(url,{credentials:"same-origin",signal:abort.signal}).then(async response=>{if(!response.ok)throw Error("graph");const data=await response.arrayBuffer();if(data.byteLength!==asset.bytes)throw Error("size");if(crypto.subtle&&asset.sha256){const digest=await crypto.subtle.digest("SHA-256",data);const actual=[...new Uint8Array(digest)].map(value=>value.toString(16).padStart(2,"0")).join("");if(actual.toLowerCase()!==asset.sha256.toLowerCase())throw Error("hash");}return parseCampusGraph(JSON.parse(new TextDecoder().decode(data)));}).then(value=>{if(active)setGraph(value);}).catch(reason=>{if(active&&reason?.name!=="AbortError")setError("Данные маршрутов не загрузились. Выбранный план остаётся доступен.");});
    return()=>{active=false;abort.abort();};
  },[asset.url,asset.sha256,retry]);
  useEffect(()=>{if(graph)setTo(classroom?resolveCampusClassroom(graph,classroom)?.id||"":"");},[graph,classroom]);
  const result=useMemo(()=>graph&&from&&to?computeCampusRoute(graph,from,to):null,[graph,from,to]);
  const route=result?.ok?result.route:null;
  const placeIds=useMemo(()=>new Set(graph?.nodes.filter(node=>node.kind==="room"||node.kind==="entrance").map(node=>node.id)||[]),[graph]);
  const visibleHistory=validRouteHistory(history,placeIds);
  const visiblePins=validPinnedPlaces(pinned,placeIds);
  useEffect(()=>{if(!graph)return;setHistory(rows=>validRouteHistory(rows,placeIds));setPinned(rows=>validPinnedPlaces(rows,placeIds));},[graph]);
  useEffect(()=>{if(route)setHistory(rows=>rememberRoute(rows,{fromId:route.fromId,toId:route.toId}));},[route]);
  useEffect(()=>{setStep(0);setCopyNote("");routeRef.current(route);},[route]);
  useEffect(()=>()=>routeRef.current(null),[]);
  useEffect(()=>setLimit(20),[query,floorOnly,field]);
  const label=(id:string)=>{const node=graph?.nodes.find(node=>node.id===id);return node?`${node.label||node.room||"Место"} · ${node.building}, ${node.floor} этаж`:"Не выбрано";};
  const rows=graph?campusPlaces(graph,query,floorOnly?plan?.building:undefined,floorOnly?plan?.floor:undefined):[];
  function pick(node:CampusNode){if(field==="from")setFrom(node.id);else setTo(node.id);setUndo(null);setRecent(values=>[node.id,...values.filter(id=>id!==node.id)].slice(0,8));setField(null);onMark(node);}
  function reveal(id:string){const node=graph?.nodes.find(node=>node.id===id);if(node)onMark(node);}
  function showStep(index:number){const next=route?.steps[index];if(!next)return;setStep(index);const target=plans.find(plan=>plan.building===next.building&&plan.floor===next.floor);if(target)onPlan(target);}
  return <section className="card stack" aria-label="Маршрут по кампусу"><h2>Маршрут</h2><p className="muted">Выберите точки самостоятельно. Приложение не определяет ваше местоположение.</p>
    {error&&<div className="banner" role="status">{error}<button className="btn" onClick={()=>setRetry(value=>value+1)}>Повторить загрузку маршрутов</button></div>}
    {!graph&&!error&&<p role="status">Загружаем места и переходы…</p>}
    {graph&&<><div className="row"><button className="btn" onClick={()=>setField("from")}>Откуда: {label(from)}</button><button className="btn" onClick={()=>setField("to")}>Куда: {label(to)}</button><button className="btn quiet" disabled={!from&&!to} onClick={()=>{setFrom(to);setTo(from);setUndo(null);}}>Поменять местами</button></div>
      {visibleHistory.length>0&&<details><summary>История маршрутов · {visibleHistory.length}</summary><p className="muted">До шести маршрутов этого профиля в текущей вкладке. Между устройствами не синхронизируются.</p><div className="stack">{visibleHistory.map(row=><button className="btn quiet" key={JSON.stringify(row)} onClick={()=>{setFrom(row.fromId);setTo(row.toId);setUndo(null);}}>{label(row.fromId)} → {label(row.toId)}</button>)}<button className="btn quiet" onClick={()=>setHistory([])}>Очистить историю маршрутов</button></div></details>}
      <div className="row">{from&&<button className="btn quiet" onClick={()=>reveal(from)}>Показать начало</button>}{to&&<button className="btn quiet" onClick={()=>reveal(to)}>Показать назначение</button>}<button className="btn quiet" onClick={()=>{setFloorOnly(true);setField("to");setQuery("");}}>Помещения текущего этажа</button></div>
      {undo&&<div className="banner row"><span>Точка убрана</span><button className="btn" onClick={()=>{if(undo.field==="from")setFrom(undo.id);else setTo(undo.id);setUndo(null);}}>Вернуть точку</button></div>}
      {result&&!result.ok&&<div className="banner" role="status">Между выбранными точками нет доступного маршрута.<button className="btn" onClick={()=>setField("from")}>Изменить начало</button><button className="btn" onClick={()=>setField("to")}>Изменить назначение</button></div>}
      {route&&<div className="stack"><p><b>{Math.ceil(route.durationSeconds/60)} мин</b> · ориентировочно к {new Date(now.getTime()+route.durationSeconds*1000).toLocaleTimeString("ru-RU",{hour:"2-digit",minute:"2-digit"})}</p><p className="muted">Оценка по схеме кампуса; она не учитывает текущую скорость и обстановку.</p>{route.steps.length===0?<p>Начало и назначение совпадают.</p>:<><div className="card stack"><p>Шаг {step+1} из {route.steps.length}: {route.steps[step]?.instruction}</p><div className="row"><button className="btn" disabled={step===0} onClick={()=>showStep(step-1)}>Предыдущий шаг</button><button className="btn" disabled={step>=route.steps.length-1} onClick={()=>showStep(step+1)}>Следующий шаг</button></div></div><details><summary>Все шаги маршрута</summary><ol>{route.steps.map((item,index)=><li key={index}><button className="btn quiet" aria-current={step===index?"step":undefined} onClick={()=>showStep(index)}>{item.instruction}</button></li>)}</ol></details></>}
      <button className="btn quiet" onClick={()=>void navigator.clipboard.writeText([`${label(from)} → ${label(to)}`,`Примерно ${Math.ceil(route.durationSeconds/60)} мин`,...route.steps.map((item,index)=>`${index+1}. ${item.instruction}`)].join("\n")).then(()=>setCopyNote("Текст маршрута скопирован")).catch(()=>setCopyNote("Не удалось скопировать. Текст шагов можно выделить вручную."))}>Скопировать маршрут</button>{copyNote&&<p role="status">{copyNote}</p>}</div>}
    </>}
    {field&&graph&&<Sheet title={field==="from"?"Начало маршрута":"Назначение"} onClose={()=>setField(null)}><div className="stack"><div className="row"><button className="btn" aria-pressed={field==="from"} onClick={()=>setField("from")}>Откуда</button><button className="btn" aria-pressed={field==="to"} onClick={()=>setField("to")}>Куда</button></div><SearchField label="Найти аудиторию или вход" value={query} onChange={setQuery}/><label className="check"><input type="checkbox" checked={floorOnly} onChange={event=>setFloorOnly(event.target.checked)}/>Только {plan?.building}, {plan?.floor} этаж</label>{(field==="from"?from:to)&&<button className="btn quiet" onClick={()=>{const id=field==="from"?from:to;setUndo({field,id});if(field==="from")setFrom("");else setTo("");setField(null);}}>Убрать выбранную точку</button>}
      {!query&&visiblePins.length>0&&<div className="stack"><h3>Закреплённые места · {visiblePins.length} из 8</h3><p className="muted">Локальный выбор этого профиля в текущей вкладке.</p>{visiblePins.map(id=>graph.nodes.find(node=>node.id===id)!).map(node=><div className="row" key={node.id}><button className="btn" onClick={()=>pick(node)}>{label(node.id)}</button><button className="btn quiet" aria-label={`Открепить ${label(node.id)}`} onClick={()=>setPinned(rows=>togglePinnedPlace(rows,node.id))}>Открепить</button></div>)}</div>}
      {!query&&recent.length>0&&<div className="stack"><h3>Недавние места</h3><button className="btn quiet" onClick={()=>setRecent([])}>Очистить недавние места</button>{recent.map(id=>graph.nodes.find(node=>node.id===id)).filter((node):node is CampusNode=>!!node).map(node=><button className="btn quiet" key={node.id} onClick={()=>pick(node)}>{label(node.id)}</button>)}</div>}
      <p className="muted">Найдено мест: {rows.length}</p>{rows.slice(0,limit).map(node=><div className="row" key={node.id}><button className="btn" onClick={()=>pick(node)}>{label(node.id)}</button><button className="btn quiet" disabled={!pinned.includes(node.id)&&visiblePins.length>=8} aria-label={`${pinned.includes(node.id)?"Открепить":"Закрепить"} ${label(node.id)}`} onClick={()=>setPinned(values=>togglePinnedPlace(values,node.id))}>{pinned.includes(node.id)?"Открепить":"Закрепить"}</button></div>)}{rows.length===0&&<button className="btn" onClick={()=>{setQuery("");setFloorOnly(false);}}>Показать все места кампуса</button>}{rows.length>limit&&<button className="btn quiet" onClick={()=>setLimit(value=>value+20)}>Показать ещё</button>}</div></Sheet>}
  </section>;
}
