import assert from "node:assert/strict";
import { test } from "node:test";
import { canCopyMessageText, filterMessages, messageDayKey, sameMessageCluster } from "./messageBrowse.ts";
import type { ChatMessage } from "./types.ts";

const rows: ChatMessage[] = [
  { messageId: "one", conversationId: "chat", senderId: "me", senderName: "Я", body: "  ВОЕНМЕХ  ", createdAt: "2026-09-25T08:00:00Z", kind: "text" },
  { messageId: "two", conversationId: "chat", senderId: "other", senderName: "Аня", body: "photo.jpg", createdAt: "2026-09-25T08:01:00Z", kind: "image" },
  { messageId: "three", conversationId: "chat", senderId: "other", senderName: "Аня", body: "note.pdf", createdAt: "2026-09-25T08:02:00Z", kind: "file" },
  { messageId: "four", conversationId: "chat", senderId: "me", senderName: "Я", body: "record.webm", createdAt: "2026-09-25T08:03:00Z", kind: "voice" },
  { messageId: "five", conversationId: "chat", senderId: "other", senderName: "Аня", body: "Сообщение удалено", createdAt: "2026-09-25T08:04:00Z", kind: "text", deleted: true },
];

const all = { query: "", author: "all" as const, kind: "all" as const };

test("message browse searches only supplied loaded bodies, ignoring case and surrounding spaces", () => {
  assert.deepEqual(filterMessages(rows, { ...all, query: " военмех " }, "me").map(row => row.messageId), ["one"]);
  assert.deepEqual(filterMessages(rows, { ...all, query: "сообщение удалено" }, "me"), []);
  assert.deepEqual(filterMessages([], all, "me"), []);
  assert.deepEqual(rows.map(row => row.messageId), ["one", "two", "three", "four", "five"]);
});

test("author filter uses sender ID and media groups use kind without loading attachments", () => {
  assert.deepEqual(filterMessages(rows, { ...all, author: "mine" }, "me").map(row => row.messageId), ["one", "four"]);
  assert.deepEqual(filterMessages(rows, { ...all, author: "others" }, "me").map(row => row.messageId), ["two", "three"]);
  assert.deepEqual(filterMessages(rows, { ...all, author: "others", kind: "visual" }, "me").map(row => row.messageId), ["two"]);
  assert.deepEqual(filterMessages(rows, { ...all, kind: "file" }, "me").map(row => row.messageId), ["three"]);
  assert.deepEqual(filterMessages(rows, { ...all, kind: "audio" }, "me").map(row => row.messageId), ["four"]);
  assert.deepEqual(filterMessages(rows, all, "me").map(row => row.messageId), ["one", "two", "three", "four", "five"]);
});

test("only nondeleted text can be copied", () => {
  assert.equal(canCopyMessageText(rows[0]), true);
  assert.equal(canCopyMessageText(rows[1]), false);
  assert.equal(canCopyMessageText(rows[4]), false);
  assert.equal(canCopyMessageText({ ...rows[0], kind: undefined }), true);
  assert.equal(canCopyMessageText({ ...rows[0], body: "   " }), false);
});

test("day separators and author clusters use local calendar day and a short time gap", () => {
  const first = rows[1];
  assert.equal(messageDayKey(first.createdAt), messageDayKey(rows[2].createdAt));
  assert.equal(sameMessageCluster(first, rows[2]), true);
  assert.equal(sameMessageCluster(rows[2], rows[3]), false);
  assert.equal(sameMessageCluster(first, { ...rows[2], createdAt: "2026-09-26T08:02:00Z" }), false);
  assert.equal(sameMessageCluster(first, { ...rows[2], createdAt: "2026-09-25T13:02:00Z" }), false);
});
