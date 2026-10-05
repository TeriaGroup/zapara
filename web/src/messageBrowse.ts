import type { ChatMessage } from "./types";

export type MessageBrowseFilter = {
  query: string;
  author: "all" | "mine" | "others";
  kind: "all" | "text" | "visual" | "file" | "audio";
};

export function filterMessages(messages: ChatMessage[], filter: MessageBrowseFilter, selfId: string): ChatMessage[] {
  const query = filter.query.trim().toLocaleLowerCase("ru-RU");
  return messages.filter(message => {
    if (message.deleted && (query || filter.author !== "all" || filter.kind !== "all")) return false;
    if (filter.author === "mine" && message.senderId !== selfId) return false;
    if (filter.author === "others" && message.senderId === selfId) return false;
    if (query && (message.deleted || !message.body.toLocaleLowerCase("ru-RU").includes(query))) return false;
    if (filter.kind === "all") return true;
    if (message.deleted) return false;
    const kind = message.kind || "text";
    if (filter.kind === "text") return kind === "text";
    if (filter.kind === "visual") return kind === "image" || kind === "video";
    if (filter.kind === "audio") return kind === "voice" || kind === "circle";
    return kind === "file";
  });
}

export function canCopyMessageText(message: ChatMessage): boolean {
  return !message.deleted && (!message.kind || message.kind === "text") && !!message.body.trim();
}

export function messageDayKey(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;
}

export function sameMessageCluster(before: ChatMessage, after: ChatMessage): boolean {
  if (before.senderId !== after.senderId || messageDayKey(before.createdAt) !== messageDayKey(after.createdAt)) return false;
  const gap = new Date(after.createdAt).getTime() - new Date(before.createdAt).getTime();
  return gap >= 0 && gap <= 5 * 60 * 1000;
}
