import { ReactNode, useState } from "react";
import { Icon } from "./icons";
import { Sheet } from "./sheet";

export type HomeworkAction = { label: string; icon?: Parameters<typeof Icon>[0]["name"]; onSelect: () => void } | { node: ReactNode };

/**
 * Действия задания в «⋯» (#15): на карточке остаются только предмет, текст, срок и чекбокс.
 * «Удалить» — опасное действие, последним пунктом и только после подтверждения.
 */
export function HomeworkActions({ title, actions, onDelete }: { title: string; actions: HomeworkAction[]; onDelete?: () => void }) {
  const [open, setOpen] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const close = () => { setOpen(false); setConfirm(false); };
  return <>
    <button className="icon-btn quiet homework-more" type="button" aria-haspopup="dialog" aria-expanded={open} aria-label={`Действия: ${title}`} onClick={() => setOpen(true)}><Icon name="more" size={18} /></button>
    {open && <Sheet title={confirm ? "Удалить задание?" : "Действия с заданием"} onClose={close}>
      {!confirm ? <div className="homework-action-list">
        <p className="muted homework-action-title">{title}</p>
        {actions.map((action, index) => "node" in action ? <div key={index}>{action.node}</div>
          : <button className="btn quiet" type="button" key={action.label} onClick={() => { close(); action.onSelect(); }}>{action.icon && <Icon name={action.icon} size={16} />}{action.label}</button>)}
        {onDelete && <button className="btn quiet danger homework-delete" type="button" onClick={() => setConfirm(true)}><Icon name="trash" size={16} />Удалить</button>}
      </div> : <div className="stack" role="alertdialog" aria-label="Подтверждение удаления">
        <p>«{title}» будет удалено с этого устройства и из синхронизации. Отменить удаление нельзя.</p>
        <div className="row homework-confirm-row">
          <button className="btn" type="button" onClick={() => setConfirm(false)}>Отмена</button>
          <button className="btn primary danger-solid" type="button" onClick={() => { close(); onDelete?.(); }}>Удалить</button>
        </div>
      </div>}
    </Sheet>}
  </>;
}
