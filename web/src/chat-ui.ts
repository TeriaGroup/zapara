/** #17: общая лексика и правила чата — одни на desktop и телефоне. */
import { S } from "./strings.gen.ts";

/** Вкладки группы на всех размерах: «Чат · Каналы · Участники». */
export const groupTabs = [["general", S.groupChat], ["channels", "Каналы"], ["people", "Участники"]] as const;

/** Общий поток группы называется «Чат», как и вкладка; остальные каналы — своим названием. */
export function channelTitle(topic: { topicId: string | null; title: string }): string {
  return topic.topicId === null ? S.groupChat : topic.title;
}

/** Есть физическая клавиатура и точный указатель — тогда подсказка про Shift+Enter уместна. */
export function hasDesktopKeyboard(win: Pick<Window, "matchMedia"> | undefined = typeof window === "undefined" ? undefined : window): boolean {
  return !!win?.matchMedia?.("(hover: hover) and (pointer: fine)").matches;
}

/** Строка под композером: счётчик, подсказка Shift+Enter (только с клавиатурой), ошибка. */
export function composerHelp(count: number, error: string, desktopKeyboard: boolean): string {
  return [`${count}/2000`, desktopKeyboard ? "Shift+Enter — новая строка" : "", error].filter(Boolean).join(" · ");
}

/** Подписи кнопок композера: всплывающая подсказка = доступное имя. */
export const composerLabels = {
  attach: "Прикрепить файл, опрос или карточку пары",
  circle: "Записать кружок",
  voice: "Записать голосовое",
  send: "Отправить",
} as const;

/** Фильтры входящих нужны только при заметном числе бесед. */
export const inboxFiltersFrom = 5;
export function showInboxFilters(count: number): boolean {
  return count >= inboxFiltersFrom;
}

/** «Чат» сразу открывает беседу, если она одна; «?all» — явный переход к списку (кнопка «Ко всем беседам»). */
export function singleChat<T>(rows: T[], loading: boolean, error: string, search: string): T | null {
  if (loading || error || rows.length !== 1) return null;
  if (new URLSearchParams(search).has("all")) return null;
  return rows[0];
}
