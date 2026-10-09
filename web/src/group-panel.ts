/** #17 (W-14): одно поведение панелей группы на desktop и телефоне. */

/**
 * «В группе сейчас» открывается одинаково на любой ширине: свёрнутой строкой-сводкой.
 * Раньше на desktop она была развёрнута («Свернуть»), а на телефоне свёрнута («Развернуть») — одна панель с разными подписями.
 * Выбор пользователя запоминается для группы на время сессии.
 */
export const groupContextExpandedByDefault = false;

export function groupContextExpanded(choices: Record<string, boolean>, communityId: string): boolean {
  return choices[communityId] ?? groupContextExpandedByDefault;
}

export function groupContextToggleLabel(expanded: boolean): string {
  return expanded ? "Свернуть" : "Развернуть";
}

const norm = (value: string) => value.toLocaleLowerCase("ru-RU").replace(/\s+/g, " ").trim();

/** Подзаголовок группы без повтора кода: «Группа И831Б», а не «Группа И831Б · И831Б». */
export function groupSubtitle(name: string, groupName: string | null | undefined): string {
  const title = name.trim(); const code = (groupName || "").trim();
  if (!code) return title;
  if (!title || norm(title) === norm(code)) return code;
  return norm(title).includes(norm(code)) ? title : `${title} · ${code}`;
}
