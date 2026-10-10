import { parityOf } from "./parity.ts";

const shortMonths = ["янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];
const longMonths = ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];
const shortDays = ["Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб"];

/** «5–11 окт», через границу месяца «28 сен – 4 окт» (#14; короткое тире без пробелов внутри месяца). */
export function weekRange(monday: Date, sunday: Date, long = false): string {
  const months = long ? longMonths : shortMonths;
  return monday.getMonth() === sunday.getMonth()
    ? `${monday.getDate()}–${sunday.getDate()} ${months[sunday.getMonth()]}`
    : `${monday.getDate()} ${months[monday.getMonth()]} – ${sunday.getDate()} ${months[sunday.getMonth()]}`;
}

export function weekParity(date: Date, period: { start: string; weekCount: number } | null | undefined, invert: boolean): string {
  if (!period) return "";
  const parity = parityOf(date, period.start, period.weekCount, invert);
  return parity === 1 ? "нечётная" : parity === 2 ? "чётная" : `${parity}-я неделя`;
}

/** Заголовок дня «Пт, 9 окт» — без «Вчера/Сегодня/Завтра»; «Сегодня» — отдельным бейджем. */
export function dayHeading(date: Date): string {
  return `${shortDays[date.getDay()]}, ${date.getDate()} ${shortMonths[date.getMonth()]}`;
}

export function formatRuDate(date: Date): string {
  return `${String(date.getDate()).padStart(2, "0")}.${String(date.getMonth() + 1).padStart(2, "0")}.${date.getFullYear()}`;
}

/** «09.10.2026» (также «9.10.2026») → дата; неполный или несуществующий день → null. */
export function parseRuDate(value: string): Date | null {
  const match = /^\s*(\d{1,2})\.(\d{1,2})\.(\d{4})\s*$/.exec(value);
  if (!match) return null;
  const [day, month, year] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(year, month - 1, day);
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day ? date : null;
}
