import assert from "node:assert/strict";
import { test } from "node:test";
import { ballots, createTopic, deleteTopic, openHeadmanBallot, proposeBallot, renameTopic, topics } from "./api.ts";
import { postGroupMedia } from "./group-media.ts";

test("ballot channels load their own board and submit votes to the selected channel", async () => {
  const original = globalThis.fetch;
  const calls: { url: string; body: unknown }[] = [];
  globalThis.fetch = async (input, init) => {
    calls.push({ url: String(input), body: init?.body ? JSON.parse(String(init.body)) : null });
    return Response.json({ ballots: [] });
  };
  try {
    await ballots("group-id", "ballot-channel-id");
    await openHeadmanBallot("group-id", "Где встречаемся?", ["А", "Б"], 3, "ballot-channel-id");
    await proposeBallot("group-id", "Когда встречаемся?", ["Ср", "Чт"], 5, "ballot-channel-id");
    await ballots("group-id");
    assert.deepEqual(calls, [
      { url: "/web-api/communities/group-id/ballots?topic=ballot-channel-id", body: null },
      { url: "/web-api/communities/group-id/ballots/headman", body: { question: "Где встречаемся?", options: ["А", "Б"], days: 3, topicId: "ballot-channel-id" } },
      { url: "/web-api/communities/group-id/ballots/collective", body: { question: "Когда встречаемся?", options: ["Ср", "Чт"], days: 5, topicId: "ballot-channel-id" } },
      { url: "/web-api/communities/group-id/ballots", body: null },
    ]);
  } finally { globalThis.fetch = original; }
});

test("creating a channel sends its immutable type and keeps permission from the topic list", async () => {
  const original = globalThis.fetch;
  const calls: { url: string; body: unknown }[] = [];
  globalThis.fetch = async (input, init) => {
    calls.push({ url: String(input), body: init?.body ? JSON.parse(String(init.body)) : null });
    return Response.json({ topics: [], canManageChannels: true });
  };
  try {
    const list = await topics("group-id");
    const created = await createTopic("group-id", "Опросы", "🗳️", "ballots");
    await renameTopic("group-id", "topic-id", "Важные опросы", "📌", "ballots");
    await deleteTopic("group-id", "topic-id");
    assert.equal(list.canManageChannels, true);
    assert.equal(created.canManageChannels, true);
    assert.deepEqual(calls, [
      { url: "/web-api/communities/group-id/topics?typed=1", body: null },
      { url: "/web-api/communities/group-id/topics?typed=1", body: { title: "Опросы", icon: "🗳️", kind: "ballots" } },
      { url: "/web-api/communities/group-id/topics/topic-id?typed=1", body: { title: "Важные опросы", icon: "📌", kind: "ballots" } },
      { url: "/web-api/communities/group-id/topics/topic-id/delete?typed=1", body: null },
    ]);
  } finally { globalThis.fetch = original; }
});

test("group media names a custom chat channel, while the general stream omits that header", async () => {
  const headers: Headers[] = [];
  const post: typeof fetch = async (_url, init) => {
    headers.push(new Headers(init?.headers));
    return Response.json({});
  };
  const blob = new Blob(["a"]);
  await postGroupMedia("conversation", "file", "notes.txt", blob, undefined, post, {}, undefined, "chat-channel-id");
  await postGroupMedia("conversation", "file", "notes.txt", blob, undefined, post, {});
  assert.equal(headers[0].get("X-Zapara-Topic"), "chat-channel-id");
  assert.equal(headers[1].get("X-Zapara-Topic"), null);
});

test("channel metadata is sent on create and edit with the server's field names", async () => {
  const original = globalThis.fetch;
  const bodies: unknown[] = [];
  globalThis.fetch = async (_input, init) => {
    bodies.push(JSON.parse(String(init?.body)));
    return Response.json({ topics: [], canManageChannels: true });
  };
  try {
    const metadata = { description: "Новости группы", accent: "purple" as const, pinned: true, writePolicy: "managers" as const };
    await createTopic("group-id", "Объявления", "📌", "chat", metadata);
    await renameTopic("group-id", "topic-id", "Важные объявления", "📌", "chat", metadata);
    assert.deepEqual(bodies, [
      { title: "Объявления", icon: "📌", kind: "chat", ...metadata },
      { title: "Важные объявления", icon: "📌", kind: "chat", ...metadata },
    ]);
  } finally { globalThis.fetch = original; }
});
