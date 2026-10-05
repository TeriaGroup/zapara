export function friendGroupChoice(value: string, groups: { id: string; name: string }[]): { name: string; manual: boolean; error?: string } {
  const name = value.trim();
  if (!name) return { name, manual: false, error: "Укажите группу." };
  const known = groups.find(group => group.id === name || group.name.toLocaleLowerCase("ru-RU") === name.toLocaleLowerCase("ru-RU"));
  if (known) return { name: known.name, manual: false };
  if (groups.length) return { name, manual: false, error: "Группа не найдена в загруженном каталоге. Выберите название из списка." };
  return { name, manual: true };
}
