import type { HomeworkItem } from "./types";

export type PersonalHomeworkUndo = {
  id: string; owner: string; beforeDone: boolean; after: string; expiresAt: number;
};

function state(item: HomeworkItem) {
  return JSON.stringify([item.id, item.subject, item.text, item.done, item.created, item.files,
    item.deadlineAt, item.targetNthOccurrence, item.legacyCreatedLocalDate]);
}

export function personalHomeworkUndo(before: HomeworkItem, owner: string, now: number): PersonalHomeworkUndo {
  return { id: before.id, owner, beforeDone: before.done,
    after: state({ ...before, done: !before.done }), expiresAt: now + 5_000 };
}

export function canUndoPersonalHomework(
  undo: PersonalHomeworkUndo, current: HomeworkItem | undefined, owner: string, now: number,
) {
  return undo.owner === owner && now < undo.expiresAt && !!current && current.id === undo.id
    && state(current) === undo.after;
}
