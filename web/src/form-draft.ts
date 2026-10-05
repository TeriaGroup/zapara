import type { FormQuestion } from "./types";
export function moveQuestion(rows: FormQuestion[], id: string, direction: -1 | 1) {
  const index = rows.findIndex(row => row.questionId === id), target = index + direction;
  if (index < 0 || target < 0 || target >= rows.length) return rows;
  const next = [...rows]; [next[index], next[target]] = [next[target], next[index]]; return next;
}
export function changeQuestionKind(row: FormQuestion, kind: FormQuestion["kind"]): FormQuestion {
  return { ...row, kind, options: ["singleChoice", "multipleChoice"].includes(kind) && !row.options.length ? ["", ""] : row.options };
}
export function questionWire(row: FormQuestion): FormQuestion {
  return { ...row, title: row.title.trim(), options: ["singleChoice", "multipleChoice"].includes(row.kind) ? row.options.map(option => option.trim()) : [] };
}
export function questionFilled(row: FormQuestion) { return !!row.title.trim() || row.options.some(option => !!option.trim()); }
