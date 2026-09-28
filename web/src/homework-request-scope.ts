import { sameSubject } from "./parity.ts";
import type { GroupHomeworkCopy } from "./types";

type ScopeTicket = { scope: string; epoch: number };
export type ActionTicket = ScopeTicket & { operation: number };

/** Invalidates a request as soon as the account, group, or community changes. */
export class HomeworkRequestScope {
  private key = "";
  private epoch = 0;
  private operation = 0;
  scope(key: string): boolean {
    if (key === this.key) return false;
    this.key = key;
    this.epoch++;
    this.operation++;
    return true;
  }
  capture(): ScopeTicket { return { scope: this.key, epoch: this.epoch }; }
  begin(): ActionTicket { return { ...this.capture(), operation: ++this.operation }; }
  owns(ticket: ScopeTicket): boolean { return ticket.scope === this.key && ticket.epoch === this.epoch; }
  active(ticket: ActionTicket): boolean { return this.owns(ticket) && ticket.operation === this.operation; }
}

export function scopedValue<T>(value: T, loadedFor: string, current: string, empty: T): T {
  return loadedFor === current ? value : empty;
}

export async function loadScopedHomework(
  guard: HomeworkRequestScope,
  ticket: ScopeTicket,
  load: () => Promise<GroupHomeworkCopy[]>,
  publish: (rows: GroupHomeworkCopy[]) => void,
  fail?: () => void,
) {
  if (!guard.owns(ticket)) return;
  try {
    const rows = await load();
    if (guard.owns(ticket)) publish(rows);
  } catch {
    if (guard.owns(ticket)) fail?.();
  }
}

export function subjectHomework(rows: GroupHomeworkCopy[], subject: string | null | undefined): GroupHomeworkCopy[] {
  return rows.filter(item => !!subject && sameSubject(item.title, subject));
}
