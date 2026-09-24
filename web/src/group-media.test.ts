import assert from "node:assert/strict";
import { test } from "node:test";
import { getGroupMedia, groupBubbleText, groupMediaDownload, GroupMediaError, postGroupMedia } from "./group-media.ts";
import { holdActions } from "./hold.ts";

test("group media posts the file bytes and a photo hold has no edit", async () => {
  assert.equal(groupBubbleText({ kind: "image", body: "снимок.png" }), "Фото");
  assert.equal(groupBubbleText({ kind: "video", body: "ролик.mp4" }), "Видео");
  assert.equal(groupBubbleText({ kind: "file", body: "notes.txt" }), "notes.txt");
  assert.equal(groupBubbleText({ kind: "text", body: "привет" }), "привет");
  assert.equal(groupBubbleText({ kind: "image", body: "снимок.png", deleted: true }), "Сообщение удалено");
  assert.deepEqual(holdActions("image", true, false, true), ["reply", "reaction", "delete"]);
  assert.deepEqual(holdActions("video", true, false, true), ["reply", "reaction", "delete"]);
  assert.deepEqual(holdActions("file", true, false, true), ["reply", "reaction", "delete"]);
  assert.deepEqual(holdActions("text", true, false, true), ["reply", "reaction", "edit", "delete"]);
  assert.deepEqual(holdActions("image", true, false, false), []);
  const photo = Uint8Array.from([1, 2, 3, 4]);
  const seen: { url: string; type: string; kind: string; name: string; reply: string; bytes: Uint8Array }[] = [];
  const post: typeof fetch = async (url, init) => {
    const headers = new Headers(init?.headers);
    const body = init?.body;
    const bytes = body instanceof Blob ? new Uint8Array(await body.arrayBuffer()) : new Uint8Array();
    seen.push({
      url: String(url),
      type: headers.get("Content-Type") ?? "",
      kind: headers.get("X-Zapara-Kind") ?? "",
      name: headers.get("X-Zapara-Name") ?? "",
      reply: headers.get("X-Zapara-Reply") ?? "",
      bytes,
    });
    const kind = headers.get("X-Zapara-Kind");
    return new Response(JSON.stringify({
      messageId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      conversationId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      senderId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      senderName: "Аня",
      body: kind === "file" ? "notes.txt" : kind === "video" ? "ролик.mp4" : "снимок.png",
      createdAt: "2026-09-23T12:00:00Z",
      kind,
      deleted: false,
    }), { status: 201, headers: { "Content-Type": "application/json" } });
  };
  const image = await (await postGroupMedia("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "image", "снимки/снимок.png", new Blob([photo]), "dddddddd-dddd-4ddd-8ddd-dddddddddddd", post, { Accept: "application/json" })).json() as { kind: string };
  assert.equal(image.kind, "image");
  const clip = Uint8Array.from([9, 8, 7]);
  await postGroupMedia("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "video", "ролик.mp4", new Blob([clip]), undefined, post, {});
  const notes = new TextEncoder().encode("конспект");
  await postGroupMedia("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "file", "notes.txt", new Blob([notes]), undefined, post, {});
  await assert.rejects(() => postGroupMedia("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "image", "big.png", new Blob([new Uint8Array(8 * 1024 * 1024 + 1)]), undefined, post, {}), (error: unknown) => error instanceof GroupMediaError && error.code === "size");
  assert.equal(seen.length, 3);
  assert.equal(seen[0].url, "/web-api/communities/conversations/bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb/media");
  assert.equal(seen[0].type, "application/octet-stream");
  assert.deepEqual(seen.map(item => item.kind), ["image", "video", "file"]);
  assert.equal(seen[0].name, encodeURIComponent("снимок.png"));
  assert.equal(seen[0].reply, "dddddddd-dddd-4ddd-8ddd-dddddddddddd");
  assert.deepEqual(seen[0].bytes, photo);
  assert.deepEqual(seen[1].bytes, Uint8Array.from([9, 8, 7]));
  assert.deepEqual(seen[2].bytes, new TextEncoder().encode("конспект"));
});

test("group media offers a same-origin download using IDs, not a supplied path", () => {
  const conversationId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
  const messageId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
  const file = groupMediaDownload(conversationId, { messageId, kind: "file", body: "C:\\secret\\notes<1>.pdf" });
  assert.deepEqual(file, {
    href: `/web-api/communities/conversations/${conversationId}/messages/${messageId}/media`,
    filename: "notes1.pdf",
    label: "Скачать документ: notes1.pdf",
  });
  assert.equal(groupBubbleText({ kind: "file", body: "../../report.pdf" }), "report.pdf");
  assert.equal(groupMediaDownload(conversationId, { messageId, kind: "image", body: "photos/shot.png" })?.label, "Скачать фото");
  assert.equal(groupMediaDownload(conversationId, { messageId, kind: "video", body: "clip.mp4" })?.filename, "clip.mp4");
  assert.equal(groupMediaDownload(conversationId, { messageId, kind: "file", body: "folder/CON.txt" })?.filename, "_CON.txt");
  assert.equal(groupMediaDownload(conversationId, { messageId, kind: "file", body: "folder/\u202ephoto.exe" })?.filename, "photo.exe");
});

test("deleted, non-media, and malformed-ID messages have no download link", () => {
  const conversationId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
  const messageId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
  assert.equal(groupMediaDownload(conversationId, { messageId, kind: "image", body: "photo.png", deleted: true }), null);
  assert.equal(groupMediaDownload(conversationId, { messageId, kind: "text", body: "hello" }), null);
  assert.equal(groupMediaDownload("../outside", { messageId, kind: "file", body: "report.pdf" }), null);
  assert.equal(groupMediaDownload(conversationId, { messageId: "../outside", kind: "file", body: "report.pdf" }), null);
});

test("media download fetches bytes with browser session headers and fails closed on HTTP error", async () => {
  const download = groupMediaDownload("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", {
    messageId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", kind: "file", body: "report.pdf",
  });
  assert.ok(download);
  const seen: { url: string; credentials: RequestCredentials | undefined; family: string | null }[] = [];
  const get: typeof fetch = async (url, init) => {
    seen.push({ url: String(url), credentials: init?.credentials, family: new Headers(init?.headers).get("X-Zapara-Family") });
    return new Response(Uint8Array.from([1, 2, 3]), { status: 200 });
  };
  const blob = await getGroupMedia(download, get, { "X-Zapara-Family": "session-family" });
  assert.deepEqual(new Uint8Array(await blob.arrayBuffer()), Uint8Array.from([1, 2, 3]));
  assert.deepEqual(seen, [{ url: download.href, credentials: "same-origin", family: "session-family" }]);
  await assert.rejects(() => getGroupMedia(download, async () => new Response("no", { status: 409 }), {}), /409/);
});
