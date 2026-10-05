import test from "node:test";
import assert from "node:assert/strict";
import { filterChatInbox } from "./chatInbox.ts";
test("unread filter composes with source and query without modifying read counts", () => {
  const rows = [0,2,3].map((unread,index) => ({kind: index === 2 ? "group" as const : "personal" as const, conversationId:String(index),communityId:null,peerUserId:null,title:"Физика",preview:null,lastAt:null,unread}));
  assert.deepEqual(filterChatInbox(rows,"физ","personal",true).map(row => row.conversationId),["1"]);
  assert.deepEqual(rows.map(row => row.unread),[0,2,3]);
});
