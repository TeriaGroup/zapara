import type { HomeworkItem, Lesson } from "./types";
import { isoDay } from "./parity";

export type CardItem = { time: string; lesson: string; subject: string; place: string; teacher: string };
export type Card =
  | { v: 1; type: "schedule"; date: string; group: string; title: string; items: CardItem[] }
  | { v: 1; type: "lesson"; date: string; time: string; lesson: string; subject: string; place: string; teacher: string; group: string }
  | { v: 1; type: "homework"; subject: string; text: string; done: boolean }
  | { v: 1; type: "tasks"; items: { subject: string; text: string }[] }
  | { v: 1; type: "place"; building: string; floor: string; room: string; subject: string };

function clip(value: string | null | undefined, max: number) {
  const text = (value || "").replace(/[\u0000-\u001f]/g, " ").replace(/\s+/g, " ").trim();
  return text.length > max ? text.slice(0, max) : text;
}

function pack(value: Card) {
  const json = JSON.stringify(value);
  return json.length <= 2000 ? json : null;
}

export function scheduleCard(group: string, date: Date, title: string, lessons: Lesson[]) {
  const items = lessons.slice(0, 8).map(lesson => ({
    time: clip(`${lesson.timeStart}–${lesson.timeEnd}`, 24),
    lesson: clip(lesson.typeRaw, 16),
    subject: clip(lesson.subjectRaw, 80),
    place: clip(lesson.roomRaw || lesson.classroomRaw, 40),
    teacher: clip(lesson.teacherRaw, 60)
  })).filter(item => item.subject && item.time);
  if (items.length === 0) return null;
  return pack({ v: 1, type: "schedule", date: isoDay(date), group: clip(group, 32), title: clip(title, 80), items });
}

export function lessonCard(group: string, date: string, lesson: { time: string; type?: string | null; subject: string; place?: string | null; teacher?: string | null }) {
  const subject = clip(lesson.subject, 80);
  const time = clip(lesson.time, 24);
  if (!subject || !time) return null;
  return pack({
    v: 1, type: "lesson", date: clip(date, 10), time, lesson: clip(lesson.type, 16),
    subject, place: clip(lesson.place, 40), teacher: clip(lesson.teacher, 60), group: clip(group, 32)
  });
}

export function lessonFrom(group: string, date: Date, lesson: Lesson) {
  return lessonCard(group, isoDay(date), {
    time: `${lesson.timeStart}–${lesson.timeEnd}`,
    type: lesson.typeRaw,
    subject: lesson.subjectRaw,
    place: lesson.roomRaw || lesson.classroomRaw,
    teacher: lesson.teacherRaw
  });
}

export function homeworkCard(item: Pick<HomeworkItem, "subject" | "text" | "done">) {
  const subject = clip(item.subject, 80);
  const text = clip(item.text, 500);
  if (!subject || !text) return null;
  return pack({ v: 1, type: "homework", subject, text, done: item.done });
}

export function tasksCard(items: Pick<HomeworkItem, "subject" | "text">[]) {
  const rows = items.slice(0, 6).map(item => ({ subject: clip(item.subject, 80), text: clip(item.text, 240) })).filter(item => item.subject && item.text);
  if (rows.length === 0) return null;
  return pack({ v: 1, type: "tasks", items: rows });
}

export function placeCard(building: string, floor: string, room: string, subject: string) {
  const card = { v: 1 as const, type: "place" as const, building: clip(building, 16), floor: clip(floor, 8), room: clip(room, 40), subject: clip(subject, 80) };
  if (!card.building && !card.room) return null;
  return pack(card);
}

export function placeFromLesson(lesson: Lesson) {
  return placeCard(lesson.buildingRaw || "", "", lesson.roomRaw || lesson.classroomRaw || "", lesson.subjectRaw);
}

export function cardLabel(body: string | null) {
  const card = readCard(body);
  if (!card) return "Карточка";
  if (card.type === "schedule") return `Расписание · ${card.title}`;
  if (card.type === "lesson") return `Пара · ${card.subject}`;
  if (card.type === "homework") return `Домашка · ${card.subject}`;
  if (card.type === "tasks") return "Домашка";
  return `Аудитория · ${card.room || card.building}`;
}

export function readCard(body: string | null): Card | null {
  if (!body) return null;
  try {
    const value = JSON.parse(body) as Card;
    if (!value || value.v !== 1 || typeof value.type !== "string") return null;
    return value;
  } catch { return null; }
}
