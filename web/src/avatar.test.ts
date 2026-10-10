import assert from "node:assert/strict";
import { test } from "node:test";
import { avatarInitials, avatarPath, createAvatarCache, validateAvatarFile } from "./avatar.ts";
import { readAvatarDimensions } from "./avatar-photo.ts";

test("avatar initials use two name parts or two letters of one name", () => {
  assert.equal(avatarInitials("  Анна   Петрова "), "АП");
  assert.equal(avatarInitials(" Анна Мария Петрова "), "АП");
  assert.equal(avatarInitials("🎓 Анна Петрова"), "АП");
  assert.equal(avatarInitials("Военмех"), "ВО");
  assert.equal(avatarInitials(""), "?");
  assert.equal(avatarInitials("🎓"), "?");
});

test("avatar paths encode only validated identifiers", () => {
  assert.equal(avatarPath("user", "abc-123"), "/web-api/social/avatars/users/abc-123");
  assert.equal(avatarPath("group", "a/b"), "/web-api/social/avatars/groups/a%2Fb");
  assert.throws(() => avatarPath("user", ""));
});

test("avatar upload accepts normal photos for local downsampling and rejects oversized originals", () => {
  assert.equal(validateAvatarFile({ type: "image/png", size: 3 * 1024 * 1024 }), null);
  assert.match(validateAvatarFile({ type: "image/gif", size: 100 }) || "", /PNG/);
  assert.match(validateAvatarFile({ type: "image/jpeg", size: 20 * 1024 * 1024 + 1 }) || "", /20 МБ/);
});

test("avatar photo dimensions are checked from file headers before decoding", () => {
  const png = new Uint8Array(24);
  png.set([137, 80, 78, 71, 13, 10, 26, 10]);
  new DataView(png.buffer).setUint32(16, 4000);
  new DataView(png.buffer).setUint32(20, 3000);
  assert.deepEqual(readAvatarDimensions(png, "image/png"), { width: 4000, height: 3000 });
  const jpeg = new Uint8Array([0xff, 0xd8, 0xff, 0xc0, 0x00, 0x07, 0x08, 0x0b, 0xb8, 0x0f, 0xa0]);
  assert.deepEqual(readAvatarDimensions(jpeg, "image/jpeg"), { width: 4000, height: 3000 });
  const webp = new Uint8Array(30);
  webp.set([..."RIFF"].map(letter => letter.charCodeAt(0)), 0);
  webp.set([..."WEBPVP8X"].map(letter => letter.charCodeAt(0)), 8);
  webp[24] = 0x9f; webp[25] = 0x0f; webp[27] = 0xb7; webp[28] = 0x0b;
  assert.deepEqual(readAvatarDimensions(webp, "image/webp"), { width: 4000, height: 3000 });
  assert.equal(readAvatarDimensions(new Uint8Array(24), "image/png"), null);
});

test("cache deduplicates reads and clears images on account change", async () => {
  let reads = 0;
  const revoked: string[] = [];
  const cache = createAvatarCache(async () => { reads++; return "blob:avatar-" + reads; }, 2, value => revoked.push(value));
  cache.scope("account-a:family-1");
  const [first, second] = await Promise.all([cache.read("user:a"), cache.read("user:a")]);
  assert.equal(first, second);
  assert.equal(reads, 1);
  cache.scope("account-b:family-2");
  assert.deepEqual(revoked, ["blob:avatar-1"]);
  assert.equal(await cache.read("user:a"), "blob:avatar-2");
});

test("cache discards stale responses after account change", async () => {
  let resolve!: (url: string) => void;
  const revoked: string[] = [];
  const cache = createAvatarCache(() => new Promise<string>(done => { resolve = done; }), 2, value => revoked.push(value));
  cache.scope("first");
  const pending = cache.read("user:a");
  cache.scope("second");
  resolve("blob:old");
  assert.equal(await pending, null);
  assert.deepEqual(revoked, ["blob:old"]);
});

