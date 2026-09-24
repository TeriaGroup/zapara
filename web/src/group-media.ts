export class GroupMediaError extends Error {
  readonly code: "kind" | "size" | "duration" | "format";
  constructor(code: "kind" | "size" | "duration" | "format") {
    super(code);
    this.code = code;
  }
}

export const groupMediaLimit = 8 * 1024 * 1024;
export const groupVoiceLimit = 2 * 1024 * 1024;
export type GroupMediaKind = "image" | "video" | "file" | "voice" | "circle";

const mediaId = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

export type GroupMediaDownload = { href: string; filename: string; label: string; kind: GroupMediaKind };

function mediaFilename(body: string, kind: GroupMediaKind): string {
  const fallback = kind === "image" ? "Фото" : kind === "video" ? "Видео" : kind === "voice" ? "Голосовое" : kind === "circle" ? "Кружок" : "Документ";
  const basename = body.replace(/\\/g, "/").split("/").pop() ?? "";
  const clean = basename
    .replace(/[\u0000-\u001f\u007f-\u009f<>:"|?*\u202a-\u202e\u2066-\u2069]/g, "")
    .trim().replace(/^[. ]+|[. ]+$/g, "").slice(0, 80).replace(/[. ]+$/g, "");
  if (!clean) return fallback;
  return /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(clean) ? `_${clean}` : clean;
}

export function groupMediaDownload(conversationId: string, message: { messageId: string; kind?: string; body: string; deleted?: boolean }): GroupMediaDownload | null {
  if (message.deleted || !mediaId.test(conversationId) || !mediaId.test(message.messageId)) return null;
  if (message.kind !== "image" && message.kind !== "video" && message.kind !== "file" && message.kind !== "voice" && message.kind !== "circle") return null;
  const filename = mediaFilename(message.body, message.kind);
  const label = message.kind === "image" ? "Скачать фото" : message.kind === "video" ? "Скачать видео" :
    message.kind === "voice" ? "Скачать голосовое" : message.kind === "circle" ? "Скачать кружок" : `Скачать документ: ${filename}`;
  return {
    href: `/web-api/communities/conversations/${conversationId}/messages/${message.messageId}/media`,
    filename,
    label,
    kind: message.kind,
  };
}

export async function getGroupMedia(download: GroupMediaDownload, get: typeof fetch, headers: Record<string, string>): Promise<Blob> {
  const response = await get(download.href, {
    credentials: "same-origin",
    headers: { ...headers, Accept: "application/octet-stream" },
  });
  if (!response.ok) throw new Error(String(response.status));
  if (Number(response.headers.get("Content-Length")) > groupMediaLimit) throw new GroupMediaError("size");
  const blob = await response.blob();
  if (blob.size > groupMediaLimit) throw new GroupMediaError("size");
  const signature = new Uint8Array(await blob.slice(0, 16).arrayBuffer());
  return blob.slice(0, blob.size, mediaMime(download.kind, download.filename, signature));
}

function mediaMime(kind: GroupMediaKind, filename: string, bytes: Uint8Array): string {
  if (kind === "file") return "application/octet-stream";
  const webm = bytes[0] === 0x1a && bytes[1] === 0x45 && bytes[2] === 0xdf && bytes[3] === 0xa3;
  const mp4 = bytes[4] === 102 && bytes[5] === 116 && bytes[6] === 121 && bytes[7] === 112;
  if (kind === "voice") {
    if (webm) return "audio/webm";
    if (bytes[0] === 79 && bytes[1] === 103 && bytes[2] === 103 && bytes[3] === 83) return "audio/ogg";
    if (mp4) return "audio/mp4";
    if ((bytes[0] === 73 && bytes[1] === 68 && bytes[2] === 51) || (bytes[0] === 0xff && (bytes[1] & 0xe0) === 0xe0)) return "audio/mpeg";
    return "application/octet-stream";
  }
  if (webm) return "video/webm";
  if (mp4) return "video/mp4";
  if (kind === "circle") return "application/octet-stream";
  if (kind === "image") {
    if (bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47) return "image/png";
    if (bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) return "image/jpeg";
    if (bytes[0] === 0x47 && bytes[1] === 0x49 && bytes[2] === 0x46 && bytes[3] === 0x38) return "image/gif";
    if (bytes[0] === 0x52 && bytes[1] === 0x49 && bytes[2] === 0x46 && bytes[3] === 0x46 && bytes[8] === 0x57 && bytes[9] === 0x45 && bytes[10] === 0x42 && bytes[11] === 0x50) return "image/webp";
    if (bytes[0] === 0x42 && bytes[1] === 0x4d) return "image/bmp";
  }
  const extension = filename.split(".").pop()?.toLowerCase();
  const byExtension: Record<string, string> = { png: "image/png", jpg: "image/jpeg", jpeg: "image/jpeg", gif: "image/gif", webp: "image/webp", bmp: "image/bmp", mp4: "video/mp4", webm: "video/webm", ogv: "video/ogg" };
  return byExtension[extension ?? ""] ?? "application/octet-stream";
}

export function groupBubbleText(message: { kind?: string; body: string; deleted?: boolean }): string {
  if (message.deleted) return "Сообщение удалено";
  if (message.kind === "image") return "Фото";
  if (message.kind === "video") return "Видео";
  if (message.kind === "circle") return "Кружок";
  if (message.kind === "voice") return "Голосовое";
  if (message.kind === "file") return mediaFilename(message.body, "file");
  return message.body;
}

export async function postGroupMedia(id: string, kind: GroupMediaKind, name: string, file: Blob, replyTo: string | undefined, post: typeof fetch, headers: Record<string, string>, durationMs?: number): Promise<Response> {
  const prepared = groupMediaRequest(kind, name, file.size, replyTo, durationMs);
  return post(`/web-api/communities/conversations/${id}/media`, {
    method: "POST",
    credentials: "same-origin",
    headers: { ...headers, ...prepared.headers },
    body: file,
  });
}

export function groupMediaRequest(kind: string, name: string, bytes: number, replyTo?: string, durationMs?: number): { name: string; headers: Record<string, string> } {
  if (kind !== "image" && kind !== "video" && kind !== "file" && kind !== "voice" && kind !== "circle") throw new GroupMediaError("kind");
  if (!Number.isFinite(bytes) || bytes < 1 || bytes > (kind === "voice" ? groupVoiceLimit : groupMediaLimit)) throw new GroupMediaError("size");
  if ((kind === "voice" || kind === "circle") && durationMs === undefined) throw new GroupMediaError("duration");
  if (durationMs !== undefined && (kind !== "voice" && kind !== "circle" || !Number.isInteger(durationMs) || durationMs < 1 || durationMs > (kind === "voice" ? 180_000 : 60_000)))
    throw new GroupMediaError("duration");
  const base = name.trim().split(/[/\\]/).pop() ?? "";
  const clean = (base || (kind === "image" ? "Фото" : kind === "video" ? "Видео" : kind === "voice" ? "Голосовое" : kind === "circle" ? "Кружок" : "Документ")).slice(0, 80);
  const headers: Record<string, string> = {
    "Content-Type": "application/octet-stream",
    "X-Zapara-Kind": kind,
    "X-Zapara-Name": encodeURIComponent(clean),
  };
  if (replyTo) headers["X-Zapara-Reply"] = replyTo;
  if (durationMs !== undefined) headers["X-Zapara-Duration-Ms"] = String(durationMs);
  return { name: clean, headers };
}

export function recordingFilename(kind: "voice" | "circle", mime: string): string {
  const type = mime.split(";", 1)[0].toLowerCase();
  if (kind === "voice") {
    if (type === "audio/webm") return "voice.webm";
    if (type === "audio/ogg") return "voice.ogg";
    if (type === "audio/mp4") return "voice.m4a";
    if (type === "audio/mpeg") return "voice.mp3";
  } else {
    if (type === "video/webm") return "circle.webm";
    if (type === "video/mp4") return "circle.mp4";
  }
  throw new GroupMediaError("format");
}
