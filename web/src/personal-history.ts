import type { SocialMessage } from "./types.ts";

export function personalCopyText(message: Pick<SocialMessage, "body" | "kind" | "deleted">): string | null {
  if (message.deleted || !["text", "image", "file", "video"].includes(message.kind)) return null;
  const body = message.body?.trim();
  if (!body) return null;
  if (message.kind !== "text" && /^(?:https?:\/\/[^/]+)?\/?(?:web-api|api\/v1)\//i.test(body)) return null;
  return body;
}

export function searchPersonalHistory<T extends Pick<SocialMessage, "body" | "kind" | "deleted" | "fileName">>(messages: T[], query: string): T[] {
  const needle = query.trim().toLocaleLowerCase("ru-RU");
  if (!needle) return messages;
  return messages.filter(message => !message.deleted &&
    `${personalCopyText(message) || ""} ${message.fileName || ""}`.toLocaleLowerCase("ru-RU").includes(needle));
}
