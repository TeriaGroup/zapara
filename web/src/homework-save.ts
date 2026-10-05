import { saveEditorHomework, shareFailedNote } from "./groupHomework.ts";
import { HOMEWORK_FILE_LIMIT } from "./homework-files.ts";
import type { HomeworkAudience, HomeworkItem } from "./types";
import type { HomeworkSaveTicket, PendingHomeworkFile, PreparedHomeworkFile } from "./homework-draft";

type SaveDependencies = {
  signedIn: boolean; communityId: string;
  isCurrent: (ticket: HomeworkSaveTicket) => boolean;
  prepare: (item: PendingHomeworkFile) => Promise<{ name: string; mime: string; blob: Blob }>;
  put: (id: string, blob: Blob) => Promise<void>;
  remove: (id: string) => Promise<void>;
  readLocal: (id: string) => HomeworkItem | undefined;
  saveLocal: (row: HomeworkItem) => void;
  upload: (item: PreparedHomeworkFile) => Promise<void>;
  personalDue?: (ticket: HomeworkSaveTicket) => string | null;
  audienceSupported?: boolean;
  share: (subject: string, text: string, deadline: string | null, topicId: string | null, audience: HomeworkAudience | undefined, operationId: string) => Promise<void>;
};

const shareUnconfirmedNote = "На устройстве сохранено. Отправка группе не подтверждена. Проверьте общую домашку; повтор использует прежний номер публикации на обновлённом сервере.";

export function checkHomeworkUpload(response: { ok: boolean; status: number }) {
  if (response.status === 413) throw new Error("quota");
  if (!response.ok) throw new Error("upload");
}

/** Retry an unfinished operation with its existing local row and attachment checkpoints. */
export async function runHomeworkSave(ticket: HomeworkSaveTicket, dependencies: SaveDependencies) {
  const { draft, operation } = ticket;
  const shareDeadline = operation.shareAttempted ? operation.shareDeadline ?? null : draft.deadlineMode === "none" ? null : draft.deadlineMode === "custom" ? draft.sharedDeadline ? new Date(draft.sharedDeadline).toISOString() : null : dependencies.personalDue?.(ticket) ?? null;
  const audience = draft.audience.kind === "selected" ? draft.audience : undefined;
  const shareSignature = JSON.stringify([draft.subject.trim(), draft.text.trim(), draft.deadlineMode, draft.sharedDeadline, draft.nth, draft.topicId, audience]);
  const assertCurrent = () => { if (!dependencies.isCurrent(ticket)) throw new Error("scope"); };
  const created: string[] = [];
  let committedNewFiles = false;
  try {
    assertCurrent();
    if (draft.share && audience && !dependencies.audienceSupported) throw new Error("audience-unsupported");
    if (operation.shareAttempted && operation.shareSignature !== shareSignature) throw new Error("share-changed");
    if (draft.share && operation.shareAttempted && !dependencies.audienceSupported) return { outcome: { stored: true as const, sent: false, note: "На устройстве сохранено. Отправка группе не подтверждена. Проверьте общую домашку перед новой отправкой." }, success: true };
    if (draft.pending.length > HOMEWORK_FILE_LIMIT) throw new Error("full");
    const selected: PreparedHomeworkFile[] = [];
    for (const source of draft.pending) {
      assertCurrent();
      let item = operation.prepared.find(row => row.source.file === source.file && row.source.kind === source.kind);
      if (!item) {
        const prepared = await dependencies.prepare(source);
        assertCurrent();
        const id = crypto.randomUUID();
        // Register before the write, so even a failed write or changed identity can be cleaned.
        created.push(id);
        await dependencies.put(id, prepared.blob);
        assertCurrent();
        item = { source, blob: prepared.blob, uploaded: false,
          file: { id, kind: source.kind, name: prepared.name, mime: prepared.mime } };
        operation.prepared.push(item);
      }
      selected.push(item);
    }
    assertCurrent();
    const files = selected.map(item => item.file);
    const outcome = await saveEditorHomework(
      { subject: draft.subject, text: draft.text, share: draft.share, isNew: true },
      dependencies.signedIn, dependencies.communityId,
      async (subject, text) => {
        assertCurrent();
        const signature = JSON.stringify([subject, text, draft.nth, files]);
        if (signature !== operation.savedSignature) {
          // Read at the write boundary: another route may have completed this retained row during file awaits.
          const done = dependencies.readLocal(operation.id)?.done ?? false;
          dependencies.saveLocal({ id: operation.id, subject, text, done, created: operation.created, targetNthOccurrence: draft.nth, files });
          operation.localSaved = true;
          operation.savedSignature = signature;
        }
        committedNewFiles = true;
        if (dependencies.signedIn) for (const item of selected) {
          assertCurrent();
          if (item.uploaded) continue;
          await dependencies.upload(item);
          assertCurrent();
          item.uploaded = true;
        }
      },
      async (subject, text) => {
        assertCurrent();
        // A rejected response may follow a server commit. Never create another copy on local retry.
        operation.shareAttempted = true;
        operation.shareSignature = shareSignature;
        operation.shareDeadline = shareDeadline;
        await dependencies.share(subject, text, shareDeadline, draft.topicId || null, audience, operation.id);
      },
    );
    assertCurrent();
    const shareUnconfirmed = outcome.note === shareFailedNote;
    return {
      outcome: shareUnconfirmed ? { ...outcome, sent: false, note: shareUnconfirmedNote } : outcome,
      success: !shareUnconfirmed,
    };
  } catch (error) {
    // A committed local row owns its blobs even if uploads or sharing failed.
    if (!committedNewFiles) {
      await Promise.all(created.map(id => dependencies.remove(id).catch(() => undefined)));
      operation.prepared = operation.prepared.filter(item => !created.includes(item.file.id));
    }
    throw error;
  }
}
