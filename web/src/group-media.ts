export class GroupMediaError extends Error {
  readonly code: "kind" | "size";
  constructor(code: "kind" | "size") {
    super(code);
    this.code = code;
  }
}

export const groupMediaLimit = 8 * 1024 * 1024;

const mediaId = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

export type GroupMediaDownload = { href: string; filename: string; label: string };

function mediaFilename(body: string, kind: "image" | "video" | "file"): string {
  const fallback = kind === "image" ? "Фото" : kind === "video" ? "Видео" : "Документ";
  const basename = body.replace(/\\/g, "/").split("/").pop() ?? "";
  const clean = basename
    .replace(/[\u0000-\u001f\u007f-\u009f<>:"|?*\u202a-\u202e\u2066-\u2069]/g, "")
    .trim().replace(/^[. ]+|[. ]+$/g, "").slice(0, 80).replace(/[. ]+$/g, "");
  if (!clean) return fallback;
  return /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(clean) ? `_${clean}` : clean;
}

export function groupMediaDownload(conversationId: string, message: { messageId: string; kind?: string; body: string; deleted?: boolean }): GroupMediaDownload | null {
  if (message.deleted || !mediaId.test(conversationId) || !mediaId.test(message.messageId)) return null;
  if (message.kind !== "image" && message.kind !== "video" && message.kind !== "file") return null;
  const filename = mediaFilename(message.body, message.kind);
  const label = message.kind === "image" ? "Скачать фото" : message.kind === "video" ? "Скачать видео" : `Скачать документ: ${filename}`;
  return {
    href: `/web-api/communities/conversations/${conversationId}/messages/${message.messageId}/media`,
    filename,
    label,
  };
}

export async function getGroupMedia(download: GroupMediaDownload, get: typeof fetch, headers: Record<string, string>): Promise<Blob> {
  const response = await get(download.href, {
    credentials: "same-origin",
    headers: { ...headers, Accept: "application/octet-stream" },
  });
  if (!response.ok) throw new Error(String(response.status));
  return response.blob();
}

export function groupBubbleText(message: { kind?: string; body: string; deleted?: boolean }): string {
  if (message.deleted) return "Сообщение удалено";
  if (message.kind === "image") return "Фото";
  if (message.kind === "video" || message.kind === "circle") return "Видео";
  if (message.kind === "voice") return "Голосовое";
  if (message.kind === "file") return mediaFilename(message.body, "file");
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
