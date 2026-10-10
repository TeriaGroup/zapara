// Базовые компоненты (#11, эпик #8). Стили — ui.css, только переменные --zp-* из tokens.css.
import { forwardRef, useEffect, useId, useRef, type ButtonHTMLAttributes, type InputHTMLAttributes, type ReactNode } from "react";
import { lessonKind, lessonKindLabels } from "./lesson-kind.ts";

const cx = (...parts: (string | false | null | undefined)[]) => parts.filter(Boolean).join(" ");

export type ButtonVariant = "primary" | "secondary" | "ghost" | "danger";
export const Button = forwardRef<HTMLButtonElement, ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant }>(
  function Button({ variant = "secondary", className, type = "button", ...rest }, ref) {
    return <button {...rest} ref={ref} type={type} className={cx("zp-btn", `zp-btn-${variant}`, className)} />;
  });

/** Поле с постоянной подписью, подсказкой и отдельной ошибкой (не плейсхолдер вместо подписи). */
export function Input({ label, hint, error, className, id, ...rest }: InputHTMLAttributes<HTMLInputElement> & { label: string; hint?: string; error?: string }) {
  const auto = useId();
  const inputId = id ?? auto;
  const described = [hint && `${inputId}-hint`, error && `${inputId}-error`].filter(Boolean).join(" ") || undefined;
  return (
    <div className={cx("zp-field", error && "zp-field-invalid", className)}>
      <label className="zp-label" htmlFor={inputId}>{label}</label>
      <input {...rest} id={inputId} className="zp-input" aria-invalid={error ? true : undefined} aria-describedby={described} />
      {hint && <span className="zp-hint" id={`${inputId}-hint`}>{hint}</span>}
      {error && <span className="zp-error" id={`${inputId}-error`} role="alert">{error}</span>}
    </div>
  );
}

export function SegmentedControl<T extends string>({ label, options, value, onChange }: {
  label: string; options: { value: T; label: string }[]; value: T; onChange: (value: T) => void;
}) {
  return (
    <div className="zp-segmented" role="radiogroup" aria-label={label}>
      {options.map(option => (
        <button key={option.value} type="button" role="radio" aria-checked={option.value === value}
          className={cx("zp-segment", option.value === value && "zp-segment-on")}
          onClick={() => onChange(option.value)}>{option.label}</button>
      ))}
    </div>
  );
}

export type ChipTone = "neutral" | "success" | "warning" | "danger" | "info";
/** Чип состояния или типа пары. Смысл всегда несёт текст, цвет — только точка и подложка. */
export function Chip({ children, tone = "neutral", lesson }: { children?: ReactNode; tone?: ChipTone; lesson?: string }) {
  const kind = lesson === undefined ? "" : lessonKind(lesson);
  return (
    <span className={cx("zp-chip", lesson !== undefined ? kind && `zp-lesson-${kind}` : tone !== "neutral" && `zp-chip-${tone}`)}>
      {(lesson !== undefined ? !!kind : tone !== "neutral") && <i aria-hidden="true" />}
      {lesson !== undefined ? (kind ? lessonKindLabels[kind] : lesson) : children}
    </span>
  );
}

export function Card({ children, title, className, as: Tag = "section" }: { children?: ReactNode; title?: string; className?: string; as?: "section" | "article" | "div" }) {
  return <Tag className={cx("zp-card", className)}>{title && <h2 className="zp-card-title">{title}</h2>}{children}</Tag>;
}

export type LessonState = "now" | "next" | "past";
const stateLabels: Record<LessonState, string> = { now: "Идёт сейчас", next: "Следующая", past: "Прошла" };
export function LessonRow({ start, end, subject, type, room, teacher, state }: {
  start: string; end: string; subject: string; type: string; room?: string; teacher?: string; state?: LessonState;
}) {
  return (
    <article className={cx("zp-lesson", state && `zp-lesson-${state}`)} aria-label={`${start}–${end}, ${subject}${state ? `, ${stateLabels[state].toLowerCase()}` : ""}`}>
      <div className="zp-lesson-time"><span>{start}</span><span>{end}</span></div>
      <div className="zp-lesson-body">
        <div className="zp-lesson-head"><strong className="zp-lesson-subject">{subject}</strong>{state && state !== "past" && <span className="zp-lesson-state">{stateLabels[state]}</span>}</div>
        <div className="zp-lesson-meta"><Chip lesson={type} />{room && <span>{room}</span>}{teacher && <span>{teacher}</span>}</div>
      </div>
    </article>
  );
}

export function EmptyState({ title, hint, action }: { title: string; hint?: string; action?: ReactNode }) {
  return (
    <div className="zp-empty" role="status">
      <p className="zp-empty-title">{title}</p>
      {hint && <p className="zp-empty-hint">{hint}</p>}
      {action && <div className="zp-empty-action">{action}</div>}
    </div>
  );
}

/**
 * Подтверждение действия. Фокус — на «Отмена», Escape и фон закрывают, после закрытия фокус возвращается.
 * Для опасного действия кнопка подтверждения красная и называет действие («Удалить», не «ОК»).
 */
export function ConfirmDialog({ open, title, body, confirmLabel, cancelLabel = "Отмена", danger = false, onConfirm, onCancel }: {
  open: boolean; title: string; body?: ReactNode; confirmLabel: string; cancelLabel?: string; danger?: boolean;
  onConfirm: () => void; onCancel: () => void;
}) {
  const titleId = useId();
  const cancel = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement as HTMLElement | null;
    cancel.current?.focus();
    const onKey = (event: KeyboardEvent) => { if (event.key === "Escape") { event.preventDefault(); onCancel(); } };
    document.addEventListener("keydown", onKey);
    return () => { document.removeEventListener("keydown", onKey); previous?.focus?.(); };
  }, [open, onCancel]);
  if (!open) return null;
  return (
    <div className="zp-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) onCancel(); }}>
      <div className="zp-dialog" role="alertdialog" aria-modal="true" aria-labelledby={titleId}>
        <h2 className="zp-dialog-title" id={titleId}>{title}</h2>
        {body && <div className="zp-dialog-body">{body}</div>}
        <div className="zp-dialog-actions">
          <Button ref={cancel} variant="ghost" onClick={onCancel}>{cancelLabel}</Button>
          <Button variant={danger ? "danger" : "primary"} onClick={onConfirm}>{confirmLabel}</Button>
        </div>
      </div>
    </div>
  );
}
