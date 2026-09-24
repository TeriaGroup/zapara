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
export type GroupFace = { communityId: string; error: string };

type Membership = { communityId: string; role: string | null };

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
    publish({ communityId: "", error: "Сначала выберите группу в настройках" });
    return;
  }
  try {
    const list = await load(selected);
    const communityId = homeworkCommunityId(selected, list.map(item => ({ ...item, groupId: selected })));
    publish(communityId
      ? { communityId, error: "" }
      : { communityId: "", error: "Вы ещё не в группе" });
  } catch {
    publish({ communityId: "", error: "Не удалось загрузить группу" });
  }
}
