export type HomeworkShareInput = { share: boolean; signedIn: boolean; communityId: string };
export type HomeworkEditorShare = { subject: string; text: string; share: boolean; isNew: boolean };
export type HomeworkShareOutcome = { stored: true; sent: boolean; note: string };
export type LocalHomeworkMark = { id: string; done: boolean };
export type GroupCopyMark = { homeworkId: string; memberId: string; completed: boolean };
export type GroupHomeworkBook = { local: LocalHomeworkMark[]; copies: GroupCopyMark[] };

export const shareSignInNote = "Войдите в аккаунт, чтобы отправить домашку группе.";
export const shareLocalOnlyNote = "Вы ещё не в группе. Домашка сохранена только на этом устройстве.";
export const shareFailedNote = "На устройстве сохранено. Группе отправить не получилось.";
export const shareSharedNote = "Домашка продублирована всей группе.";

/** Store the editor's subject and text first. The group call gets that same pair and starts only after the local row. */
export async function saveEditorHomework(
  editor: HomeworkEditorShare,
  signedIn: boolean,
  communityId: string,
  saveLocal: (subject: string, text: string) => Promise<void> | void,
  send: (subject: string, text: string) => Promise<void>,
): Promise<HomeworkShareOutcome> {
  const subject = editor.subject.trim();
  const text = editor.text.trim();
  return saveGroupHomework(
    { share: editor.share && editor.isNew, signedIn, communityId, subject, text },
    saveLocal,
    send,
  );
}

/** Store the device task first. Send a group copy only when the box, the session, and the community all say yes. */
export async function saveGroupHomework(
  input: HomeworkShareInput & { subject: string; text: string },
  saveLocal: (subject: string, text: string) => Promise<void> | void,
  send: (subject: string, text: string) => Promise<void>,
): Promise<HomeworkShareOutcome> {
  await saveLocal(input.subject, input.text);
  if (!input.share) return { stored: true, sent: false, note: "" };
  if (!input.signedIn) return { stored: true, sent: false, note: shareSignInNote };
  if (!input.communityId.trim()) return { stored: true, sent: false, note: shareLocalOnlyNote };
  try {
    await send(input.subject, input.text);
    return { stored: true, sent: true, note: shareSharedNote };
  } catch {
    return { stored: true, sent: false, note: shareFailedNote };
  }
}

/** «Сделано» on a group copy changes that member's flag only. The local task and every other member stay put. */
export function completeGroupCopy(
  local: LocalHomeworkMark[],
  copies: GroupCopyMark[],
  actorId: string,
  homeworkId: string,
  completed: boolean,
): GroupHomeworkBook {
  const actor = actorId.trim();
  const copyId = homeworkId.trim();
  return {
    local: local.map(row => ({ ...row })),
    copies: copies.map(row => row.homeworkId === copyId && row.memberId === actor ? { ...row, completed } : { ...row }),
  };
}
