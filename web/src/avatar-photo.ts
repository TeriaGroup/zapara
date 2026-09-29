import { validateAvatarFile } from "./avatar.ts";

export function readAvatarDimensions(data: Uint8Array, type: string): { width: number; height: number } | null {
  if (type === "image/png") {
    if (data.length < 24 || ![137, 80, 78, 71, 13, 10, 26, 10].every((value, index) => data[index] === value)) return null;
    const view = new DataView(data.buffer, data.byteOffset, data.byteLength);
    return { width: view.getUint32(16), height: view.getUint32(20) };
  }
  if (type === "image/jpeg") {
    if (data.length < 4 || data[0] !== 0xff || data[1] !== 0xd8) return null;
    let at = 2;
    while (at + 8 < data.length) {
      if (data[at] !== 0xff) return null;
      while (data[at] === 0xff) at++;
      const marker = data[at++];
      if (marker === 0xda || marker === 0xd9) return null;
      if (at + 1 >= data.length) return null;
      const length = (data[at] << 8) | data[at + 1];
      if (length < 2 || at + length > data.length) return null;
      if ([0xc0, 0xc1, 0xc2, 0xc3, 0xc5, 0xc6, 0xc7, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf].includes(marker)) {
        if (length < 7) return null;
        return { width: (data[at + 5] << 8) | data[at + 6], height: (data[at + 3] << 8) | data[at + 4] };
      }
      at += length;
    }
    return null;
  }
  if (type === "image/webp") {
    const ascii = (at: number, text: string) => [...text].every((letter, index) => data[at + index] === letter.charCodeAt(0));
    if (data.length < 30 || !ascii(0, "RIFF") || !ascii(8, "WEBP")) return null;
    if (ascii(12, "VP8X")) return {
      width: 1 + data[24] + (data[25] << 8) + (data[26] << 16),
      height: 1 + data[27] + (data[28] << 8) + (data[29] << 16),
    };
    if (ascii(12, "VP8L") && data[20] === 0x2f) return {
      width: 1 + (((data[22] & 0x3f) << 8) | data[21]),
      height: 1 + (((data[24] & 0x0f) << 10) | (data[23] << 2) | (data[22] >> 6)),
    };
    if (ascii(12, "VP8 ") && data[23] === 0x9d && data[24] === 0x01 && data[25] === 0x2a) return {
      width: (data[26] | (data[27] << 8)) & 0x3fff,
      height: (data[28] | (data[29] << 8)) & 0x3fff,
    };
  }
  return null;
}

function webp(canvas: HTMLCanvasElement, quality: number): Promise<Blob> {
  return new Promise((resolve, reject) => canvas.toBlob(blob => {
    if (!blob || blob.type !== "image/webp") reject(new Error("webp-unavailable"));
    else resolve(blob);
  }, "image/webp", quality));
}

export async function prepareAvatarFile(file: File): Promise<File> {
  const problem = validateAvatarFile(file);
  if (problem) throw new Error(problem);
  const header = new Uint8Array(await file.slice(0, 2 * 1024 * 1024).arrayBuffer());
  const dimensions = readAvatarDimensions(header, file.type);
  if (!dimensions?.width || !dimensions.height) throw new Error("Не удалось прочитать размеры фото. Выберите другое изображение.");
  if (dimensions.width * dimensions.height > 48_000_000) throw new Error("Разрешение фото не должно превышать 48 мегапикселей.");
  if (typeof createImageBitmap !== "function") throw new Error("Этот браузер не поддерживает подготовку фото.");
  const bitmap = await createImageBitmap(file, { imageOrientation: "from-image" });
  try {
    if (bitmap.width * bitmap.height > 48_000_000) throw new Error("Разрешение фото не должно превышать 48 мегапикселей.");
    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = 512;
    const context = canvas.getContext("2d");
    if (!context) throw new Error("Браузер не смог подготовить фото.");
    const edge = Math.min(bitmap.width, bitmap.height);
    context.drawImage(bitmap, (bitmap.width - edge) / 2, (bitmap.height - edge) / 2, edge, edge, 0, 0, 512, 512);
    for (const quality of [0.82, 0.68, 0.5, 0.35]) {
      const blob = await webp(canvas, quality);
      if (blob.size <= 512 * 1024) return new File([blob], "avatar.webp", { type: "image/webp" });
    }
    throw new Error("Фото не удалось сжать до нужного размера. Выберите другое изображение.");
  } finally { bitmap.close(); }
}
