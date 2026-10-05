import assert from "node:assert/strict";
import { test } from "node:test";
import { createSessionRefresher } from "./session-refresh.ts";
import * as api from "./api.ts";

test("session refresh ignores an older response after authentication changes", async () => {
  let generation = 0;
  let releaseOld!: (value: string) => void;
  const old = new Promise<string>(resolve => { releaseOld = resolve; });
  const applied: string[] = [];
  let calls = 0;
  const refresh = createSessionRefresher(() => generation, async () => ++calls === 1 ? old : "new", value => applied.push(value), () => {});
  const first = refresh();
  generation++;
  await refresh();
  releaseOld("old");
  await first;
  assert.deepEqual(applied, ["new"]);
  assert.equal(calls, 2);
});

test("session refresh shares a request within one auth generation", async () => {
  let release!: (value: string) => void;
  let calls = 0;
  const refresh = createSessionRefresher(() => 1, () => { calls++; return new Promise<string>(resolve => { release = resolve; }); }, () => {}, () => {});
  const first = refresh();
  const second = refresh();
  assert.equal(calls, 1);
  release("session");
  await Promise.all([first, second]);
});

test("old session response cannot restore stale API identity after login", async () => {
  const original = globalThis.fetch;
  let releaseOld!: (response: Response) => void;
  let sessions = 0;
  let avatarFamily = "";
  const makeSession = (userId: string, familyId: string) => ({ authenticated: true, user: { userId, username: userId, displayName: null }, csrfToken: familyId,
    familyId, capabilities: { registration: true } });
  globalThis.fetch = async (url, init) => {
    if (url === "/web-api/session") return ++sessions === 1
      ? new Promise<Response>(resolve => { releaseOld = resolve; })
      : Response.json(makeSession("new", "new-family"));
    if (url === "/web-api/auth/login") return Response.json(makeSession("new", "new-family"));
    if (url === "/web-api/social/avatars/me") { avatarFamily = (init?.headers as Record<string, string>)["X-Zapara-Family"]; return Response.json({ revision: "ok" }); }
    throw new Error(String(url));
  };
  try {
    const first = api.session();
    await api.login("new", "password");
    await api.session();
    releaseOld(Response.json(makeSession("old", "old-family")));
    await assert.rejects(first, /stale-session/);
    await api.saveAvatar("user", "new", new File(["x"], "avatar.webp", { type: "image/webp" }), { userId: "new", familyId: "new-family" });
    assert.equal(avatarFamily, "new-family");
  } finally { globalThis.fetch = original; }
});
