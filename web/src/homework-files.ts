const documents = new Set(["pdf", "txt", "csv", "rtf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp", "zip"]);
const photos = new Set(["jpg", "jpeg", "png", "webp", "gif"]);
export const HOMEWORK_FILE_LIMIT = 6;

export function cleanHomeworkName(raw: string): string {
  const base = raw.split(/[/\\]/).pop()?.trim() || "";
  const cleaned = [...base].filter(ch => ch >= " " && ch !== "\"" && ch !== "'").join("");
  if (!cleaned || cleaned.includes("..") || cleaned.length > 80) return "";
  return cleaned;
}

export function homeworkExtension(name: string): string {
  const dot = name.lastIndexOf(".");
  return dot < 0 ? "" : name.slice(dot + 1).toLowerCase();
}

export function checkHomeworkFile(kind: "photo" | "document", name: string, size: number): string {
  if (size <= 0) throw new Error("bad");
  const cleaned = cleanHomeworkName(name);
  if (!cleaned || [...cleaned].filter(ch => ch === ".").length !== 1) throw new Error("bad");
  const ext = homeworkExtension(cleaned);
  if (kind === "photo") {
    if (size > 12 * 1024 * 1024) throw new Error("big");
    if (!photos.has(ext)) throw new Error("bad");
  } else {
    if (size > 20 * 1024 * 1024) throw new Error("big");
    if (!documents.has(ext)) throw new Error("bad");
  }
  return cleaned;
}

export async function compressHomeworkPhoto(file: Blob): Promise<Blob> {
  const bitmap = await createImageBitmap(file);
  try {
    if (bitmap.width <= 0 || bitmap.height <= 0 || bitmap.width * bitmap.height > 24_000_000) throw new Error("bad");
    const scale = Math.min(1, 1600 / Math.max(bitmap.width, bitmap.height, 1));
    const canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(bitmap.width * scale));
    canvas.height = Math.max(1, Math.round(bitmap.height * scale));
    const context = canvas.getContext("2d");
    if (!context) throw new Error("bad");
    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, "image/jpeg", 0.7));
    if (!blob || blob.size > 4 * 1024 * 1024) throw new Error(blob ? "big" : "bad");
    return blob;
  } finally {
    bitmap.close();
  }
}

function database(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const open = indexedDB.open("zapara-homework-files", 1);
    open.onupgradeneeded = () => { if (!open.result.objectStoreNames.contains("files")) open.result.createObjectStore("files"); };
    open.onsuccess = () => resolve(open.result);
    open.onerror = () => reject(open.error);
  });
}

export async function putHomeworkBlob(id: string, blob: Blob): Promise<void> {
  const db = await database();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction("files", "readwrite");
    tx.objectStore("files").put(blob, id);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
  db.close();
}

export async function deleteHomeworkBlob(id: string): Promise<void> {
  const db = await database();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction("files", "readwrite");
    tx.objectStore("files").delete(id);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
  db.close();
}

export async function readHomeworkBlob(id: string): Promise<Blob | null> {
  const db = await database();
  const blob = await new Promise<Blob | null>((resolve, reject) => {
    const tx = db.transaction("files", "readonly");
    const request = tx.objectStore("files").get(id);
    request.onsuccess = () => resolve((request.result as Blob) || null);
    request.onerror = () => reject(request.error);
  });
  db.close();
  return blob;
}
