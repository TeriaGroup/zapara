/** A missing or empty choice stays unselected; an unavailable stored group is cleared. */
export function resolveStoredGroup(stored: string | null, groups: { id: string }[]): string {
  if (stored === "") return "";
  if (stored && groups.some(group => group.id === stored)) return stored;
  return "";
}

/** Shared homework follows the timetable group, never the first membership by name. */
export function homeworkCommunityId(
  groupId: string,
  rows: { communityId: string; role: string | null; groupId: string }[],
): string {
  const selected = groupId.trim();
  if (!selected) return "";
  return rows.find(row => row.role && row.groupId === selected)?.communityId ?? "";
}

export type CommunityFollow = { communityId: string; failed: boolean };
/**
 * Отсутствие членства — не ошибка загрузки (#36): отдельное состояние с действием.
 * missing: "group" — группа не выбрана; "membership" — группа выбрана, но пользователь в её сообществе не состоит
 * (candidate — сообщество группы, куда можно отправить заявку, если оно есть).
 */
export type GroupFace = {
  communityId: string;
  error: string;
  missing?: "group" | "membership";
  candidate?: { communityId: string; name: string } | null;
};

type Membership = { communityId: string; role: string | null; name?: string };

/** Drop the previous community before the lookup, so a slow response cannot keep the group the user just left. */
export async function followGroupCommunity(
  input: { authenticated: boolean; groupId: string },
  load: (groupId: string) => Promise<Membership[]>,
  publish: (state: CommunityFollow) => void,
): Promise<void> {
  publish({ communityId: "", failed: false });
  const selected = input.groupId.trim();
  if (!input.authenticated || !selected) return;
  try {
    const list = await load(selected);
    publish({
      communityId: homeworkCommunityId(selected, list.map(item => ({ ...item, groupId: selected }))),
      failed: false,
    });
  } catch {
    publish({ communityId: "", failed: true });
  }
}

/** The open group, chat, desk, and ballots belong to the timetable group. A new choice starts blank. */
export async function openGroupFace(
  input: { authenticated: boolean; groupId: string },
  load: (groupId: string) => Promise<Membership[]>,
  publish: (face: GroupFace) => void,
): Promise<void> {
  publish({ communityId: "", error: "" });
  if (!input.authenticated) return;
  const selected = input.groupId.trim();
  if (!selected) {
    publish({ communityId: "", error: "", missing: "group" });
    return;
  }
  try {
    const list = await load(selected);
    const communityId = homeworkCommunityId(selected, list.map(item => ({ ...item, groupId: selected })));
    if (communityId) { publish({ communityId, error: "" }); return; }
    const open = list[0];
    publish({ communityId: "", error: "", missing: "membership",
      candidate: open ? { communityId: open.communityId, name: open.name || "Сообщество группы" } : null });
  } catch {
    publish({ communityId: "", error: "Не удалось загрузить группу" });
  }
}
