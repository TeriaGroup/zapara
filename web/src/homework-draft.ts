import type { HomeworkAudience, HomeworkFile } from "./types";

export type PendingHomeworkFile = { file: File; kind: "photo" | "document" };
export type HomeworkDraft = {
  subject: string; text: string; share: boolean; nth: number; sharedDeadline: string;
  deadlineMode: "personal" | "custom" | "none"; audience: HomeworkAudience; topicId: string;
  pending: PendingHomeworkFile[];
};
export type PreparedHomeworkFile = { source: PendingHomeworkFile; file: HomeworkFile; blob: Blob; uploaded: boolean };
export type HomeworkSaveOperation = { id: string; created: string; localSaved: boolean; savedSignature?: string; shareAttempted?: boolean; shareSignature?: string; shareDeadline?: string | null; prepared: PreparedHomeworkFile[] };
export type HomeworkSaveTicket = { epoch: number; draft: HomeworkDraft; operation: HomeworkSaveOperation };
const emptyDraft = (subject = ""): HomeworkDraft => ({ subject, text: "", share: false, nth: 1, sharedDeadline: "", deadlineMode: "personal", audience: { kind: "all", roleIds: [], userIds: [] }, topicId: "", pending: [] });

/** Files stay in memory above the routes; a scope change invalidates every previous async ticket. */
export class HomeworkDraftController {
  draft = emptyDraft();
  busy = false;
  note = "";
  private baseline = "";
  private epoch = 0;
  private scopeId: string;
  private operation: HomeworkSaveOperation | null = null;
  constructor(scopeId: string) { this.scopeId = scopeId; }
  scope(scopeId: string) {
    if (scopeId === this.scopeId) return;
    this.scopeId = scopeId;
    this.epoch++;
    this.busy = false;
    this.operation = null;
    this.note = "";
    this.baseline = "";
    this.draft = emptyDraft();
  }
  get dirty() {
    const row = this.draft;
    return !!this.operation?.localSaved || row.subject.trim() !== this.baseline.trim() || !!row.text.trim()
      || row.share || row.nth !== 1 || !!row.sharedDeadline || row.deadlineMode !== "personal" || row.audience.kind !== "all" || !!row.topicId || row.pending.length > 0;
  }
  field<K extends keyof HomeworkDraft>(name: K, value: HomeworkDraft[K]) {
    if (this.busy) return;
    this.draft = { ...this.draft, [name]: value };
  }
  preload(subject: string) {
    if (this.busy || this.dirty) return;
    this.baseline = subject;
    this.draft = emptyDraft(subject);
  }
  clear(subject = "") {
    if (this.busy) return;
    this.epoch++;
    this.operation = null;
    this.baseline = subject;
    this.draft = emptyDraft(subject);
    this.note = "Черновик очищен";
  }
  begin(): HomeworkSaveTicket | null {
    if (this.busy) return null;
    this.busy = true;
    this.note = "Сохраняем задание…";
    this.operation ??= { id: crypto.randomUUID(), created: new Date().toISOString(), localSaved: false, prepared: [] };
    return { epoch: this.epoch, draft: { ...this.draft, pending: [...this.draft.pending] }, operation: this.operation };
  }
  current(ticket: HomeworkSaveTicket) { return ticket.epoch === this.epoch && ticket.operation === this.operation; }
  sameScope(ticket: HomeworkSaveTicket) { return ticket.epoch === this.epoch; }
  captureScope() { const epoch = this.epoch; return () => epoch === this.epoch; }
  /** A rejected preflight must not discard user input or allocate a lasting new-row identity. */
  cancelBeforeSave(ticket: HomeworkSaveTicket, note: string) {
    if(!this.current(ticket))return;
    if(!ticket.operation.localSaved&&!ticket.operation.shareAttempted&&!ticket.operation.prepared.length)this.operation=null;
    this.busy=false;
    this.note=note;
  }
  finish(ticket: HomeworkSaveTicket, note: string, success: boolean) {
    if (!this.current(ticket)) return;
    this.busy = false;
    if (success) {
      this.operation = null;
      this.draft = emptyDraft(this.baseline);
    }
    this.note = note;
  }
}

export function validateHomeworkDraft(draft: HomeworkDraft) {
  if (!draft.subject.trim()) return "Укажите предмет";
  if (!draft.text.trim()) return "Напишите задание";
  if (draft.share && draft.deadlineMode === "custom" && draft.sharedDeadline && Number.isNaN(new Date(draft.sharedDeadline).getTime())) return "Укажите корректный срок общей домашки";
  if (draft.share && draft.audience.kind === "selected" && !draft.audience.roleIds.length && !draft.audience.userIds.length) return "Выберите получателей общей домашки";
  return "";
}
export function homeworkSaveError(error: unknown, localSaved: boolean) {
  const code = error instanceof Error ? error.message : "";
  const detail = code === "quota" ? "Превышен лимит трафика."
    : code === "big" ? "Файл слишком большой."
    : code === "full" ? "Можно приложить не больше шести файлов."
    : code === "bad" ? "Такой файл приложить нельзя."
    : code === "upload" || error instanceof TypeError ? "Вложения не отправились. Проверьте сеть и повторите сохранение."
    : code === "audience-unsupported" ? "Адресная домашка недоступна на этом сервере. Выберите всю группу или повторите позже."
    : code === "share-changed" ? "Предыдущая отправка ещё не подтверждена. Для безопасного повтора верните прежние текст, срок и получателей либо очистите черновик и создайте новую публикацию."
    : "Сохранить не получилось. Повторите попытку.";
  return (localSaved ? "На устройстве сохранено. " : "") + detail;
}
