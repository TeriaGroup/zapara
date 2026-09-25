export function matchesBrowseQuery(query: string, ...values: Array<string | null | undefined>): boolean {
  const needle = query.trim().toLocaleLowerCase("ru-RU");
  return !needle || values.some(value => value?.toLocaleLowerCase("ru-RU").includes(needle));
}

export function isNearLatest(scrollTop: number, clientHeight: number, scrollHeight: number): boolean {
  return scrollHeight - scrollTop - clientHeight <= 48;
}
