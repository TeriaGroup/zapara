import { useEffect, useState, type SetStateAction } from "react";
import { useApp } from "./store";

const browseMemory=new Map<string,unknown>();
/** Session-local list controls only. Account, session and selected group are always part of the key. */
export function useBrowseValue<T>(key:string,initial:T):[T,(next:SetStateAction<T>)=>void]{
  const app=useApp();const scope=JSON.stringify([app.session?.user?.userId||"guest",app.session?.familyId||"",app.groupId,key]);
  const [state,setState]=useState(()=>({scope,value:(browseMemory.get(scope) as T|undefined)??initial}));
  const value=state.scope===scope?state.value:(browseMemory.get(scope) as T|undefined)??initial;
  const update=(next:SetStateAction<T>)=>{const current=(browseMemory.get(scope) as T|undefined)??value;const result=typeof next==="function"?(next as (value:T)=>T)(current):next;browseMemory.set(scope,result);if(browseMemory.size>200)browseMemory.delete(browseMemory.keys().next().value!);setState({scope,value:result});};
  return [value,update];
}
export function focusElement(id:string){const node=document.getElementById(id);if(node){node.tabIndex=-1;node.focus({preventScroll:true});node.scrollIntoView({block:"center"});}}

export function SearchField({ value, onChange, label, placeholder }: { value: string; onChange: (value: string) => void; label: string; placeholder?: string }) {
  return <div className="row"><label className="field" style={{ flex: "1 1 220px" }}>{label}<input type="search" value={value} placeholder={placeholder} onChange={event => onChange(event.target.value)} /></label>{value && <button className="btn quiet" type="button" onClick={() => onChange("")}>Очистить поиск</button>}</div>;
}
export function FilterEmpty({ onReset, text = "По выбранным условиям ничего не найдено." }: { onReset: () => void; text?: string }) {
  return <div className="card stack" role="status"><p>{text}</p><button className="btn" type="button" onClick={onReset}>Показать все</button></div>;
}
export function useClock(interval = 30_000) {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => { const update = () => setNow(new Date()); const timer = window.setInterval(update, interval); document.addEventListener("visibilitychange", update); return () => { window.clearInterval(timer); document.removeEventListener("visibilitychange", update); }; }, [interval]);
  return now;
}
export function useConnectivity() {
  const [online, setOnline] = useState(() => navigator.onLine);
  useEffect(() => { const update = () => setOnline(navigator.onLine); window.addEventListener("online", update); window.addEventListener("offline", update); return () => { window.removeEventListener("online", update); window.removeEventListener("offline", update); }; }, []);
  return online;
}
export function SecretInput({ value, onChange, disabled, autoComplete = "off", label }: { value: string; onChange: (value: string) => void; disabled?: boolean; autoComplete?: string; label: string }) {
  const [shown, setShown] = useState(false);
  useEffect(()=>{if(!value)setShown(false);},[value]);
  return <label className="field">{label}<span className="row"><input style={{ flex: "1 1 180px", minWidth: 0 }} type={shown ? "text" : "password"} value={value} disabled={disabled} autoComplete={autoComplete} onChange={event => onChange(event.target.value)} /><button className="btn quiet" type="button" disabled={disabled} aria-pressed={shown} onClick={() => setShown(value => !value)}>{shown ? "Скрыть" : "Показать"}</button></span></label>;
}

const positions = new Map<string, number>();
function hasFocusedRouteTarget(){const active=document.activeElement;return !!active&&!!document.querySelector('.stage')?.contains(active)&&active.matches('[tabindex="-1"]:not(h1)');}
export function focusRouteHeading(){const heading=document.querySelector<HTMLElement>('.stage h1');if(heading&&!document.querySelector('[role="dialog"]')&&!hasFocusedRouteTarget()){heading.tabIndex=-1;heading.focus({preventScroll:true});}}
/** The map contains scroll positions only, never fields or private content. */
export function useRoutePosition(key: string) {
  useEffect(() => {
    const current = key;
    const at = positions.get(current) ?? 0;
    let restoring=true;
    const stop=()=>{restoring=false;observer.disconnect();};
    const restore=()=>{if(!restoring)return;if(hasFocusedRouteTarget()){stop();return;}if(document.documentElement.scrollHeight-window.innerHeight>=at){window.scrollTo({top:at});stop();}};
    const observer=new MutationObserver(restore);
    const frame = requestAnimationFrame(restore);
    observer.observe(document.querySelector(".stage")??document.body,{childList:true,subtree:true});
    const timeout=window.setTimeout(stop,5000);
    window.addEventListener("wheel",stop,{passive:true});window.addEventListener("pointerdown",stop,{passive:true});window.addEventListener("keydown",stop);
    const save = () => {if(!restoring)positions.set(current, window.scrollY);};
    window.addEventListener("scroll", save, { passive: true });
    return () => { stop();window.clearTimeout(timeout);cancelAnimationFrame(frame); window.removeEventListener("scroll", save);window.removeEventListener("wheel",stop);window.removeEventListener("pointerdown",stop);window.removeEventListener("keydown",stop); };
  }, [key]);
}
