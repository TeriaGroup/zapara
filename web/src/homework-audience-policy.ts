import type { GroupHome, GroupSpace, HomeworkAudience } from "./types";

export const allHomeworkAudience = (): HomeworkAudience => ({ kind: "all", roleIds: [], userIds: [] });
export function audienceLabel(audience?: HomeworkAudience | null): string {
  if (!audience || audience.kind === "all") return "Вся группа";
  return `Выбраны: ${audience.roleIds.length} подгрупп, ${audience.userIds.length} участников`;
}
export function audienceMemberIds(audience: HomeworkAudience, home: GroupHome, space: GroupSpace): string[] {
  if (audience.kind === "all") return home.classmates.map(person => person.userId);
  const selected = new Set(audience.userIds);
  for (const grant of space.desk.grants) if (audience.roleIds.includes(grant.roleId)) selected.add(grant.userId);
  return home.classmates.filter(person => selected.has(person.userId)).map(person => person.userId);
}
