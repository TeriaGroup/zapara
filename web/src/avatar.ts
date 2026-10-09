export type AvatarKind = "user" | "group";

/** #17: у группы без загруженного фото — значок группы, а не буквы названия («И8», «А8» читались как код). У человека — инициалы. */
export function avatarFallback(kind: AvatarKind): "icon" | "initials" {
  return kind === "group" ? "icon" : "initials";
}

export function avatarInitials(name: string): string {
  const words = name.trim().split(/\s+/u).map(word => Array.from(word).filter(char => /[\p{L}\p{N}]/u.test(char))).filter(letters => letters.length > 0);
  if (!words.length) return "?";
  const letters = words.length > 1 ? `${words[0][0]}${words[words.length - 1][0]}` : words[0].slice(0, 2).join("");
  return letters.toLocaleUpperCase("ru-RU");
}

export function avatarPath(kind: AvatarKind, id: string): string {
  if (!id.trim()) throw new Error("avatar-id");
  return `/web-api/social/avatars/${kind === "user" ? "users" : "groups"}/${encodeURIComponent(id)}`;
}

export function validateAvatarFile(file: Pick<File, "type" | "size">): string | null {
  if (!["image/png", "image/jpeg", "image/webp"].includes(file.type)) return "Выберите изображение PNG, JPEG или WebP.";
  if (file.size <= 0) return "Файл пустой. Выберите другое изображение.";
  if (file.size > 20 * 1024 * 1024) return "Размер исходного изображения не должен превышать 20 МБ.";
  return null;
}

export function createAvatarCache(
  load: (key: string, etag: string | null) => Promise<string | null | { url?: string | null; etag?: string | null; notModified?: boolean }>,
  maxEntries = 96,
  revoke: (url: string) => void = URL.revokeObjectURL,
) {
  type Entry = { value: string | null; etag: string | null; pending: Promise<string | null> | null; refs: number; retired: boolean; revoked: boolean };
  const entries = new Map<string, Entry>();
  const listeners = new Set<(key?: string) => void>();
  let owner = "";
  let epoch = 0;
  let sweepTimer: ReturnType<typeof setTimeout> | null = null;

  function revokeEntry(entry: Entry) {
    if (entry.value && !entry.revoked) { revoke(entry.value); entry.revoked = true; }
  }

  function remove(key: string, force = false) {
    const entry = entries.get(key);
    if (!entry) return;
    entries.delete(key);
    entry.retired = true;
    if (force || entry.refs === 0) revokeEntry(entry);
  }

  function sweep() {
    while (entries.size > maxEntries) {
      const idle = [...entries].find(([, entry]) => entry.refs === 0 && !entry.pending)?.[0];
      if (!idle) break;
      remove(idle);
    }
  }

  function scheduleSweep() {
    if (sweepTimer !== null) return;
    sweepTimer = setTimeout(() => { sweepTimer = null; sweep(); }, 0);
  }

  function notify(key?: string) { for (const listener of listeners) listener(key); }
  function loaded(result: Awaited<ReturnType<typeof load>>) {
    return typeof result === "object" && result !== null
      ? { url: result.url ?? null, etag: result.etag ?? null, notModified: result.notModified === true }
      : { url: result, etag: null, notModified: false };
  }

  return {
    scope(next: string) {
      if (next === owner) return;
      owner = next;
      epoch++;
      for (const key of entries.keys()) remove(key, true);
      notify();
    },
    subscribe(listener: (key?: string) => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    invalidate(key: string) { remove(key); notify(key); },
    fail(key: string) { remove(key); entries.set(key, { value: null, etag: null, pending: null, refs: 0, retired: false, revoked: false }); notify(key); },
    retain(key: string, url: string | null): () => void {
      const entry = entries.get(key);
      if (!entry || !url || entry.value !== url) return () => undefined;
      entry.refs++;
      return () => {
        entry.refs = Math.max(0, entry.refs - 1);
        if (entry.retired && entry.refs === 0) revokeEntry(entry);
        else scheduleSweep();
      };
    },
    async refresh(key: string): Promise<string | null> {
      const entry = entries.get(key);
      if (!entry) return this.read(key);
      if (entry.pending) return entry.pending;
      const captured = epoch;
      const pending = load(key, entry.etag).then(result => {
        const next = loaded(result);
        if (captured !== epoch || entries.get(key) !== entry) {
          if (next.url && next.url !== entry.value) revoke(next.url);
          return null;
        }
        entry.pending = null;
        if (next.notModified) return entry.value;
        if (next.url === entry.value) { entry.etag = next.etag; return entry.value; }
        const replacement: Entry = { value: next.url, etag: next.etag, pending: null, refs: 0, retired: false, revoked: false };
        entries.set(key, replacement);
        entry.retired = true;
        if (entry.refs === 0) revokeEntry(entry);
        notify(key);
        scheduleSweep();
        return next.url;
      }).catch(() => { if (entries.get(key) === entry) entry.pending = null; return entry.value; });
      entry.pending = pending;
      return pending;
    },
    async refreshAll(): Promise<void> {
      await Promise.allSettled([...entries.keys()].map(key => this.refresh(key)));
    },
    async read(key: string): Promise<string | null> {
      const existing = entries.get(key);
      if (existing) {
        entries.delete(key);
        entries.set(key, existing);
        return existing.pending ?? existing.value;
      }
      const captured = epoch;
      const entry: Entry = { value: null, etag: null, pending: null, refs: 0, retired: false, revoked: false };
      entries.set(key, entry);
      const pending = load(key, null).then(result => {
        const next = loaded(result);
        const value = next.url;
        if (captured !== epoch || entries.get(key) !== entry) {
          if (value) revoke(value);
          return null;
        }
        entry.value = value;
        entry.etag = next.etag;
        entry.pending = null;
        scheduleSweep();
        return value;
      }).catch(() => {
        if (entries.get(key) === entry) { entry.pending = null; entry.value = null; }
        return null;
      });
      entry.pending = pending;
      return pending;
    },
  };
}
