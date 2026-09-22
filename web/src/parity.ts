import type { Lesson } from "./types";

export function parseDay(iso: string): Date {
  const [y, m, d] = iso.slice(0, 10).split("-").map(Number);
  return new Date(y, (m || 1) - 1, d || 1);
}

export function isoDay(date: Date): string {
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${m}-${d}`;
}

export function addDays(date: Date, days: number): Date {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

export function weekday(date: Date): number {
  const day = date.getDay();
  return day === 0 ? 7 : day;
}

export function weekCode(date: Date, periodStart: string, weekCount: number): number {
  const start = parseDay(periodStart);
  const dow = weekday(start);
  const monday = addDays(start, -(dow - 1));
  const days = Math.floor((parseDay(isoDay(date)).getTime() - monday.getTime()) / 86_400_000);
  if (days < 0) return 1;
  let code = (Math.floor(days / 7) + 1) % weekCount;
  if (code === 0) code = weekCount;
  return code;
}

export function parityOf(date: Date, periodStart: string, weekCount: number, invert: boolean): number {
  const code = weekCode(date, periodStart, weekCount);
  const odd = weekCount === 2 ? (code === 1 ? 1 : 2) : code;
  return invert && (odd === 1 || odd === 2) ? (odd === 1 ? 2 : 1) : odd;
}

export function lessonsOn(lessons: Lesson[], date: Date, periodStart: string, weekCount: number, invert: boolean): Lesson[] {
  const day = weekday(date);
  if (day === 7) return [];
  const parity = parityOf(date, periodStart, weekCount, invert);
  return lessons
    .filter(lesson => lesson.dayOfWeek === day && (lesson.parity === 0 || lesson.parity === parity))
    .sort((a, b) => a.timeStart.localeCompare(b.timeStart) || a.index - b.index);
}

export function smartDate(now = new Date()): Date {
  const day = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  return now.getHours() >= 18 ? addDays(day, 1) : day;
}

const days = ["", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота", "воскресенье"];
const months = ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

export function dayTitle(date: Date, today = new Date()): string {
  const a = isoDay(date);
  const t = isoDay(today);
  if (a === t) return "Сегодня";
  if (a === isoDay(addDays(parseDay(t), 1))) return "Завтра";
  if (a === isoDay(addDays(parseDay(t), -1))) return "Вчера";
  const name = days[weekday(date)];
  return name.charAt(0).toUpperCase() + name.slice(1);
}

export function longDate(date: Date, periodStart: string, weekCount: number, invert: boolean): string {
  const parity = parityOf(date, periodStart, weekCount, invert);
  const week = weekCode(date, periodStart, weekCount);
  return `${date.getDate()} ${months[date.getMonth()]}, ${days[weekday(date)]} · ${parity === 1 ? "нечётная" : "чётная"} неделя · ${week}-я`;
}

export function subjectKey(raw: string): string {
  return raw.toLocaleLowerCase("ru").replaceAll("ё", "е").replace(/[^a-zа-я0-9]/gi, "");
}

export function sameSubject(a: string, b: string): boolean {
  const x = subjectKey(a);
  const y = subjectKey(b);
  if (!x || !y) return false;
  if (x === y) return true;
  const n = Math.min(x.length, y.length);
  return n >= 8 && (x.startsWith(y) || y.startsWith(x));
}

export function score(roomA: string | null, buildingA: string | null, roomB: string | null, buildingB: string | null): number {
  const canon = (value: string | null) => {
    const text = value?.trim();
    if (!text) return null;
    if (/^(вц|гк|main)$/i.test(text)) return "ГК";
    if (/^улк$/i.test(text)) return "УЛК";
    return text;
  };
  const left = canon(buildingA);
  const right = canon(buildingB);
  const sameBuilding = !!left && !!right && left.toLowerCase() === right.toLowerCase();
  const conflict = !!left && !!right && !sameBuilding;
  const sameRoom = !!roomA?.trim() && !!roomB?.trim() && roomA.trim().toLowerCase() === roomB.trim().toLowerCase() && !conflict;
  if (sameRoom) return 100;
  if (!sameBuilding) return 25;
  const floor = (room: string | null) => {
    const digits = room?.match(/\d+/)?.[0];
    const n = digits ? Number(digits[0]) : 0;
    return n >= 1 && n <= 9 ? n : 0;
  };
  const a = floor(roomA);
  const b = floor(roomB);
  return a && a === b ? 75 : 50;
}
