// Тип пары: ключ цвета и подпись (DESIGN.md §2). Одни слова и цвета для web и desktop (#11).
export type LessonKind = "lecture" | "practice" | "lab" | "consult" | "credit" | "exam" | "course";

export const lessonKindLabels: Record<LessonKind, string> = {
  lecture: "Лекция", practice: "Практика", lab: "Лаба", consult: "Консульт.",
  credit: "Зачёт", exam: "Экзамен", course: "Курсовая",
};

/** Строка типа из расписания → ключ. Незнакомая строка остаётся без цвета (пустой ключ). */
export function lessonKind(type: string): LessonKind | "" {
  const value = type.trim().toLowerCase();
  if (value === "лек" || value === "лекция") return "lecture";
  if (value === "пр" || value === "практика") return "practice";
  if (value === "лаб" || value === "лабораторная" || value === "лабораторная работа") return "lab";
  if (value === "конс" || value === "консультация") return "consult";
  if (value === "зач" || value === "зачёт" || value === "зачет") return "credit";
  if (value === "экз" || value === "экзамен") return "exam";
  if (value === "курс" || value === "курсовая") return "course";
  return "";
}
