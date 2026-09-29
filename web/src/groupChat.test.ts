import assert from "node:assert/strict";
import { test } from "node:test";
import { clearSentGroupDraft, createGroupPoller, groupMediaSelectionIsCurrent, groupMessageQuery, loadGroupUpdates, mergeGroupMessages, newestUnseenIncoming } from "./groupChat.ts";
import type { ChatMessage } from "./types.ts";

function message(id: string, minute: number, body = id): ChatMessage {
  return {
    messageId: id, conversationId: "chat", senderId: "student", senderName: "Студент",
    kind: "text", body, createdAt: `2026-09-24T12:${String(minute).padStart(2, "0")}:00Z`, deleted: false, replyTo: null,
  };
}

test("a recent group page updates a message without discarding a local send or older rows", () => {
  const known = [message("one", 1), message("two", 2), message("my-send", 3)];
  const latest = [message("two", 2, "edited"), message("my-send", 3)];
  const merged = mergeGroupMessages(known, latest);
  assert.deepEqual(merged.map(item => item.messageId), ["one", "two", "my-send"]);
  assert.equal(merged[1].body, "edited");
});

test("a gap larger than the recent page is filled through the after cursor", async () => {
  const known = [message("one", 1), message("two", 2)];
  const calls: (string | undefined)[] = [];
  const pages = new Map<string | undefined, { messages: ChatMessage[]; hasMore: boolean }>([
    [undefined, { messages: [message("five", 5), message("six", 6)], hasMore: true }],
    ["two", { messages: [message("three", 3), message("four", 4)], hasMore: true }],
    ["four", { messages: [message("five", 5), message("six", 6)], hasMore: false }],
  ]);
  const updates = await loadGroupUpdates(known, async after => {
    calls.push(after);
    const page = pages.get(after);
    assert.ok(page);
    return page;
  });
  assert.deepEqual(calls, [undefined, "two", "four"]);
  assert.deepEqual(mergeGroupMessages(known, updates.messages).map(item => item.messageId),
    ["one", "two", "three", "four", "five", "six"]);
  assert.equal(updates.throughMessageId, "six");
  assert.equal(updates.hasOlder, true);
});

test("cursor pages retain server order when several messages share a timestamp", async () => {
  const known = [message("one", 1), message("two", 1)];
  const pages = new Map<string | undefined, { messages: ChatMessage[]; hasMore: boolean }>([
    [undefined, { messages: [message("five", 1), message("six", 1)], hasMore: true }],
    ["two", { messages: [message("three", 1), message("four", 1)], hasMore: true }],
    ["four", { messages: [message("five", 1), message("six", 1)], hasMore: false }],
  ]);
  const updates = await loadGroupUpdates(known, async after => {
    const page = pages.get(after);
    assert.ok(page);
    return page;
  });
  assert.deepEqual(updates.messages.map(item => item.messageId), ["three", "four", "five", "six"]);
});

test("an incomplete gap never publishes a read cursor from the latest page", async () => {
  await assert.rejects(loadGroupUpdates([message("one", 1)], async after => {
    if (!after) return { messages: [message("five", 5)], hasMore: true };
    throw new Error("catch-up unavailable");
  }), /catch-up unavailable/);
});

test("the first page exposes older history and prepending retains cursor order", async () => {
  const initial = await loadGroupUpdates([], async () => ({ messages: [message("three", 1), message("four", 1)], hasMore: true }));
  assert.equal(initial.hasOlder, true);
  assert.equal(initial.throughMessageId, "four");
  assert.deepEqual(mergeGroupMessages([message("one", 1), message("two", 1)], initial.messages).map(item => item.messageId),
    ["one", "two", "three", "four"]);
});

