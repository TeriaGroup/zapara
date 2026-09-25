export function matchesBrowseQuery(query: string, ...values: Array<string | null | undefined>): boolean {
  const needle = query.trim().toLocaleLowerCase("ru-RU");
  return !needle || values.some(value => value?.toLocaleLowerCase("ru-RU").includes(needle));
}

export function isNearLatest(scrollTop: number, clientHeight: number, scrollHeight: number): boolean {
  return scrollHeight - scrollTop - clientHeight <= 48;
}

export function unreadBadgeText(count: number): string { return count > 99 ? "99+" : String(count); }

export function unreadBadgeDescription(count: number): string { return `Непрочитанных сообщений: ${count}`; }
