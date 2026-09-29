import assert from "node:assert/strict";
import { test } from "node:test";
import * as api from "./api.ts";

test("avatar requests stay on the browser API and carry the account and group guards", async () => {
  const previous = globalThis.fetch;
  const calls: { url: string; init: RequestInit }[] = [];
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), init: init || {} });
    if (url === "/web-api/session") return Response.json({ authenticated: true, user: { userId: "self" }, csrfToken: "csrf", familyId: "family" });
    if (init?.method === "PUT") return Response.json({ revision: "revision" });
    return new Response(null, { status: 304 });
  };
  try {
    await api.session();
    await api.avatarImage("group", "group/one", '"old"');
    const image = calls[1];
    assert.equal(image.url, "/web-api/social/avatars/groups/group%2Fone");
    assert.equal(image.init.credentials, "same-origin");
    assert.equal(image.init.cache, "no-store");
    assert.equal((image.init.headers as Record<string, string>)["If-None-Match"], '"old"');
    assert.equal((image.init.headers as Record<string, string>)["X-Zapara-Group-Space"], "1");
    assert.equal((image.init.headers as Record<string, string>)["X-Zapara-Family"], "family");
    assert.equal((image.init.headers as Record<string, string>)["X-Zapara-CSRF"], "csrf");
    await api.saveAvatar("user", "self", new File([new Uint8Array([1])], "avatar.webp", { type: "image/webp" }), { userId: "self", familyId: "family" });
    const upload = calls[2];
    assert.equal(upload.url, "/web-api/social/avatars/me");
    assert.equal(upload.init.credentials, "same-origin");
    assert.equal(upload.init.method, "PUT");
    assert.equal((upload.init.body as FormData).getAll("file").length, 1);
    assert.equal((upload.init.headers as Record<string, string>)["Content-Type"], undefined);
  } finally { globalThis.fetch = previous; }
});

test("avatar mutation rejects a captured account after the browser session changes", async () => {
  const previous = globalThis.fetch;
  let calls = 0;
  globalThis.fetch = async (url) => {
    calls++;
    if (url === "/web-api/session") return Response.json({ authenticated: true, user: { userId: "other" }, csrfToken: "new", familyId: "new-family" });
    return Response.json({ revision: "unexpected" });
  };
  try {
    await api.session();
    const file = new File([new Uint8Array([1])], "avatar.webp", { type: "image/webp" });
    await assert.rejects(api.saveAvatar("user", "self", file, { userId: "self", familyId: "family" }), /avatar-scope-changed/);
    assert.equal(calls, 1);
  } finally { globalThis.fetch = previous; }
});

test("avatar image rejects an oversized stream before it is buffered", async () => {
  const previous = globalThis.fetch;
  let cancelled = false;
  globalThis.fetch = async () => new Response(new ReadableStream({
    start(controller) { controller.enqueue(new Uint8Array(512 * 1024 + 1)); },
    cancel() { cancelled = true; },
  }), { headers: { "Content-Type": "image/webp" } });
  try {
    await assert.rejects(api.avatarImage("user", "self"), /invalid-avatar-image/);
    assert.equal(cancelled, true);
  } finally { globalThis.fetch = previous; }
});

test("avatar image rejects declared oversize before reading its body", async () => {
  const previous = globalThis.fetch;
  let cancelled = false;
  globalThis.fetch = async () => new Response(new ReadableStream({
    cancel() { cancelled = true; },
  }), { headers: { "Content-Type": "image/webp", "Content-Length": String(512 * 1024 + 1) } });
  try {
    await assert.rejects(api.avatarImage("user", "self"), /invalid-avatar-image/);
    assert.equal(cancelled, true);
  } finally { globalThis.fetch = previous; }
});
