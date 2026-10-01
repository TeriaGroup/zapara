import { sameSubject } from "./parity.ts";
import type { GroupHomeworkCopy, HomeworkItem } from "./types";

export type HomeworkBrowseStatus = "active" | "done" | "all";
export type HomeworkTarget = { kind: "local" | "shared"; id: string } | null;

function normalized(value: string) {
  return value.trim().replace(/\s+/g, " ").toLocaleLowerCase("ru-RU");
}

function matchesQuery(query: string, ...fields: string[]) {
  const words = normalized(query).split(" ").filter(Boolean);
  const haystack = normalized(fields.join(" "));
  return words.every(word => haystack.includes(word));
}

function matchesStatus(done: boolean, status: HomeworkBrowseStatus) {
  return status === "all" || done === (status === "done");
}

export function browseHomework(
  personal: HomeworkItem[], shared: GroupHomeworkCopy[],
  options: { subject: string | null; query: string; status: HomeworkBrowseStatus; target: HomeworkTarget; editingSharedId?: string },
) {
  const local = personal.filter(item => item.id === (options.target?.kind === "local" ? options.target.id : null)
    || (!options.subject || sameSubject(item.subject, options.subject))
      && matchesStatus(item.done, options.status)
      && matchesQuery(options.query, item.subject, item.text));
  const copies = shared.filter(item => item.homeworkId === options.editingSharedId
    || item.homeworkId === (options.target?.kind === "shared" ? options.target.id : null)
    || (!options.subject || sameSubject(item.title, options.subject))
      && matchesStatus(item.completed, options.status)
      && matchesQuery(options.query, item.title, item.body));
  return { local, copies, total: personal.length + shared.length, shown: local.length + copies.length };
}

export function homeworkEmptyKind(total: number, shown: number, targetMissing: boolean) {
  if (shown > 0) return null;
  if (targetMissing) return "missing";
  return total === 0 ? "empty" : "filtered";
}
