import assert from "node:assert/strict";
import { test } from "node:test";
import { reconcileGroupHomeChat } from "./group-home-refresh.ts";
import type { Conversation, GroupHome } from "./types.ts";

test("group home refresh updates selected conversation metadata without changing its identity", () => {
  const direct: Conversation = { conversationId: "direct", kind: "direct", communityId: "group", title: "Старое имя", peerUserId: "peer", lastBody: "старое", lastAt: null, unread: 0 };
  const groupChat: Conversation = { ...direct, conversationId: "general", kind: "group", peerUserId: null };
  const home: GroupHome = { communityId: "group", name: "Группа", groupName: "А1", groupChat, classmates: [], directs: [{ ...direct, title: "Новое имя", lastBody: "новое", unread: 1 }] };
  const next = reconcileGroupHomeChat(direct, home);
  assert.equal(next?.conversationId, "direct");
  assert.equal(next?.title, "Новое имя");
  assert.equal(next?.unread, 1);
  assert.equal(reconcileGroupHomeChat(null, home), null);
});
