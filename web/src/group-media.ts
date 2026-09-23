export class GroupMediaError extends Error {
  readonly code: "kind" | "size";
  constructor(code: "kind" | "size") {
    super(code);
    this.code = code;
  }
}

export const groupMediaLimit = 8 * 1024 * 1024;

export function groupBubbleText(message: { kind?: string; body: string; deleted?: boolean }): string {
  if (message.deleted) return "Сообщение удалено";
  if (message.kind === "image") return "Фото";
  if (message.kind === "video" || message.kind === "circle") return "Видео";
  if (message.kind === "voice") return "Голосовое";
  if (message.kind === "file") return message.body.trim() ? message.body : "Документ";
  return message.body;
}

export async function postGroupMedia(id: string, kind: "image" | "video" | "file", name: string, file: Blob, replyTo: string | undefined, post: typeof fetch, headers: Record<string, string>): Promise<Response> {
  const prepared = groupMediaRequest(kind, name, file.size, replyTo);
  return post(`/web-api/communities/conversations/${id}/media`, {
    method: "POST",
    credentials: "same-origin",
    headers: { ...headers, ...prepared.headers },
    body: file,
  });
}

export function groupMediaRequest(kind: string, name: string, bytes: number, replyTo?: string): { name: string; headers: Record<string, string> } {
  if (kind !== "image" && kind !== "video" && kind !== "file") throw new GroupMediaError("kind");
  if (!Number.isFinite(bytes) || bytes < 1 || bytes > groupMediaLimit) throw new GroupMediaError("size");
  const base = name.trim().split(/[/\\]/).pop() ?? "";
  const clean = (base || (kind === "image" ? "Фото" : kind === "video" ? "Видео" : "Документ")).slice(0, 80);
  const headers: Record<string, string> = {
    "Content-Type": "application/octet-stream",
    "X-Zapara-Kind": kind,
    "X-Zapara-Name": encodeURIComponent(clean),
  };
  if (replyTo) headers["X-Zapara-Reply"] = replyTo;
  return { name: clean, headers };
}
