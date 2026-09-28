import type { SocialMessage } from "./types";

export const personalTextLimit = 2000;
export function personalText(text: string) { return text.replace(/\r\n/g, "\n").trim(); }
export function personalTextCount(text: string) { return Array.from(personalText(text)).length; }
export function personalTextValid(text: string) {
  const normalized = personalText(text);
  const length = Array.from(normalized).length;
  return length > 0 && length <= personalTextLimit && !/[\u0000-\u0008\u000b-\u001f\u007f-\u009f\uD800-\uDFFF]/u.test(normalized);
}
export function sendOnEnter(key: string, shift: boolean, composing: boolean, imeKey: boolean, desktop: boolean) {
  return desktop && key === "Enter" && !shift && !composing && !imeKey;
}
export function emptyChatState(loading: boolean, error: string, count: number) {
  return count > 0 ? null : loading ? "loading" : error ? "failed" : "empty";
}

type NormalDraft = { text: string; reply: SocialMessage | null };
export type PersonalComposer = NormalDraft & {
  editing: SocialMessage | null; saved: NormalDraft | null; revision: number; busy: boolean; error: string;
};
type SendTicket = { conversationId: string; epoch: number; revision: number; kind: "text" | "attachment"; state: PersonalComposer };

/** Memory belongs to the current account session, above route consumers. */
export class PersonalComposerStore {
  private owner: string;
  private epoch = 0;
  private drafts = new Map<string, PersonalComposer>();
  private pending = new Map<string, SendTicket>();
  constructor(owner: string) { this.owner = owner; }
  scope(owner: string) {
    if (this.owner === owner) return;
    this.owner = owner; this.epoch += 1; this.drafts.clear(); this.pending.clear();
  }
  read(conversationId: string): PersonalComposer {
    let state = this.drafts.get(conversationId);
    if (!state) {
      state = { text: "", reply: null, editing: null, saved: null, revision: 0, busy: false, error: "" };
      this.drafts.set(conversationId, state);
    }
    return state;
  }
  private update(id: string, patch: Partial<PersonalComposer>) {
    const state = this.read(id);
    this.drafts.set(id, { ...state, ...patch, revision: state.revision + 1 });
  }
  text(id: string, text: string) { this.update(id, { text }); }
  reply(id: string, reply: SocialMessage) {
    const state = this.read(id);
    this.update(id, { ...(state.saved ?? { text: state.text }), reply, editing: null, saved: null });
  }
  edit(id: string, editing: SocialMessage) {
    const state = this.read(id);
    this.update(id, { text: editing.body ?? "", reply: null, editing,
      saved: state.saved ?? { text: state.text, reply: state.reply } });
  }
  cancel(id: string) {
    const state = this.read(id);
    this.update(id, state.editing
      ? { ...(state.saved ?? { text: "", reply: null }), editing: null, saved: null }
      : { reply: null });
  }
  begin(id: string, kind: "text" | "attachment" = "text"): SendTicket | null {
    const state = this.read(id);
    if (state.busy) return null;
    const ticket = { conversationId: id, epoch: this.epoch, revision: state.revision, kind, state };
    this.pending.set(id, ticket);
    this.drafts.set(id, { ...state, busy: true, error: "" });
    return ticket;
  }
  finish(ticket: SendTicket, error: string, success: boolean) {
    const id = ticket.conversationId;
    if (ticket.epoch !== this.epoch || this.pending.get(id) !== ticket) return;
    this.pending.delete(id);
    const state = this.read(id);
    let next = { ...state, busy: false, error };
    if (success && state.revision === ticket.revision) {
      next = ticket.kind === "attachment" ? { ...next, reply: null }
        : state.editing ? { ...next, ...(state.saved ?? { text: "", reply: null }), editing: null, saved: null }
        : { ...next, text: "", reply: null };
      next.revision += 1;
    }
    this.drafts.set(id, next);
  }
}
