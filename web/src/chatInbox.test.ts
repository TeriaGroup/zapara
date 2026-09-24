import assert from "node:assert/strict";
import { test } from "node:test";
import { mergeChatInbox } from "./chatInbox.ts";
import type { Conversation, GroupHome, SocialHome } from "./types.ts";

function conversation(id: string, kind: "group" | "direct", lastAt: string | null, unread: number): Conversation {
  return { conversationId: id, kind, communityId: "study-a", title: kind === "direct" ? "Аня" : "Группа А",
    peerUserId: kind === "direct" ? "peer-a" : null, lastBody: "Сообщение", lastAt, unread };
}

test("one inbox orders group, classmate and account chats by the newest message", () => {
  const group: GroupHome = { communityId: "study-a", name: "Группа А", groupName: "А863С",
    groupChat: conversation("group-chat", "group", "2026-09-24T12:00:00Z", 2),
    classmates: [], directs: [conversation("classmate-chat", "direct", "2026-09-24T14:00:00Z", 1)] };
  const social: SocialHome = { code: "ABCDEFGH", incoming: [], outgoing: [], friends: [
    { userId: "peer-b", username: "peer", displayName: "Борис", conversationId: "personal-chat",
      lastBody: "Привет", lastAt: "2026-09-24T13:00:00Z", unread: 3 },
  ] };
  const rows = mergeChatInbox([group], social);
  assert.deepEqual(rows.map(item => [item.kind, item.conversationId, item.unread]), [
    ["classmate", "classmate-chat", 1], ["personal", "personal-chat", 3], ["group", "group-chat", 2],
  ]);
  assert.equal(rows[0].communityId, "study-a");
  assert.equal(rows[1].title, "Борис");
});

test("empty conversations remain reachable after active ones", () => {
  const group: GroupHome = { communityId: "study-a", name: "Группа А", groupName: null,
    groupChat: conversation("empty-group", "group", null, 0), classmates: [], directs: [] };
  const rows = mergeChatInbox([group], null);
  assert.equal(rows.length, 1);
  assert.equal(rows[0].conversationId, "empty-group");
  assert.equal(rows[0].lastAt, null);
});
