import { parityOf } from "./parity.ts";

export type MobileTab = "schedule" | "maps" | "homework" | "chat" | "sections";

/** #27: вкладка для пути. Страницы вне таб-панели (раздел из «Разделов», настройки, 404) не подсвечивают ни одну
 * вкладку — «Разделы» подсвечены, только пока открыт их список. */
export function mobileTabForPath(pathname: string): MobileTab | null {
  const path = pathname.replace(/\/+$/, "") || "/";
  if (path === "/" || path === "/schedule") return "schedule";
  if (path === "/maps") return "maps";
  if (path === "/homework") return "homework";
  if (path === "/group" || path === "/chat" || path.startsWith("/chat/")) return "chat";
  return null;
}

/** Страницы, у которых без группы уже есть своя кнопка «Выбрать группу» в пустом состоянии. */
const groupCtaPages = new Set(["/", "/schedule", "/week", "/summary", "/homework", "/group", "/community", "/friends"]);

/** #27 (W-12): чип группы в шапке не дублирует CTA пустого состояния и не спорит с «Назад» в настройках. */
export function showGroupChip(pathname: string, hasGroup: boolean): boolean {
  const path = pathname.replace(/\/+$/, "") || "/";
  if (path === "/settings" || path.startsWith("/settings/") || path.startsWith("/legal/")) return false;
  if (!hasGroup && groupCtaPages.has(path)) return false;
  return true;
}

export function studyGroupCaption(groupName: string | undefined, period: { start: string; weekCount: number } | null | undefined,
  date: Date, invert: boolean): string {
  const name = groupName?.trim();
  if (!name) return "Выбрать группу";
  if (!period || !/^\d{4}-\d{2}-\d{2}/.test(period.start) || !Number.isFinite(Date.parse(period.start)) ||
    !Number.isInteger(period.weekCount) || period.weekCount <= 0 || !Number.isFinite(date.getTime())) return name;
  const parity = parityOf(date, period.start, period.weekCount, invert);
  return `${name} · ${parity === 1 ? "нечётная" : parity === 2 ? "чётная" : `${parity}-я неделя`}`;
}

/**
 * r2: сколько места снизу оставить под нижнюю навигацию — её реальная высота. Раньше было не меньше 64px,
 * и на низком окне (200% масштаба) компактная панель всё равно отнимала 64px. 0 — панель скрыта или не измерена.
 */
export function bottomNavReserve(height: number): number {
  return Number.isFinite(height) && height > 0 ? Math.ceil(height) : 64;
}
