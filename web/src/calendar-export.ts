/** A static iCalendar snapshot of the exact occurrences selected by the client. */
export type CalendarEntry = { id: string; start: string; end: string; summary: string; location?: string; description?: string };
export type CalendarExportResult = { content: string; eventCount: number; skippedCount: number };
export type AllDayCalendarEntry = { id: string; day: string; summary: string; description?: string };

const encoder = new TextEncoder();
/** Uses raw timetable values so display aliases do not change occurrence identity. */
export function calendarOccurrenceId(groupId: string, subjectRaw: string, teacherRaw = "", classroomRaw = ""): string {
  return [groupId, subjectRaw, teacherRaw, classroomRaw].map(value => `${value.length}:${value}`).join("");
}
function validDay(day: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);
  if (!match || +match[1] < 1) return null;
  const date = new Date(0); date.setUTCFullYear(+match[1], +match[2] - 1, +match[3]); date.setUTCHours(0, 0, 0, 0);
  return date.getUTCFullYear() === +match[1] && date.getUTCMonth() === +match[2] - 1 && date.getUTCDate() === +match[3] ? date : null;
}
function instant(value: string): Date | null {
  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(Z|[+-](\d{2}):(\d{2}))$/.exec(value);
  if (!match || !validDay(match[1]) || +match[2] > 23 || +match[3] > 59 || +match[4] > 59
    || match[5] !== "Z" && (+match[6] > 14 || +match[7] > 59 || +match[6] === 14 && +match[7] > 0)) return null;
  const date = new Date(value);
  return Number.isFinite(date.getTime()) && date.getUTCFullYear() >= 1 && date.getUTCFullYear() <= 9999 ? date : null;
}
const utc = (date: Date) => date.toISOString().replace(/[-:]/g, "").replace(/\.\d{3}Z$/, "Z");
function escaped(value: string): string {
  return value.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/g, "")
    .replaceAll("\\", "\\\\").replace(/\r\n|\r|\n/g, "\\n").replaceAll(";", "\\;").replaceAll(",", "\\,");
}
function folded(value: string): string {
  const lines: string[] = []; let line = "", bytes = 0;
  for (const rune of value) {
    const length = encoder.encode(rune).length;
    if (bytes + length > 75) { lines.push(line); line = " "; bytes = 1; }
    line += rune; bytes += length;
  }
  lines.push(line); return lines.join("\r\n");
}

/** The timetable belongs to Moscow time, independent of the browser's local timezone. */
export function moscowLessonInstant(day: string, time: string): string | null {
  const date = validDay(day), match = /^(\d{1,2}):(\d{2})$/.exec(time);
  if (!date || !match || +match[1] > 23 || +match[2] > 59) return null;
  date.setUTCMinutes(+match[1] * 60 + +match[2] - 180);
  return date.getUTCFullYear() >= 1 ? date.toISOString() : null;
}

export async function createCalendarExport(entries: CalendarEntry[], name: string, generatedAt = new Date()): Promise<CalendarExportResult> {
  if (!Number.isFinite(generatedAt.getTime()) || generatedAt.getUTCFullYear() < 1 || generatedAt.getUTCFullYear() > 9999)
    throw new RangeError("Некорректное время создания календаря.");
  const lines = ["BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Расписание военмех//Расписание//RU", "CALSCALE:GREGORIAN", `X-WR-CALNAME:${escaped(name)}`];
  const seen = new Set<string>(); let skippedCount = 0, eventCount = 0;
  for (const entry of entries) {
    const start = instant(entry.start), end = instant(entry.end);
    if (!entry.id.trim() || !entry.summary.trim() || !start || !end || end.getTime() <= start.getTime()) { skippedCount++; continue; }
    const digest = await crypto.subtle.digest("SHA-256", encoder.encode(`${entry.id}\n${utc(start)}`));
    const uid = [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, "0")).join("");
    if (seen.has(uid)) { skippedCount++; continue; } seen.add(uid); eventCount++;
    lines.push("BEGIN:VEVENT", `UID:${uid}`, `DTSTAMP:${utc(generatedAt)}`, `DTSTART:${utc(start)}`, `DTEND:${utc(end)}`, `SUMMARY:${escaped(entry.summary)}`);
    if (entry.location?.trim()) lines.push(`LOCATION:${escaped(entry.location)}`);
    if (entry.description?.trim()) lines.push(`DESCRIPTION:${escaped(entry.description)}`);
    lines.push("END:VEVENT");
  }
  lines.push("END:VCALENDAR"); return { content: lines.map(folded).join("\r\n") + "\r\n", eventCount, skippedCount };
}

/** Date-only deadlines stay date-only; DTEND is the exclusive following date. */
export async function createAllDayCalendarExport(entries: AllDayCalendarEntry[], name: string, generatedAt = new Date()): Promise<CalendarExportResult> {
  if (!Number.isFinite(generatedAt.getTime()) || generatedAt.getUTCFullYear() < 1 || generatedAt.getUTCFullYear() > 9999)
    throw new RangeError("Некорректное время создания календаря.");
  const lines = ["BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Расписание военмех//Расписание//RU", "CALSCALE:GREGORIAN", `X-WR-CALNAME:${escaped(name)}`];
  const seen = new Set<string>(); let skippedCount = 0, eventCount = 0;
  for (const entry of entries) {
    const day = validDay(entry.day);
    if (!entry.id.trim() || !entry.summary.trim() || !day || entry.day === "9999-12-31") { skippedCount++; continue; }
    const start = entry.day.replaceAll("-", ""); day.setUTCDate(day.getUTCDate() + 1);
    const end = day.toISOString().slice(0, 10).replaceAll("-", "");
    const digest = await crypto.subtle.digest("SHA-256", encoder.encode(`${entry.id}\nDATE:${start}`));
    const uid = [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, "0")).join("");
    if (seen.has(uid)) { skippedCount++; continue; } seen.add(uid); eventCount++;
    lines.push("BEGIN:VEVENT", `UID:${uid}`, `DTSTAMP:${utc(generatedAt)}`, `DTSTART;VALUE=DATE:${start}`, `DTEND;VALUE=DATE:${end}`, "TRANSP:TRANSPARENT", `SUMMARY:${escaped(entry.summary)}`);
    if (entry.description?.trim()) lines.push(`DESCRIPTION:${escaped(entry.description)}`);
    lines.push("END:VEVENT");
  }
  lines.push("END:VCALENDAR"); return { content: lines.map(folded).join("\r\n") + "\r\n", eventCount, skippedCount };
}
