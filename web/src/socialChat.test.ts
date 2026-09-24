import assert from "node:assert/strict";
import { test } from "node:test";
import { createSocialPoller, loadSocialUpdates, mergeSocialMessages } from "./socialChat.ts";
import type { SocialMessage, SocialPage } from "./types.ts";

function message(id: string, number: number, body = id): SocialMessage {
  return {
    messageId: id, senderId: "sender", senderName: "Друг", kind: "text", body,
    attachmentId: null, fileName: null, contentType: null, bytes: null,
    createdAt: `2026-09-24T12:${String(number).padStart(2, "0")}:00Z`,
    replyTo: null, replyBody: null, editedAt: null, deleted: false, read: false,
    durationMs: null, reactions: [],
  };
}

test("a latest page updates known messages without dropping previously loaded history", () => {
  const known = [message("one", 1), message("two", 2)];
  const latest = [message("two", 2, "edited"), message("three", 3)];
  const result = mergeSocialMessages(known, latest);
  assert.deepEqual(result.map(item => item.messageId), ["one", "two", "three"]);
  assert.equal(result[1].body, "edited");
});

test("messages with the same server timestamp keep page order for the older cursor", () => {
  const earlier = message("z-older", 1);
  const later = message("a-later", 1);
  assert.deepEqual(mergeSocialMessages([earlier], [later]).map(item => item.messageId),
    ["z-older", "a-later"]);
  assert.deepEqual(mergeSocialMessages([earlier], [later, message("b-last", 1)]).map(item => item.messageId),
    ["z-older", "a-later", "b-last"]);
});

test("a poll walks older pages until it reconnects with known messages", async () => {
  const known = [message("one", 1), message("two", 2)];
  const calls: (string | undefined)[] = [];
  const pages = new Map<string | undefined, SocialPage>([
    [undefined, { messages: [message("five", 5), message("six", 6)], hasMore: true }],
    ["five", { messages: [message("three", 3), message("four", 4)], hasMore: true }],
    ["three", { messages: [message("one", 1), message("two", 2)], hasMore: false }],
  ]);
  const updates = await loadSocialUpdates(known, async before => {
    calls.push(before);
    const page = pages.get(before);
    assert.ok(page);
    return page;
  });
  assert.deepEqual(calls, [undefined, "five", "three"]);
  assert.deepEqual(mergeSocialMessages(known, updates.messages).map(item => item.messageId),
    ["one", "two", "three", "four", "five", "six"]);
});

test("an empty conversation loads one page and retains its older-page flag", async () => {
  let calls = 0;
  const result = await loadSocialUpdates([], async () => {
    calls += 1;
    return { messages: [message("latest", 1)], hasMore: true };
  });
  assert.equal(calls, 1);
  assert.equal(result.hasMore, true);
  assert.deepEqual(result.messages.map(item => item.messageId), ["latest"]);
});

test("overlapping polls use one request and a stale response cannot undo a local message", async () => {
  let finish: (page: SocialPage) => void = () => {};
  let calls = 0;
  const known = [message("mine", 1)];
  const published: string[][] = [];
  const poller = createSocialPoller(
    async () => {
      calls += 1;
      if (calls === 1) return new Promise<SocialPage>(resolve => { finish = resolve; });
      return { messages: [message("mine", 1, "saved")], hasMore: false };
    },
    () => known,
    page => published.push(page.messages.map(item => item.body || "")),
    () => assert.fail("poll unexpectedly failed"),
  );
  const first = poller.poll();
  const duplicate = poller.poll();
  assert.equal(calls, 1);
  poller.changed();
  finish({ messages: [message("mine", 1, "old")], hasMore: false });
  await Promise.all([first, duplicate]);
  assert.deepEqual(published, []);
  await poller.poll();
  assert.deepEqual(published, [["saved"]]);
  poller.dispose();
});
