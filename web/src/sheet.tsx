import { ReactNode } from "react";
import { useSheetDismiss } from "./swipe";

export function Sheet({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const dismiss = useSheetDismiss(onClose);
  return <div className="sheet" role="dialog" aria-modal="true" aria-label={title} onClick={onClose}>
    <div className="card" onClick={event => event.stopPropagation()}>
      <div className="sheet-heading" {...dismiss}>
        <span className="sheet-handle" aria-hidden="true" />
        <div className="row"><h2>{title}</h2><button className="btn quiet" type="button" autoFocus onClick={onClose}>Закрыть</button></div>
      </div>
      {children}
    </div>
  </div>;
}