test("an avatar in use survives refresh until its image unmounts", async () => {
  const revoked: string[] = [];
  const cache = createAvatarCache(async () => "blob:one", 2, value => revoked.push(value));
  cache.scope("account");
  const url = await cache.read("user:a");
  const release = cache.retain("user:a", url);
  cache.invalidate("user:a");
  assert.deepEqual(revoked, []);
  release();
  assert.deepEqual(revoked, ["blob:one"]);
});

test("idle avatar cache stays bounded", async () => {
  let reads = 0;
  const revoked: string[] = [];
  const cache = createAvatarCache(async () => `blob:${++reads}`, 2, value => revoked.push(value));
  cache.scope("account");
  await cache.read("user:a");
  await cache.read("user:b");
  await cache.read("user:c");
  await new Promise(resolve => setTimeout(resolve, 5));
  assert.deepEqual(revoked, ["blob:1"]);
  await cache.read("user:a");
  assert.equal(reads, 4);
});

test("conditional refresh reuses an unchanged image and swaps a newer one", async () => {
  const seen: (string | null)[] = [];
  const revoked: string[] = [];
  let revision = 1;
  const cache = createAvatarCache(async (_key, etag) => {
    seen.push(etag);
    if (etag === `revision-${revision}`) return { notModified: true };
    return { url: `blob:${revision}`, etag: `revision-${revision}` };
  }, 2, value => revoked.push(value));
  cache.scope("account");
  assert.equal(await cache.read("user:a"), "blob:1");
  const release = cache.retain("user:a", "blob:1");
  assert.equal(await cache.refresh("user:a"), "blob:1");
  revision = 2;
  assert.equal(await cache.refresh("user:a"), "blob:2");
  assert.deepEqual(seen, [null, "revision-1", "revision-1"]);
  assert.deepEqual(revoked, []);
  release();
  assert.deepEqual(revoked, ["blob:1"]);
});

test("a missing avatar is not requested again by periodic refresh until the pause ends (#34)", async () => {
  let clock = 0, reads = 0;
  let answer: { url: string | null; etag: string | null; missing?: boolean } = { url: null, etag: null, missing: true };
  const cache = createAvatarCache(async () => { reads++; return answer; }, 2, () => {}, 10 * 60_000, () => clock);
  cache.scope("account");
  assert.equal(await cache.read("user:a"), null);
  assert.equal(reads, 1);
  await cache.refreshAll();
  clock += 60_000; await cache.refreshAll();
  assert.equal(reads, 1, "the 60 s timer and tab switches do not repeat the 404");

  // Even after LRU eviction the answer is remembered.
  await cache.read("user:b"); await cache.read("user:c"); await new Promise(done => setTimeout(done, 0));
  const before = reads;
  assert.equal(await cache.read("user:a"), null);
  assert.equal(reads, before);

  clock += 10 * 60_000;
  answer = { url: "blob:new", etag: "1" };
  assert.equal(await cache.read("user:a"), "blob:new", "after the pause the avatar is checked again");
});

test("own upload and account change clear the remembered missing avatar (#34)", async () => {
  let reads = 0;
  const cache = createAvatarCache(async () => { reads++; return reads === 1 ? { url: null, etag: null, missing: true } : { url: "blob:mine", etag: "2" }; }, 4, () => {}, 10 * 60_000, () => 0);
  cache.scope("account");
  assert.equal(await cache.read("user:me"), null);
  cache.invalidate("user:me");
  assert.equal(await cache.read("user:me"), "blob:mine");

  let other = 0;
  const scoped = createAvatarCache(async () => { other++; return { url: null, etag: null, missing: true }; }, 4, () => {}, 10 * 60_000, () => 0);
  scoped.scope("a"); await scoped.read("user:x");
  scoped.scope("b"); await scoped.read("user:x");
  assert.equal(other, 2, "another account asks again");
});

test("network errors and 401/403 are not remembered as missing (#34)", async () => {
  let reads = 0;
  const cache = createAvatarCache(async () => { reads++; if (reads === 1) throw new Error("offline"); return { url: null, etag: null }; }, 4, () => {}, 10 * 60_000, () => 0);
  cache.scope("account");
  await cache.read("user:a");
  await cache.refresh("user:a");
  await cache.refresh("user:a");
  assert.equal(reads, 3);
});
