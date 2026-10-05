import { sameSubject } from "./parity.ts";
import type { GroupHomeworkCopy, HomeworkItem } from "./types";
import { dueBucket, type DueBucket } from "./ux300.ts";
import { syncSubjectKey } from "./utc.ts";

export type HomeworkBrowseStatus = "active" | "done" | "all";
export type HomeworkTarget = { kind: "local" | "shared"; id: string } | null;
/** View-only precision; inferred lesson dates are not exact server deadlines. */
export type PersonalBrowseItem = HomeworkItem & { deadlinePrecision?: "date" | "instant" };

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
  personal: PersonalBrowseItem[], shared: GroupHomeworkCopy[],
  options: { subject: string | null; exactSubjectKey?: string|null; query: string; status: HomeworkBrowseStatus; target: HomeworkTarget; editingSharedId?: string; source?: "all"|"personal"|"shared"; deadline?: "all"|DueBucket; onlyFiles?: boolean; order?: "original"|"subject"|"deadline"; now?: Date },
) {
  const due = (value?:string|null,precision: "date" | "instant"="instant") => !options.deadline || options.deadline==="all" || dueBucket(value,options.now,precision)===options.deadline;
  const subject=(value:string)=>options.exactSubjectKey!=null?syncSubjectKey(value)===options.exactSubjectKey:!options.subject||sameSubject(value,options.subject);
  const local = personal.filter(item => item.id === (options.target?.kind === "local" ? options.target.id : null)
    || subject(item.subject)
      && matchesStatus(item.done, options.status)
      && options.source!=="shared" && due(item.deadlineAt,item.deadlinePrecision) && (!options.onlyFiles || !!item.files?.length)
      && matchesQuery(options.query, item.subject, item.text,...(item.files||[]).map(file=>file.name)));
  const copies = shared.filter(item => item.homeworkId === options.editingSharedId
    || item.homeworkId === (options.target?.kind === "shared" ? options.target.id : null)
    || subject(item.title)
      && matchesStatus(item.completed, options.status)
      && options.source!=="personal" && !options.onlyFiles && due(item.deadlineAt)
      && matchesQuery(options.query, item.title, item.body));
  const byDue=(a?:string|null,b?:string|null)=>(a&&Number.isFinite(Date.parse(a))?Date.parse(a):Number.MAX_SAFE_INTEGER)-(b&&Number.isFinite(Date.parse(b))?Date.parse(b):Number.MAX_SAFE_INTEGER);
  if(options.order==="subject"){local.sort((a,b)=>a.subject.localeCompare(b.subject,"ru"));copies.sort((a,b)=>a.title.localeCompare(b.title,"ru"));}
  else if(options.order==="deadline"){local.sort((a,b)=>byDue(a.deadlineAt,b.deadlineAt));copies.sort((a,b)=>byDue(a.deadlineAt,b.deadlineAt));}
  return { local, copies, total: personal.length + shared.length, shown: local.length + copies.length };
}

export function homeworkEmptyKind(total: number, shown: number, targetMissing: boolean) {
  if (shown > 0) return null;
  if (targetMissing) return "missing";
  return total === 0 ? "empty" : "filtered";
}
