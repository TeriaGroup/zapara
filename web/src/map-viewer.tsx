import { useEffect, useRef, useState } from "react";
import { boundedPan } from "./map-viewport";
import { Icon } from "./icons";
import type { MapPlan } from "./types";

export function MapViewer({ plan, onNextFloor, onPreviousFloor }: { plan: MapPlan; onNextFloor: () => void; onPreviousFloor: () => void }) {
  const frame = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });
  const [natural, setNatural] = useState({ width: 0, height: 0 });
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
  const [retry, setRetry] = useState(0);
  const [shownPlan, setShownPlan] = useState<MapPlan | null>(null);
  const accessiblePlan = shownPlan ?? plan;
  useEffect(() => {
    let stopped = false;
    setState("loading");
    const candidate = new Image();
    candidate.onload = () => { if (!stopped) { setNatural({ width: candidate.naturalWidth, height: candidate.naturalHeight }); setShownPlan(plan); setState("ready"); setZoom(1); setPan({ x: 0, y: 0 }); } };
    candidate.onerror = () => { if (!stopped) setState("error"); };
    candidate.src = plan.url;
    return () => { stopped = true; candidate.onload = null; candidate.onerror = null; };
  }, [plan.id, plan.url, retry]);
  const [full, setFull] = useState(false);
  const [fullError, setFullError] = useState("");
  const drag = useRef<{ id: number; x: number; y: number; startX: number; startY: number } | null>(null);
  const swipe = useRef<{ x: number; y: number } | null>(null);
  useEffect(() => { const node = frame.current; if (!node) return; const observer = new ResizeObserver(() => setSize({ width: node.clientWidth, height: node.clientHeight })); observer.observe(node); return () => observer.disconnect(); }, []);
  useEffect(() => { const changed = () => setFull(document.fullscreenElement === frame.current); document.addEventListener("fullscreenchange", changed); return () => document.removeEventListener("fullscreenchange", changed); }, []);
  const fit = natural.width ? Math.min((size.width - 32) / natural.width, (size.height - 32) / natural.height, 1) : 1;
  const image = { width: natural.width * Math.max(0, fit), height: natural.height * Math.max(0, fit) };
  useEffect(() => { setPan(current => boundedPan(current.x, current.y, zoom, image, size)); }, [zoom, image.width, image.height, size.width, size.height]);
  function reset() { setZoom(1); setPan({ x: 0, y: 0 }); }
  function changeZoom(delta: number) { setZoom(value => Math.max(1, Math.min(4, Math.round((value + delta) * 100) / 100))); }
  return <div className="map-viewer">
    <div className="map-viewer-tools" role="group" aria-label="Масштаб плана">
      <button className="icon-btn" disabled={!shownPlan || zoom === 1} aria-label="Уменьшить план" onClick={() => changeZoom(-.25)}><Icon name="minus" /></button>
      <span className="map-scale" aria-live="polite">{Math.round(zoom * 100)}%</span>
      <button className="icon-btn" disabled={!shownPlan || zoom === 4} aria-label="Увеличить план" onClick={() => changeZoom(.25)}><Icon name="plus" /></button>
      <button className="btn quiet" disabled={!shownPlan} onClick={reset}><Icon name="fit" />Вписать</button>
      <button className="btn quiet map-fullscreen" onClick={() => { setFullError(""); const action = full ? document.exitFullscreen() : frame.current?.requestFullscreen(); void action?.catch(() => setFullError("Полноэкранный режим недоступен в этом браузере.")); }}><Icon name="expand" />{full ? "Выйти из полного экрана" : "На весь экран"}</button>
    </div>
    {fullError && <p role="status" className="muted">{fullError}</p>}
    {shownPlan && state !== "ready" && <div className="banner row" role="status"><span>{state === "error" ? "Новый план не загрузился." : "Загружаем выбранный план…"} Показан {shownPlan.building}, {shownPlan.floor} этаж.</span>{state === "error" && <button className="btn" type="button" onClick={() => setRetry(value => value + 1)}>Повторить</button>}</div>}
    <div ref={frame} className={"map-frame map-viewport" + (zoom > 1 ? " zoomed" : "")} tabIndex={0} role="region" aria-label={`План: ${accessiblePlan.building}, ${accessiblePlan.floor} этаж. Плюс и минус — масштаб, стрелки — перемещение, ноль — вписать.`}
      onKeyDown={event => { if (event.key === "+" || event.key === "=") changeZoom(.25); else if (event.key === "-") changeZoom(-.25); else if (event.key === "0") reset(); else if (["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) setPan(current => boundedPan(current.x + (event.key === "ArrowLeft" ? 48 : event.key === "ArrowRight" ? -48 : 0), current.y + (event.key === "ArrowUp" ? 48 : event.key === "ArrowDown" ? -48 : 0), zoom, image, size)); else return; event.preventDefault(); }}
      onPointerDown={event => { if (!shownPlan || event.button !== 0 || (event.target as HTMLElement).closest("button")) return; if (zoom === 1) { if (event.pointerType !== "mouse") swipe.current = { x: event.clientX, y: event.clientY }; return; } event.currentTarget.setPointerCapture(event.pointerId); drag.current = { id: event.pointerId, x: event.clientX, y: event.clientY, startX: pan.x, startY: pan.y }; }}
      onPointerMove={event => { const start = drag.current; if (!start || start.id !== event.pointerId) return; setPan(boundedPan(start.startX + event.clientX - start.x, start.startY + event.clientY - start.y, zoom, image, size)); }}
      onPointerUp={event => { drag.current = null; const start = swipe.current; swipe.current = null; if (!start || zoom !== 1) return; const dx = event.clientX - start.x, dy = event.clientY - start.y; if (Math.abs(dx) < 64 || Math.abs(dx) < Math.abs(dy)) return; if (dx < 0) onNextFloor(); else onPreviousFloor(); }} onPointerCancel={() => { drag.current = null; swipe.current = null; }}>
      {shownPlan && <img src={shownPlan.url} draggable={false} alt={`${shownPlan.building}, ${shownPlan.floor} этаж`} style={{ width: image.width || undefined, height: image.height || undefined, transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})` }} />}
      {!shownPlan && <div className="map-status" role="status"><Icon name="map" size={32} /><h2>{state === "loading" ? "Загружаем план" : "План не загрузился"}</h2>{state === "error" && <><p>Проверьте подключение и попробуйте ещё раз.</p><button className="btn" onClick={() => { setState("loading"); setRetry(value => value + 1); }}>Повторить</button></>}</div>}
      {full && <button className="btn map-exit" onClick={() => void document.exitFullscreen()}>Закрыть полный экран</button>}
    </div>
    <p className="map-help muted">{zoom === 1 ? "Увеличьте план, чтобы рассмотреть аудитории. На сенсорном экране смахните, чтобы сменить этаж." : "Перетаскивайте увеличенный план или используйте стрелки на клавиатуре. «Вписать» вернёт весь этаж."}</p>
  </div>;
}
