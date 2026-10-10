import { clockMinutes, freeGaps, minuteClock } from "./planner.ts";
import { isoDay } from "./parity.ts";
import type { Lesson } from "./types";

/** Одна пара (90 мин). Промежуток короче — плановый «Перерыв», не короче — «Окно» (#13, глоссарий). */
export const windowMinutes = 90;

export type BreakItem = { kind: "break" | "window"; start: number; end: number; duration: number; label: string; durationLabel: string };
export type TimelineItem<L> = { kind: "lesson"; lesson: L; index: number } | BreakItem;

export function durationLabel(minutes: number) {
  const hours = Math.floor(minutes / 60), rest = minutes % 60;
  return [hours ? `${hours} ч` : "", rest ? `${rest} мин` : ""].filter(Boolean).join(" ") || "0 мин";
}

export function breakItem(gap: { start: number; end: number; duration: number }): BreakItem {
  const kind = gap.duration >= windowMinutes ? "window" : "break";
  return { kind, ...gap, label: `${kind === "window" ? "Окно" : "Перерыв"} ${minuteClock(gap.start)}–${minuteClock(gap.end)}`, durationLabel: durationLabel(gap.duration) };
}

/** Пары, перерывы и окна одним списком по времени (на «Сегодня» и «Неделе» одинаково). */
export function dayTimeline<L extends Pick<Lesson, "timeStart" | "timeEnd">>(lessons: L[]): TimelineItem<L>[] {
  const items: (TimelineItem<L> & { at: number; order: number })[] = lessons.map((lesson, index) => ({ kind: "lesson" as const, lesson, index, at: clockMinutes(lesson.timeStart) ?? 0, order: 1 }));
  for (const gap of freeGaps(lessons)) items.push({ ...breakItem(gap), at: gap.start, order: 0 });
  return items.sort((a, b) => a.at - b.at || a.order - b.order || (a.kind === "lesson" && b.kind === "lesson" ? a.index - b.index : 0))
    .map(({ at: _at, order: _order, ...item }) => item as TimelineItem<L>);
}

export type LessonPhase = "past" | "current" | "next" | "later";
export type LessonStatus = { phase: LessonPhase; progress: number; caption: string };

/** Состояние пары относительно текущего времени: прошла, идёт (с прогрессом), следующая, позже. */
export function lessonStatuses<L extends Pick<Lesson, "timeStart" | "timeEnd">>(lessons: L[], date: Date, now: Date): LessonStatus[] {
  const day = isoDay(date), today = isoDay(now);
  const minute = now.getHours() * 60 + now.getMinutes() + now.getSeconds() / 60;
  let nextTaken = false;
  return lessons.map(lesson => {
    const start = clockMinutes(lesson.timeStart) ?? 0, end = clockMinutes(lesson.timeEnd) ?? start + 95;
    if (day < today || day === today && end <= minute) return { phase: "past", progress: 1, caption: "Прошла" };
    if (day === today && start <= minute) {
      nextTaken = true;
      return { phase: "current", progress: Math.min(1, Math.max(0, (minute - start) / Math.max(1, end - start))), caption: `Идёт · до ${lesson.timeEnd}` };
    }
    if (!nextTaken) { nextTaken = true; return { phase: "next", progress: 0, caption: day === today ? "Следующая" : "Первая пара" }; }
    return { phase: "later", progress: 0, caption: "" };
  });
}

/** Закреплённая сводка вверху «Сегодня»: «Сейчас: до 20:05 · 455» или «Следующая: 18:30, 455». */
export function nextSummary<L extends Pick<Lesson, "timeStart" | "timeEnd" | "roomRaw" | "classroomRaw">>(lessons: L[], date: Date, now: Date): string | null {
  const statuses = lessonStatuses(lessons, date, now);
  const at = statuses.findIndex(status => status.phase === "current" || status.phase === "next");
  if (at < 0) return null;
  const lesson = lessons[at], room = (lesson.roomRaw || lesson.classroomRaw || "").trim();
  return statuses[at].phase === "current"
    ? `Сейчас: до ${lesson.timeEnd}${room ? ` · ${room}` : ""}`
    : `${isoDay(date) === isoDay(now) ? "Следующая" : "Первая"}: ${lesson.timeStart}${room ? `, ${room}` : ""}`;
}
