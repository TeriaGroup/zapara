import type { Lesson, MapPlan, PublicMapAsset } from "./types.ts";
import { parseCampusGraph, type CampusGraph } from "./campus-routing.ts";
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

/**
 * #28, решение по умолчанию: автозум к аудитории следующей пары выключен.
 * Включение грузит граф кампуса (тот же файл, что у «Маршрута») и ставит метку на аудиторию —
 * MapViewer сам приближает план к метке. Выключено, пока не решено, нужен ли лишний запрос графа при каждом открытии карт.
 */
export const autoZoomNextRoom = false;

/** Граф кампуса с теми же проверками, что в «Маршруте»: свой origin, размер и SHA-256. */
export async function loadCampusGraph(asset: PublicMapAsset, signal?: AbortSignal, fetcher: typeof fetch = fetch, origin = globalThis.location?.origin || "http://localhost"): Promise<CampusGraph> {
  const url = new URL(asset.url, origin);
  if (url.origin !== origin || !url.pathname.startsWith("/api/v1/maps/assets/")) throw Error("origin");
  const response = await fetcher(url, { credentials: "same-origin", signal });
  if (!response.ok) throw Error("graph");
  const data = await response.arrayBuffer();
  if (data.byteLength !== asset.bytes) throw Error("size");
  if (globalThis.crypto?.subtle && asset.sha256) {
    const digest = await crypto.subtle.digest("SHA-256", data);
    const actual = [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, "0")).join("");
    if (actual.toLowerCase() !== asset.sha256.toLowerCase()) throw Error("hash");
  }
  return parseCampusGraph(JSON.parse(new TextDecoder().decode(data)));
}
