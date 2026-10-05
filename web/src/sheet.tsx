import { ReactNode, useEffect, useRef } from "react";
import { useSheetDismiss } from "./swipe";

const openSheets: HTMLElement[] = [];
let bodyOverflow = "";
export function Sheet({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const dismiss = useSheetDismiss(onClose);
  const root = useRef<HTMLDivElement>(null);
  const origin = useRef(document.activeElement instanceof HTMLElement ? document.activeElement : null);
  const close = useRef(onClose); close.current = onClose;
  useEffect(() => {
    const node = root.current;
    if (!node) return;
    if (openSheets.length === 0) { bodyOverflow = document.body.style.overflow; document.body.style.overflow = "hidden"; }
    openSheets.push(node);
    const focusable = () => [...node.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),select:not(:disabled),textarea:not(:disabled),a[href],[tabindex="0"]')].filter(item => item.getClientRects().length > 0 && !item.closest('[hidden]'));
    focusable()[0]?.focus();
    const keys = (event: KeyboardEvent) => {
      if (openSheets.at(-1) !== node) return;
      if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close.current(); return; }
      if (event.key !== "Tab") return;
      const items = focusable();
      if (!items.length) { event.preventDefault(); node.focus(); return; }
      const at = items.indexOf(document.activeElement as HTMLElement);
      if (event.shiftKey && at <= 0) { event.preventDefault(); items.at(-1)?.focus(); }
      else if (!event.shiftKey && (at < 0 || at === items.length - 1)) { event.preventDefault(); items[0].focus(); }
    };
    document.addEventListener("keydown", keys, true);
    return () => {
      document.removeEventListener("keydown", keys, true);
      const at = openSheets.indexOf(node); if (at >= 0) openSheets.splice(at, 1);
      if (openSheets.length === 0) document.body.style.overflow = bodyOverflow;
      if (origin.current?.isConnected) origin.current.focus();
      else (openSheets.at(-1) ?? document.querySelector<HTMLElement>(".stage h1"))?.focus();
    };
  }, []);
  return <div ref={root} className="sheet" role="dialog" aria-modal="true" aria-label={title} tabIndex={-1} onClick={onClose}>
    <div className="card" onClick={event => event.stopPropagation()}>
      <div className="sheet-heading" {...dismiss}>
        <span className="sheet-handle" aria-hidden="true" />
        <div className="row"><h2>{title}</h2><button className="btn quiet" type="button" onClick={onClose}>Закрыть</button></div>
      </div>
      {children}
    </div>
  </div>;
}