test("overlapping polls share a request and preserve unknown pre-send messages", async () => {
  let finish: (page: { messages: ChatMessage[]; hasMore: boolean }) => void = () => {};
  let calls = 0;
  const published: { ids: string[]; first: boolean; hasOlder: boolean }[] = [];
  const poller = createGroupPoller(
    async () => {
      calls += 1;
      if (calls === 1) return new Promise(resolve => { finish = resolve; });
      return { messages: [message("saved", 1)], hasMore: false };
    },
    () => [],
    (page, first) => published.push({ ids: page.messages.map(row => row.messageId), first, hasOlder: page.hasOlder }),
    () => assert.fail("poll unexpectedly failed"),
  );
  const first = poller.poll();
  const duplicate = poller.poll();
  assert.equal(calls, 1);
  poller.changed();
  finish({ messages: [message("stale", 1)], hasMore: false });
  await Promise.all([first, duplicate]);
  assert.deepEqual(published, [{ ids: ["stale"], first: true, hasOlder: false }]);
  await poller.poll();
  assert.deepEqual(published.at(-1), { ids: ["saved"], first: false, hasOlder: false });
  await poller.poll();
  assert.deepEqual(published.at(-1), { ids: ["saved"], first: false, hasOlder: false });
  poller.dispose();
});

test("a local send during failed gap catch-up cannot move the server cursor past inbound history", async () => {
  const mine = { ...message("mine", 9), senderId: "me" };
  let ui = [message("one", 1)];
  let round = 0;
  let rejectFirst!: (error: Error) => void;
  const poller = createGroupPoller(async after => {
    if (!after) {
      round++;
      return round === 1 ? { messages: [message("five", 5), message("six", 6)], hasMore: true }
        : { messages: [mine], hasMore: true };
    }
    assert.equal(after, "one");
    if (round === 1) return new Promise((_, reject) => { rejectFirst = reject; });
    return { messages: [message("two", 2), message("three", 3), message("four", 4), message("five", 5), message("six", 6), mine], hasMore: false };
  }, () => ui, updates => { ui = mergeGroupMessages(ui, updates.messages); }, () => {});
  const first = poller.poll();
  await new Promise(resolve => setImmediate(resolve));
  ui = mergeGroupMessages(ui, [mine]);
  poller.changed();
  rejectFirst(new Error("offline"));
  await first;
  await poller.poll();
  assert.deepEqual(ui.map(row => row.messageId), ["one", "two", "three", "four", "five", "six", "mine"]);
  poller.dispose();
});

test("an open direct chat notices only newly fetched messages from the other member", () => {
  const known = [message("old", 1)];
  const own = { ...message("mine", 2), senderId: "me" };
  const incoming = message("new", 3);
  assert.equal(newestUnseenIncoming(known, [message("old", 1, "edited"), own, incoming], "me")?.messageId, "new");
  assert.equal(newestUnseenIncoming(known, [message("old", 1, "edited"), own], "me"), null);
});

test("group message query safely combines a topic with one older or newer cursor", () => {
  assert.equal(groupMessageQuery("general", { before: "old-id" }), "?topic=general&before=old-id");
  assert.equal(groupMessageQuery("topic & one", { after: "new-id" }), "?topic=topic+%26+one&after=new-id");
  assert.equal(groupMessageQuery(undefined, { before: "old-id" }), "?before=old-id");
  assert.throws(() => groupMessageQuery("general", { before: "old-id", after: "new-id" }));
});

test("a sent draft clears only the matching conversation and only if the text is unchanged", () => {
  const drafts = { "chat-a": "hello", "chat-b": "other" };
  assert.deepEqual(clearSentGroupDraft(drafts, "chat-a", "hello"), { "chat-a": "", "chat-b": "other" });
  assert.deepEqual(clearSentGroupDraft({ ...drafts, "chat-a": "newer text" }, "chat-a", "hello"),
    { "chat-a": "newer text", "chat-b": "other" });
  assert.deepEqual(clearSentGroupDraft(drafts, "chat-a", "hello", false), drafts);
});

test("a file picked after switching chats, including away and back, cannot target the old chat", () => {
  const started = { key: "chat-a:direct", conversationId: "chat-a", epoch: 3 };
  assert.equal(groupMediaSelectionIsCurrent(started, started), true);
  assert.equal(groupMediaSelectionIsCurrent(started, { key: "chat-b:direct", conversationId: "chat-b", epoch: 4 }), false);
  assert.equal(groupMediaSelectionIsCurrent(started, { key: "chat-a:direct", conversationId: "chat-a", epoch: 5 }), false);
});
