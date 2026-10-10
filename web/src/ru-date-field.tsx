import { useEffect, useRef, useState } from "react";
import { Icon } from "./icons";
import { isoDay } from "./parity";
import { localDay } from "./planner";
import { formatRuDate, parseRuDate } from "./week-format";

/**
 * Поле даты всегда в формате «дд.мм.гггг», независимо от языка браузера (#14, W-04):
 * нативный `<input type="date">` показывает формат ОС («10/09/2026»), поэтому он только открывает календарь.
 */
export function RuDateField({ label, value, onChange }: { label: string; value: Date; onChange: (date: Date) => void }) {
  const [text, setText] = useState(formatRuDate(value));
  const [invalid, setInvalid] = useState(false);
  const picker = useRef<HTMLInputElement>(null);
  useEffect(() => { setText(formatRuDate(value)); setInvalid(false); }, [isoDay(value)]);
  function commit(raw: string) {
    const date = parseRuDate(raw);
    if (date) { setInvalid(false); onChange(date); } else setInvalid(raw.trim() !== "");
  }
  return <div className="field ru-date-field">
    <label htmlFor="ru-date-input">{label}</label>
    <div className="ru-date-row">
      <input id="ru-date-input" lang="ru" inputMode="numeric" placeholder="дд.мм.гггг" value={text} aria-invalid={invalid || undefined} maxLength={10}
        onChange={event => { setText(event.target.value); if (parseRuDate(event.target.value)) commit(event.target.value); }}
        onBlur={event => commit(event.target.value)} onKeyDown={event => { if (event.key === "Enter") commit(event.currentTarget.value); }} />
      <button className="icon-btn quiet" type="button" aria-label="Открыть календарь" onClick={() => { const node = picker.current; if (!node) return; try { node.showPicker(); } catch { node.focus(); } }}><Icon name="calendar" size={18} /></button>
      <input ref={picker} className="ru-date-native" type="date" tabIndex={-1} aria-hidden="true" lang="ru" value={isoDay(value)}
        onChange={event => { const date = localDay(event.target.value); if (date) onChange(date); }} />
    </div>
    {invalid && <span className="ru-date-error" role="alert">Введите дату как 09.10.2026</span>}
  </div>;
}
