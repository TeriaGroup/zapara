/** Русское склонение по числу: 1 задание, 2 задания, 5 заданий. */
export function plural(count: number, forms: [string, string, string]) {
  const tens = Math.abs(count) % 100, units = tens % 10;
  return `${count} ${tens > 10 && tens < 20 ? forms[2] : units === 1 ? forms[0] : units >= 2 && units <= 4 ? forms[1] : forms[2]}`;
}
export const taskCount = (count: number) => plural(count, ["задание", "задания", "заданий"]);
export const subjectCount = (count: number) => plural(count, ["предмет", "предмета", "предметов"]);

/** Сводка, из которой однозначно видно, что считается (#15, W-11): «2 предмета · 8 заданий · открыто 6 · выполнено 2». */
export function homeworkSummary(rows: { subject: string; done: boolean }[]) {
  const subjects = new Set(rows.map(row => row.subject.trim().toLocaleLowerCase("ru"))).size;
  const done = rows.filter(row => row.done).length;
  return `${subjectCount(subjects)} · ${taskCount(rows.length)} · открыто ${rows.length - done} · выполнено ${done}`;
}
