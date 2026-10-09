import type { Lesson, MapPlan } from "./types.ts";
import { roomPlan } from "./ux-navigation.ts";

/** #28: расшифровка кодов корпусов — рядом с переключателем и в подсказках. */
export const buildingNames: Record<string, string> = {
  "ГК": "Главный корпус",
  "УЛК": "Учебно-лабораторный корпус",
  "ВЦ": "Вычислительный центр",
};

export function buildingName(code: string): string {
  return buildingNames[code.trim().toLocaleUpperCase("ru-RU")] || code;
}

/** «268» из «268 (Фесто);» — без хвостовых разделителей. */
export function lessonRoom(lesson: Pick<Lesson, "roomRaw" | "classroomRaw">): string {
  return (lesson.classroomRaw || lesson.roomRaw || "").replace(/[;\s]+$/, "").replace(/\*/g, "").trim();
}

function minutes(value: string): number {
  const [h, m] = value.split(":").map(Number);
  return (h || 0) * 60 + (m || 0);
}

export type NextLessonCaption = { label: "Сейчас" | "Следующая пара"; text: string; plan: MapPlan | null };

/** «Следующая пара: 18:30 · 268 (Фесто) · УЛК, 2 этаж» или «Сейчас: …», пока пара идёт. Без пары — null. */
export function nextLessonCaption(lesson: Lesson | null, plans: MapPlan[], now: Date): NextLessonCaption | null {
  if (!lesson) return null;
  const room = lessonRoom(lesson);
  const plan = room ? roomPlan(plans, lesson.roomRaw || lesson.classroomRaw || "", lesson.buildingRaw || "") : null;
  const at = now.getHours() * 60 + now.getMinutes();
  const running = at >= minutes(lesson.timeStart) && at < minutes(lesson.timeEnd);
  const parts = [running ? `до ${lesson.timeEnd}` : lesson.timeStart, room || "аудитория не указана"];
  if (plan) parts.push(`${plan.building}, ${plan.floor} этаж`);
  return { label: running ? "Сейчас" : "Следующая пара", text: parts.join(" · "), plan };
}

export type RoomSearch = { plan: MapPlan | null; message: string };

/** Поиск аудитории по номеру («268», «УЛК 320», «372*»): открывает план нужного корпуса и этажа. */
export function findRoom(plans: MapPlan[], query: string): RoomSearch {
  const value = query.trim();
  if (!value) return { plan: null, message: "" };
  if (!/\d{3,4}/.test(value)) return { plan: null, message: "Введите номер аудитории, например 268 или УЛК 320." };
  const plan = roomPlan(plans, value);
  return plan
    ? { plan, message: `Аудитория ${value.replace(/^(ГК|УЛК|ВЦ)\s*/i, "")}: ${buildingName(plan.building)}, ${plan.floor} этаж` }
    : { plan: null, message: `Плана для «${value}» нет. Проверьте номер — корпус и этаж можно выбрать вручную.` };
}
