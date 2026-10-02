import type { FormAnswer, FormQuestion, FormResponse, MapPlan } from "./types.ts";
import { matchesWords } from "./next-workflows.ts";

export const noteSearch = matchesWords;
export function dayLoad(rows: { timeStart: string; timeEnd: string }[]) {
  const minute = (value: string) => { const match = /^(\d{1,2}):(\d{2})$/.exec(value); return match && +match[1] < 24 && +match[2] < 60 ? +match[1] * 60 + +match[2] : NaN; };
  const ranges = rows.map(row => ({start:minute(row.timeStart),end:minute(row.timeEnd)})).filter(row => Number.isFinite(row.start) && Number.isFinite(row.end) && row.end > row.start).sort((a,b)=>a.start-b.start);
  const merged: {start:number;end:number}[]=[];
  for(const range of ranges) { const previous=merged.at(-1); if(previous && previous.end>=range.start) previous.end=Math.max(previous.end,range.end); else merged.push({...range}); }
  const minutes=merged.reduce((total,row)=>total+row.end-row.start,0);
  const span=merged.length ? merged.at(-1)!.end-merged[0].start : 0;
  return {minutes,span,gaps:span-minutes};
}
export function choiceCounts(questions: FormQuestion[], responses: FormResponse[]) {
  return questions.filter(question=>question.kind.endsWith("Choice")).map(question=>({title:question.title,options:question.options.map(label=>({label,count:responses.filter(response=>response.answers.find(answer=>answer.questionId===question.questionId)?.choices.includes(label)).length})),loaded:responses.length}));
}
export function answerDraftChanged(value: FormAnswer[], saved: FormAnswer[]) {
  const canonical = (rows: FormAnswer[]) => JSON.stringify(rows
    .filter(row => (row.text ?? "") !== "" || row.choices.length > 0)
    .map(row => [row.questionId, row.text ?? "", [...row.choices].sort()])
    .sort((a, b) => String(a[0]).localeCompare(String(b[0]))));
  return canonical(value) !== canonical(saved);
}
export function copyFormQuestion(rows: FormQuestion[], id: string, nextId: string, limit = 20): FormQuestion[] {
  const at = rows.findIndex(row => row.questionId === id);
  if (at < 0 || rows.length >= limit || rows.some(row => row.questionId === nextId)) return rows;
  return [...rows.slice(0, at + 1), { ...rows[at], questionId: nextId, options: [...rows[at].options] }, ...rows.slice(at + 1)];
}
export function ballotDraftProblem(question: string, options: string[]) {
  if (!question.trim()) return "Введите вопрос голосования.";
  if (options.length < 2 || options.some(value => !value.trim())) return "Заполните каждый вариант ответа.";
  const values = options.map(value => value.trim().toLocaleLowerCase("ru-RU"));
  if (new Set(values).size !== values.length) return "Варианты повторяются. Измените повторяющийся текст.";
  return "";
}
export function chooseBuildingPlan(plans: MapPlan[], building: string, floor: number | undefined) {
  return plans.filter(row => row.building === building)
    .sort((a, b) => Math.abs(a.floor - (floor ?? 1)) - Math.abs(b.floor - (floor ?? 1)) || a.floor - b.floor)[0] ?? null;
}
export function safeAppReturn(value: unknown, fallback = "/schedule") {
  return typeof value === "string" && !/[\\\r\n]/.test(value) && /^\/(?:schedule|week|summary|teachers|maps|friends|homework|chat|community|group|settings)(?:[/?#]|$)/.test(value) ? value : fallback;
}
export type DueBucket = "overdue" | "today" | "soon" | "later" | "none";
export function dueBucket(value: string | null | undefined, now = new Date(), precision: "instant" | "date" = "instant"): DueBucket {
  const time = value ? Date.parse(value) : NaN;
  if (!Number.isFinite(time)) return "none";
  const boundary=precision==="date" ? new Date(now.getFullYear(),now.getMonth(),now.getDate()).getTime() : now.getTime();
  if (time < boundary) return "overdue";
  const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1);
  if (time < end.getTime()) return "today";
  end.setDate(end.getDate() + 3);
  return time < end.getTime() ? "soon" : "later";
}
export type FormBrowse = "all" | "needed" | "answered" | "closed";
export function browseForms<T extends { title: string; description: string; canRespond: boolean; ownResponse: unknown }>(rows: T[], query: string, filter: FormBrowse) {
  return rows.filter(row => noteSearch(query, row.title, row.description) && (filter === "all" || filter === "needed" && row.canRespond && !row.ownResponse || filter === "answered" && !!row.ownResponse || filter === "closed" && !row.canRespond));
}
export function browseDevices<T extends { deviceName: string; platform: string; isCurrent: boolean; lastSeenAt: string }>(rows: T[], query: string, others: boolean) {
  return rows.filter(row => (!others || !row.isCurrent) && noteSearch(query, row.deviceName, row.platform))
    .sort((a, b) => Number(b.isCurrent) - Number(a.isCurrent) || b.lastSeenAt.localeCompare(a.lastSeenAt));
}
export function summaryRows<T extends { name: string; count: number }>(rows: T[], query: string, sort: "original" | "name" | "count") {
  const visible = rows.filter(row => noteSearch(query, row.name));
  return sort === "original" ? visible : visible.sort((a, b) => sort === "name" ? a.name.localeCompare(b.name, "ru") : b.count - a.count);
}

export function errorHint(error: unknown, fallback: string) {
  const status = error instanceof Error ? error.message : "";
  if (status === "401") return "Проверьте логин и пароль или восстановите доступ к аккаунту.";
  if (status === "429") return "Слишком много попыток. Подождите и повторите вход позже.";
  if (status === "403") return "Действие недоступно для этого аккаунта. Обновите сведения об аккаунте.";
  return fallback;
}
