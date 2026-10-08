const maxFiles = 3;
const maxBytes = 512 * 1024;
const lines: string[] = [];
let stored = 0;

function encodedLength(text: string) {
  return new TextEncoder().encode(text).length;
}

export function resetAppLog() {
  lines.length = 0;
  stored = 0;
}

export function rememberLog(line: string) {
  const clean = line.replaceAll("\0", "");
  if (!clean) return;
  const row = clean.endsWith("\n") ? clean : clean + "\n";
  lines.push(row);
  stored += encodedLength(row);
  const budget = maxFiles * maxBytes;
  while (lines.length > 1 && stored > budget) {
    stored -= encodedLength(lines.shift()!);
  }
}

export function packSupportLog(text: string): { name: string; bytes: Uint8Array }[] {
  const clean = text.replaceAll("\0", "");
  if (!clean) return [];
  return split(new TextEncoder().encode(clean)).map((bytes, index) => ({ name: `web-${index + 1}.log`, bytes }));
}

export function supportLogFiles(): File[] {
  return packSupportLog(lines.join("")).map(part => {
    const copy = new ArrayBuffer(part.bytes.byteLength);
    new Uint8Array(copy).set(part.bytes);
    return new File([copy], part.name, { type: "text/plain" });
  });
}

function shown(value: unknown) {
  if (typeof value === "string") return value.replaceAll("\0", "");
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  if (value instanceof Error) return (value.stack || `${value.name}: ${value.message}`).replaceAll("\0", "");
  return "";
}

export function installAppLog() {
  const host = globalThis as typeof globalThis & { __zaparaAppLog?: boolean };
  if (host.__zaparaAppLog || typeof window === "undefined") return;
  host.__zaparaAppLog = true;
  const stamp = () => new Date().toISOString();
  for (const level of ["debug", "log", "info", "warn", "error"] as const) {
    const write = console[level].bind(console);
    console[level] = (...args: unknown[]) => {
      const text = args.map(shown).filter(Boolean).join(" ");
      if (text) rememberLog(`${stamp()} ${level} ${text}`);
      write(...args);
    };
  }
  window.addEventListener("error", event => {
    if (event.message) rememberLog(`${stamp()} error ${event.message}`);
  });
  window.addEventListener("unhandledrejection", event => {
    const text = shown(event.reason);
    if (text) rememberLog(`${stamp()} rejection ${text}`);
  });
}

function split(bytes: Uint8Array) {
  if (bytes.length === 0) return [];
  const budget = maxFiles * maxBytes;
  let start = bytes.length > budget ? bytes.length - budget : 0;
  while (start < bytes.length && (bytes[start] & 0xC0) === 0x80) start++;
  if (start >= bytes.length) return [];
  if (start > 0) {
    let newline = start;
    const windowEnd = Math.min(bytes.length, start + 4096);
    while (newline < windowEnd && bytes[newline] !== 10) newline++;
    if (newline < bytes.length && bytes[newline] === 10 && newline + 1 < bytes.length) start = newline + 1;
  }
  const newestFirst: Uint8Array[] = [];
  let end = bytes.length;
  while (end > start && newestFirst.length < maxFiles) {
    let chunkStart = Math.max(start, end - maxBytes);
    if (chunkStart > start) {
      while (chunkStart < end && (bytes[chunkStart] & 0xC0) === 0x80) chunkStart++;
      let newline = chunkStart;
      const windowEnd = Math.min(end, chunkStart + 4096);
      while (newline < windowEnd && bytes[newline] !== 10) newline++;
      if (newline < end && bytes[newline] === 10 && newline + 1 < end) chunkStart = newline + 1;
    }
    if (chunkStart >= end) break;
    newestFirst.push(bytes.slice(chunkStart, end));
    end = chunkStart;
  }
  newestFirst.reverse();
  return newestFirst;
}
