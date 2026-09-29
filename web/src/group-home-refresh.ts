import type { Conversation, GroupHome } from "./types";

export function reconcileGroupHomeChat(active: Conversation | null, home: GroupHome): Conversation | null {
  if (!active) return null;
  return [home.groupChat, ...home.directs].find(row => row.conversationId === active.conversationId) ?? active;
}
