import { useEffect, useRef, useState } from "react";
import * as api from "./api";
import { avatarInitials, createAvatarCache, validateAvatarFile, type AvatarKind } from "./avatar";
import { prepareAvatarFile } from "./avatar-photo";
import { useApp } from "./store";

const cache = createAvatarCache((key, etag) => {
  const [kind, ...parts] = key.split(":");
  return api.avatarImage(kind as AvatarKind, parts.join(":"), etag);
});
let watchers = 0;
let refreshTimer: number | null = null;
function refreshVisible() { if (!document.hidden) void cache.refreshAll(); }
function watchAvatar(key: string, onChange: () => void): () => void {
  const unsubscribe = cache.subscribe(changed => { if (!changed || changed === key) onChange(); });
  watchers++;
  if (watchers === 1) {
    refreshTimer = window.setInterval(refreshVisible, 60_000);
    document.addEventListener("visibilitychange", refreshVisible);
  }
  return () => {
    unsubscribe();
    watchers--;
    if (watchers === 0) {
      if (refreshTimer !== null) window.clearInterval(refreshTimer);
      refreshTimer = null;
      document.removeEventListener("visibilitychange", refreshVisible);
    }
  };
}

function scopeOf(session: ReturnType<typeof useApp>["session"]): string {
  return session?.authenticated && session.user ? `${session.user.userId}:${session.familyId || ""}` : "";
}

export function Avatar({ kind, id, name, className = "", onPresence }: {
  kind: AvatarKind; id: string | null | undefined; name: string; className?: string;
  onPresence?: (present: boolean) => void;
}) {
  const app = useApp();
  const scope = scopeOf(app.session);
  const key = id ? `${kind}:${id}` : "";
  const [revision, setRevision] = useState(0);
  const [image, setImage] = useState<{ scope: string; key: string; url: string | null }>({ scope: "", key: "", url: null });

  useEffect(() => {
    cache.scope(scope);
    if (!scope || !key) { setImage({ scope, key, url: null }); onPresence?.(false); return; }
    let alive = true;
    let release: () => void = () => undefined;
    setImage({ scope, key, url: null });
    void cache.read(key).then(url => {
      if (alive) { release = cache.retain(key, url); setImage({ scope, key, url }); onPresence?.(!!url); }
    });
    return () => { alive = false; release(); };
  }, [scope, key, revision]);

  useEffect(() => watchAvatar(key, () => setRevision(value => value + 1)), [key]);

  const url = image.scope === scope && image.key === key ? image.url : null;
  return <span className={`avatar ${className}`} role="img" aria-label={`Аватар: ${name}`}>
    {url ? <img src={url} alt="" onError={() => { cache.fail(key); onPresence?.(false); }} /> : <span aria-hidden="true">{avatarInitials(name)}</span>}
  </span>;
}

function editError(error: unknown, action: "save" | "delete"): string {
  const status = error instanceof Error ? error.message : "";
  if (status === "404") return "Фото пока недоступно на сервере. Попробуйте позже.";
  if (status === "403") return "Недостаточно прав для изменения фото.";
  if (status === "413") return "Файл слишком большой для сервера. Выберите другое изображение.";
  if (status === "415" || status === "400") return "Формат изображения не подошёл. Выберите PNG, JPEG или WebP.";
  if (status === "avatar-scope-changed") return "Аккаунт изменился. Выберите фото ещё раз.";
  if (/^[А-ЯЁа-яё]/u.test(status)) return status;
  return action === "save" ? "Не удалось подтвердить обновление фото. Попробуйте обновить страницу." : "Фото не удалилось. Попробуйте ещё раз.";
}

export function AvatarEditor({ kind, id, name }: { kind: AvatarKind; id: string; name: string }) {
  const app = useApp();
  const owner = app.session?.authenticated && app.session.user && app.session.familyId
    ? { userId: app.session.user.userId, familyId: app.session.familyId } : null;
  const marker = JSON.stringify([kind, id, owner?.userId, owner?.familyId]);
  const [busy, setBusy] = useState(false);
  const [present, setPresent] = useState(false);
  const [notice, setNotice] = useState("");
  const input = useRef<HTMLInputElement>(null);
  const busyRef = useRef(false);
  const operation = useRef(0);
  const mounted = useRef(false);
  const currentMarker = useRef(marker);
  currentMarker.current = marker;
  const key = `${kind}:${id}`;

  useEffect(() => {
    mounted.current = true;
    busyRef.current = false;
    setBusy(false);
    setNotice("");
    setPresent(false);
    return () => { mounted.current = false; operation.current++; };
  }, [marker]);

  function current(ticket: number, started: string) {
    return mounted.current && operation.current === ticket && currentMarker.current === started;
  }

  async function select(file: File | undefined) {
    if (!file || busyRef.current) return;
    if (!owner) { setNotice("Аккаунт обновляется. Повторите выбор фото."); return; }
    const problem = validateAvatarFile(file);
    if (problem) { setNotice(problem); if (input.current) input.current.value = ""; return; }
    const started = marker;
    const ticket = ++operation.current;
    busyRef.current = true;
    setBusy(true); setNotice("Подготавливаем фото…");
    try {
      const ready = await prepareAvatarFile(file);
      if (!current(ticket, started)) return;
      setNotice("Загружаем фото…");
      await api.saveAvatar(kind, id, ready, owner);
      if (!current(ticket, started)) return;
      cache.invalidate(key);
      setNotice("Фото обновлено.");
    } catch (error) { if (current(ticket, started)) setNotice(editError(error, "save")); }
    finally { if (current(ticket, started)) { busyRef.current = false; setBusy(false); if (input.current) input.current.value = ""; } }
  }

  async function remove() {
    if (busyRef.current) return;
    if (!owner) { setNotice("Аккаунт обновляется. Повторите удаление фото."); return; }
    const started = marker;
    const ticket = ++operation.current;
    busyRef.current = true;
    setBusy(true); setNotice("Удаляем фото…");
    try {
      await api.deleteAvatar(kind, id, owner);
      if (!current(ticket, started)) return;
      cache.invalidate(key);
      setPresent(false);
      setNotice("Фото удалено.");
    } catch (error) { if (current(ticket, started)) setNotice(editError(error, "delete")); }
    finally { if (current(ticket, started)) { busyRef.current = false; setBusy(false); } }
  }

  return <div className="avatar-editor stack">
    <div className="row"><Avatar kind={kind} id={id} name={name} className="avatar-large" onPresence={setPresent} />
      <div><b>{name}</b><p className="muted">Фото профиля {kind === "group" ? "группы" : "аккаунта"}. PNG, JPEG или WebP, до 20 МБ.</p></div></div>
    <div className="row"><label className="btn avatar-upload">{busy ? "Подождите…" : "Выбрать фото"}
      <input ref={input} type="file" accept="image/png,image/jpeg,image/webp" disabled={busy} onChange={event => void select(event.target.files?.[0])} />
    </label>{present && <button className="btn quiet" type="button" disabled={busy} onClick={() => void remove()}>Удалить фото</button>}</div>
    {notice && <p role="status">{notice}</p>}
  </div>;
}
