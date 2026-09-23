import type { Lesson } from "./types.ts";
import { visibleLessons } from "./subgroups.ts";

export type CountItem = { name: string; count: number };

export type ScheduleSummary = {
  total: number;
  byDay: CountItem[];
  byType: CountItem[];
  bySubject: CountItem[];
  byTeacher: CountItem[];
  byRoom: CountItem[];
};

const days = ["", "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье"];

const types: Record<string, string> = {
  лек: "лекция",
  пр: "практика",
  практика: "практика",
  лаб: "лабораторная",
  конс: "консультация",
  зач: "зачёт",
  экз: "экзамен",
  курс: "курсовая",
};

/** 0 both, 1 the odd week the person sees, 2 the even week. Invert swaps the stored week. */
export function summaryCode(segment: number, invert: boolean): number {
  const user = segment === 0 ? 1 : segment === 1 ? 2 : 0;
  if (user === 0) return 0;
  return invert ? (user === 1 ? 2 : 1) : user;
}

/** Stored parity 0 is on both weeks. `code` 0 keeps every lesson. */
export function onWeek(parity: number, code: number): boolean {
  return code === 0 || parity === 0 || parity === code;
}

export function composeSummary(lessons: Lesson[], choices: Record<string, string>, segment: number, invert: boolean): ScheduleSummary {
  const code = summaryCode(segment, invert);
  const filtered = visibleLessons(lessons, choices).filter(lesson => onWeek(lesson.parity, code));
  const dayCount = filtered.some(lesson => lesson.dayOfWeek === 7) ? 7 : 6;
  return {
    total: filtered.length,
    byDay: Array.from({ length: dayCount }, (_, index) => ({
      name: days[index + 1],
      count: filtered.filter(lesson => lesson.dayOfWeek === index + 1).length,
    })),
    byType: counts(filtered.map(lesson => typeLabel(lesson.typeRaw))),
    bySubject: counts(filtered.map(lesson => stripType(lesson.subjectRaw, lesson.typeRaw) || "—")),
    byTeacher: counts(filtered.flatMap(lesson => teacherParts(lesson.teacherRaw))),
    byRoom: counts(filtered.map(roomLabel).filter(name => name.length > 0)),
  };
}

function typeLabel(value: string | null | undefined): string {
  const raw = (value || "").trim().toLocaleLowerCase("ru");
  if (!raw) return "";
  return types[raw] || (value || "").trim();
}

export function stripType(name: string | null | undefined, typeRaw: string | null | undefined): string {
  const subject = (name || "").trim();
  const type = (typeRaw || "").trim();
  if (type && subject.toLocaleLowerCase("ru").startsWith((type + " ").toLocaleLowerCase("ru"))) return subject.slice(type.length + 1).trim();
  return subject;
}

function teacherParts(raw: string | null | undefined): string[] {
  return (raw || "").split(";").map(part => part.trim()).filter(part => part.length > 0 && part !== "—");
}

export function roomLabel(lesson: Pick<Lesson, "classroomRaw" | "roomRaw" | "buildingRaw">): string {
  const classroom = (lesson.classroomRaw || "").trim();
  const room = (lesson.roomRaw || "").trim();
  if (/дистанционно/i.test(classroom) || /дистанционно/i.test(room)) return "дистанционно";
  const cleaned = classroom.replace(/;+\s*$/g, "").trim();
  if ((!room || room === "—") && (!cleaned || cleaned === "—")) return "";
  const building = (lesson.buildingRaw || "").trim()
    || (classroom.includes("*") ? "УЛК" : /вц/i.test(classroom) ? "ВЦ" : "ГК");
  const number = (room && room !== "—" ? room : cleaned).replaceAll("*", "").trim();
  return `${number} ${building}`.trim();
}

function counts(names: string[]): CountItem[] {
  const map = new Map<string, { name: string; count: number }>();
  for (const name of names) {
    if (!name || name === "—") continue;
    const key = name.toLocaleLowerCase("ru");
    const current = map.get(key);
    if (current) current.count += 1;
    else map.set(key, { name, count: 1 });
  }
  return [...map.values()].sort((a, b) => b.count - a.count || a.name.localeCompare(b.name, "ru", { sensitivity: "accent" }));
}
