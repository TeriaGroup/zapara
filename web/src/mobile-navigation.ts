import { parityOf } from "./parity.ts";

export type MobileTab = "schedule" | "maps" | "homework" | "chat" | "sections";

export function mobileTabForPath(pathname: string): MobileTab {
  const path = pathname.replace(/\/+$/, "") || "/";
  if (path === "/" || path === "/schedule") return "schedule";
  if (path === "/maps") return "maps";
  if (path === "/homework") return "homework";
  if (path === "/group" || path === "/chat" || path.startsWith("/chat/")) return "chat";
  return "sections";
}

export function studyGroupCaption(groupName: string | undefined, period: { start: string; weekCount: number } | null | undefined,
  date: Date, invert: boolean): string {
  const name = groupName?.trim();
  if (!name) return "Выбрать группу";
  if (!period || !/^\d{4}-\d{2}-\d{2}/.test(period.start) || !Number.isFinite(Date.parse(period.start)) ||
    !Number.isInteger(period.weekCount) || period.weekCount <= 0 || !Number.isFinite(date.getTime())) return name;
  const parity = parityOf(date, period.start, period.weekCount, invert);
  return `${name} · ${parity === 1 ? "нечёт." : parity === 2 ? "чёт." : `${parity}-я нед.`}`;
}
