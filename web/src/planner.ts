import { addDays, isoDay, lessonsOn, sameSubject } from "./parity.ts";
import type { HomeworkItem, Lesson } from "./types";
export function localDay(value: string): Date | null {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value))
        return null;
    const [y, m, d] = value.split("-").map(Number);
    const date = new Date(y, m - 1, d);
    return isoDay(date) === value ? date : null;
}
export function clockMinutes(value: string): number | null {
    const match = /^(\d{1,2}):(\d{2})$/.exec(value);
    if (!match || +match[1] > 23 || +match[2] > 59)
        return null;
    return +match[1] * 60 + +match[2];
}
export function minuteClock(value: number) { return `${Math.floor(value / 60).toString().padStart(2, "0")}:${(value % 60).toString().padStart(2, "0")}`; }
export function freeGaps(lessons: Pick<Lesson, "timeStart" | "timeEnd">[]) {
    const ranges = lessons.flatMap(lesson => {
        const start = clockMinutes(lesson.timeStart), end = clockMinutes(lesson.timeEnd);
        return start !== null && end !== null && end > start ? [{ start, end }] : [];
    }).sort((a, b) => a.start - b.start || a.end - b.end);
    const merged: {
        start: number;
        end: number;
    }[] = [];
    for (const range of ranges) {
        const last = merged.at(-1);
        if (last && range.start <= last.end)
            last.end = Math.max(last.end, range.end);
        else
            merged.push({ ...range });
    }
    return merged.flatMap((range, i) => i && range.start - merged[i - 1].end >= 30
        ? [{ start: merged[i - 1].end, end: range.start, duration: range.start - merged[i - 1].end }] : []);
}
export function heroLesson(lessons: Lesson[], date: Date, now: Date) {
    if (isoDay(date) < isoDay(now))
        return null;
    if (isoDay(date) > isoDay(now))
        return lessons[0] ?? null;
    const minute = now.getHours() * 60 + now.getMinutes();
    return lessons.find(lesson => (clockMinutes(lesson.timeEnd) ?? 0) > minute) ?? null;
}
export function nearbyHomework(items: HomeworkItem[], date: Date, subjects: string[]) {
    const start = new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
    const end = addDays(new Date(start), 3).getTime();
    return items.filter(item => item.deadlineAt
        ? Number.isFinite(Date.parse(item.deadlineAt)) && Date.parse(item.deadlineAt) >= start && Date.parse(item.deadlineAt) < end
        : subjects.some(subject => sameSubject(subject, item.subject)))
        .sort((a, b) => (a.deadlineAt ? Date.parse(a.deadlineAt) : Infinity) - (b.deadlineAt ? Date.parse(b.deadlineAt) : Infinity));
}
export function absoluteDate(date: Date) { return date.toLocaleDateString("ru-RU", { weekday: "long", day: "numeric", month: "long", year: "numeric" }); }
export function personalHomeworkDue(item: HomeworkItem, lessons: Lesson[], period: import("./types").Period, invert: boolean): Date | null {
    const created = item.legacyCreatedLocalDate ? localDay(item.legacyCreatedLocalDate) : new Date(item.created);
    if (!created || !Number.isFinite(created.getTime()))
        return null;
    const base = new Date(created.getFullYear(), created.getMonth(), created.getDate());
    let found = 0;
    const nth = Math.max(1, Math.min(10, item.targetNthOccurrence ?? 1));
    for (let offset = 1; offset <= 120; offset++) {
        const date = addDays(base, offset);
        if (lessonsOn(lessons, date, period.start, period.weekCount, invert).some(lesson => sameSubject(lesson.subjectRaw, item.subject)) && ++found === nth)
            return date;
    }
    return null;
}
export function hasLessonOverlap(lessons: Pick<Lesson, "timeStart" | "timeEnd">[]) {
    const ranges = lessons.map(lesson => ({ start: clockMinutes(lesson.timeStart), end: clockMinutes(lesson.timeEnd) })).filter((range): range is { start: number; end: number } => range.start !== null && range.end !== null && range.end > range.start).sort((a,b) => a.start - b.start);
    let end = -1;
    for (const range of ranges) { if (range.start < end) return true; end = Math.max(end, range.end); }
    return false;
}
