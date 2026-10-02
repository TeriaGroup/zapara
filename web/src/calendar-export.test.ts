import assert from "node:assert/strict";
import { test } from "node:test";
import { calendarOccurrenceId, createCalendarExport, moscowLessonInstant, type CalendarEntry } from "./calendar-export.ts";

const event: CalendarEntry = { id: "3313|2026-10-02|Математика", start: "2026-10-02T09:00:00+03:00",
  end: "2026-10-02T10:35:00+03:00", summary: "Математика", location: "320 УЛК" };
const now = new Date("2026-10-01T20:00:00Z");
test("raw occurrence identity has a portable unambiguous UTF16 length prefix", () => {
  assert.equal(calendarOccurrenceId("3313", "Математика", "Петров", "320*;"), "4:331310:Математика6:Петров5:320*;");
  assert.equal(calendarOccurrenceId("a", "🚀"), "1:a2:🚀0:0:");
  assert.notEqual(calendarOccurrenceId("a\nb", "c"), calendarOccurrenceId("a", "b\nc"));
});
test("calendar export uses UTC events and independent generation time", async () => {
  const result = await createCalendarExport([event], "Неделя 09С52", now);
  assert.equal(result.eventCount, 1); assert.equal(result.skippedCount, 0);
  assert.match(result.content, /DTSTART:20261002T060000Z\r\nDTEND:20261002T073500Z/);
  assert.match(result.content, /DTSTAMP:20261001T200000Z/);
  assert.match(result.content, /^BEGIN:VCALENDAR\r\n/); assert.ok(result.content.endsWith("END:VCALENDAR\r\n"));
});
test("Russian and emoji content folds by UTF8 bytes and cannot inject properties", async () => {
  const title = "Длинное название 🚀 ".repeat(12);
  const result = await createCalendarExport([{ ...event, summary: title, description: "Строка\nBEGIN:VEVENT; две, ещё\\путь" }], title, now);
  assert.ok(result.content.split("\r\n").filter(Boolean).every(line => Buffer.byteLength(line, "utf8") <= 75));
  const unfolded = result.content.replace(/\r\n /g, "");
  assert.ok(unfolded.includes(`SUMMARY:${title}`));
  assert.ok(unfolded.includes("DESCRIPTION:Строка\\nBEGIN:VEVENT\\; две\\, ещё\\\\путь"));
  assert.equal(unfolded.match(/^BEGIN:VEVENT$/gm)?.length, 1);
  assert.ok(!result.content.includes("�"));
});
test("identical occurrences deduplicate but different dates remain independent", async () => {
  const next = { ...event, start: "2026-10-09T09:00:00+03:00", end: "2026-10-09T10:35:00+03:00" };
  const result = await createCalendarExport([event, { ...event, start: "2026-10-02T06:00:00Z" }, next], "Пары", now);
  assert.equal(result.eventCount, 2); assert.equal(result.skippedCount, 1);
  const again = await createCalendarExport([event], "Пары", new Date("2026-10-02T20:00:00Z"));
  assert.equal(result.content.match(/UID:(\w+)/)?.[1], again.content.match(/UID:(\w+)/)?.[1]);
});
test("broken dates and intervals are omitted with an honest count", async () => {
  const result = await createCalendarExport([event, { ...event, id: "bad", start: "2026-02-30T09:00:00Z" },
    { ...event, id: "backwards", end: "2026-10-02T08:00:00+03:00" }, { ...event, id: "empty", summary: " " }], "Пары", now);
  assert.equal(result.eventCount, 1); assert.equal(result.skippedCount, 3);
  await assert.rejects(createCalendarExport([event], "Пары", new Date(NaN)));
});
test("Moscow lesson conversion is strict and independent of browser timezone", () => {
  assert.equal(moscowLessonInstant("2026-10-02", "09:00"), "2026-10-02T06:00:00.000Z");
  assert.equal(moscowLessonInstant("2026-10-02", "00:30"), "2026-10-01T21:30:00.000Z");
  assert.equal(moscowLessonInstant("2026-02-30", "09:00"), null);
  assert.equal(moscowLessonInstant("2026-10-02", "24:00"), null);
  assert.equal(moscowLessonInstant("2026-10-02", "9:00"), "2026-10-02T06:00:00.000Z");
});
