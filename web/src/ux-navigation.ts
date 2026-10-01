import { addDays, parityOf, weekday, weekCode } from "./parity.ts";
import { summaryCode } from "./summary.ts";
import type { MapPlan, Period } from "./types.ts";

export function calendarWeek(date: Date): Date[] {
  const monday = addDays(date, 1 - weekday(date));
  return Array.from({ length: 7 }, (_, index) => addDays(monday, index));
}

// Summary already translates its label through invert; applying it here again would undo it.
export function currentSummarySegment(now: Date, period: Period): number {
  return weekCode(now, period.start, period.weekCount) === 2 ? 1 : 0;
}

export function summaryDayDate(now: Date, period: Period, segment: number, invert: boolean, day: number): Date {
  let monday = calendarWeek(now)[0];
  const wanted = summaryCode(segment, invert);
  if (wanted && parityOf(monday, period.start, period.weekCount, invert) !== wanted) monday = addDays(monday, 7);
  return addDays(monday, Math.max(1, Math.min(7, day)) - 1);
}

/** Select a known plan, never a guessed room marker. */
export function roomPlan(plans: MapPlan[], room: string, building = ""): MapPlan | null {
  const value = `${room} ${building}`.toLocaleUpperCase("ru-RU");
  if (!/\d/.test(room) || /ДИСТАНЦ|ОНЛАЙН/.test(value)) return null;
  const explicit = /УЛК|\*/.test(value) ? "УЛК" : /ГК|ВЦ/.test(value) ? "ГК" : building.trim();
  const label = explicit || "ГК";
  const candidates = plans.filter(plan => plan.building.toLocaleUpperCase("ru-RU") === label.toLocaleUpperCase("ru-RU"));
  const number = room.match(/(?:^|\D)(\d{3,4})[а-яa-z]?(?:\D|$)/i)?.[1];
  if (!number) return null;
  const floor = Number(number[0]);
  return candidates.find(plan => plan.floor === floor) ?? null;
}

/** A render can invalidate an old response before its effect cleanup runs. */
export class RequestEpoch {
  private owner = "";
  private revision = 0;
  scope(owner: string) { if (this.owner !== owner) { this.owner = owner; this.revision++; } }
  begin(owner: string): () => boolean {
    this.scope(owner);
    const revision = ++this.revision;
    return () => this.owner === owner && this.revision === revision;
  }
  invalidate() { this.revision++; }
}
