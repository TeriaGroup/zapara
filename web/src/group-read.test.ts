import assert from "node:assert/strict";
import { test } from "node:test";
import * as api from "./api.ts";

test("group message GET opts into explicit read cursors and POST acknowledges one delivered ID", async () => {
  const oldFetch = globalThis.fetch;
  const calls: { url: string; init: RequestInit }[] = [];
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), init: init || {} });
    return init?.method === "POST" ? Response.json({ conversationId: "conversation" }) : Response.json({ messages: [], hasMore: false });
  };
  try {
    await api.messages("conversation", "general");
    await api.markRead("conversation", "last-delivered");
    assert.equal((calls[0].init.headers as Record<string, string>)["X-Zapara-Read-Cursor"], "1");
    assert.equal(calls[1].url, "/web-api/communities/conversations/conversation/read");
    assert.deepEqual(JSON.parse(String(calls[1].init.body)), { throughMessageId: "last-delivered" });
  } finally { globalThis.fetch = oldFetch; }
});
