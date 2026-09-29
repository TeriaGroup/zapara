import type { GroupHome, SocialHome } from "./types";

export type ChatInboxItem = {
  kind: "group" | "classmate" | "personal";
  conversationId: string;
  communityId: string | null;
  peerUserId: string | null;
  title: string;
  preview: string | null;
  lastAt: string | null;
  unread: number;
};

export function chatInboxTime(iso: string, now = new Date()): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return date.toDateString() === now.toDateString()
    ? date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })
    : date.toLocaleDateString("ru-RU", { day: "numeric", month: "short", year: "numeric" });
}

export function unreadChatTotal(rows: ChatInboxItem[]): number {
  return rows.reduce((sum, row) => sum + Math.max(0, row.unread), 0);
}

export function filterChatInbox(rows: ChatInboxItem[], query: string, kind: ChatInboxItem["kind"] | "all"): ChatInboxItem[] {
  const needle = query.trim().toLocaleLowerCase("ru-RU");
  return rows.filter(row => (kind === "all" || row.kind === kind) &&
    (!needle || `${row.title} ${row.preview || ""}`.toLocaleLowerCase("ru-RU").includes(needle)));
}

/** The two existing APIs stay separate; only the presentation list is shared. */
export function mergeChatInbox(groups: GroupHome[], social: SocialHome | null): ChatInboxItem[] {
  const rows: ChatInboxItem[] = [];
  for (const home of groups) {
    rows.push({ kind: "group", conversationId: home.groupChat.conversationId,
      communityId: home.communityId, peerUserId: null, title: home.groupName || home.name,
      preview: home.groupChat.lastBody, lastAt: home.groupChat.lastAt, unread: home.groupChat.unread });
    for (const chat of home.directs) rows.push({ kind: "classmate", conversationId: chat.conversationId,
      communityId: home.communityId, peerUserId: chat.peerUserId, title: `${chat.title} · ${home.groupName || home.name}`,
      preview: chat.lastBody, lastAt: chat.lastAt, unread: chat.unread });
  }
  for (const friend of social?.friends ?? []) rows.push({ kind: "personal",
    conversationId: friend.conversationId, communityId: null, peerUserId: friend.userId,
    title: friend.displayName?.trim() || friend.username,
    preview: friend.lastBody, lastAt: friend.lastAt, unread: friend.unread });
  return sortChatInbox(rows);
}

export function sortChatInbox(rows: ChatInboxItem[]): ChatInboxItem[] {
  return rows.sort((left, right) => {
    if (left.lastAt === null) return right.lastAt === null ? left.title.localeCompare(right.title, "ru") : 1;
    if (right.lastAt === null) return -1;
    return right.lastAt.localeCompare(left.lastAt) || left.title.localeCompare(right.title, "ru");
  });
}
