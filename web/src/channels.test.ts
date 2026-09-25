import assert from "node:assert/strict";
import { test } from "node:test";
import { ballotBoardAfterMutation, canComposeChannel, canCreateBallot, channelAccentColor, filterTopics, isChatChannel, nextUnreadTopic, orderedTopics, topicPreview } from "./channels.ts";
import type { BallotBoard, GroupTopic } from "./types.ts";

const chat: GroupTopic = {
  topicId: "chat-id", title: "Учёба", icon: "📚", kind: "chat",
  lastBody: "Конспект готов", lastAuthor: "Аня", lastAt: "2026-09-25T10:00:00Z",
  unread: 2, canDelete: false, activeBallots: 0,
  description: "", accent: "default", pinned: false, writePolicy: "all", canPost: true,
};

test("chat previews name the sender and permit message composition", () => {
  assert.equal(topicPreview(chat), "Аня: Конспект готов");
  assert.equal(isChatChannel(chat), true);
  assert.equal(topicPreview({ ...chat, lastBody: null, lastAuthor: null }), "Сообщений пока нет");
});

test("ballot channel previews show question and live poll activity without treating it as a chat", () => {
  const ballot: GroupTopic = { ...chat, kind: "ballots", lastBody: "Где встречаемся?", activeBallots: 2 };
  assert.equal(topicPreview(ballot), "Где встречаемся? · 2 активных голосования");
  assert.equal(isChatChannel(ballot), false);
  assert.equal(topicPreview({ ...ballot, lastBody: null, activeBallots: 0 }), "Голосований пока нет");
});

test("ballot actions reload the selected channel because mutation responses show the global board", async () => {
  const global = { ballots: [{ ballotId: "legacy" }] } as BallotBoard;
  const scoped = { ballots: [{ ballotId: "channel" }] } as BallotBoard;
  const requested: string[] = [];
  const load = async (topicId: string) => { requested.push(topicId); return scoped; };
  assert.equal(await ballotBoardAfterMutation(global, "topic-id", load), scoped);
  assert.deepEqual(requested, ["topic-id"]);
  assert.equal(await ballotBoardAfterMutation(global, undefined, load), global);
  assert.deepEqual(requested, ["topic-id"]);
});

test("a restricted chat can be read but cannot compose, and a restricted ballot needs channel managers", () => {
  const restrictedChat = { ...chat, canPost: false, writePolicy: "managers" } as GroupTopic;
  const restrictedBallot = { ...restrictedChat, kind: "ballots" } as GroupTopic;
  assert.equal(canComposeChannel(restrictedChat), false);
  assert.equal(canComposeChannel({ ...restrictedChat, canPost: true }), true);
  assert.equal(canCreateBallot(restrictedBallot, false), false);
  assert.equal(canCreateBallot(restrictedBallot, true), true);
  assert.equal(canCreateBallot({ ...restrictedBallot, writePolicy: "all" }, false), true);
});

test("pinned channels lead the list after the built-in general stream", () => {
  const general = { ...chat, topicId: null, title: "Общий поток", pinned: false } as GroupTopic;
  const ordinary = { ...chat, topicId: "ordinary", title: "Учёба", pinned: false } as GroupTopic;
  const pinned = { ...chat, topicId: "pinned", title: "Объявления", pinned: true } as GroupTopic;
  assert.deepEqual(orderedTopics([ordinary, general, pinned]).map(topic => topic.title), ["Общий поток", "Объявления", "Учёба"]);
});

test("unread channels lead their unpinned peers without moving general or pinned channels", () => {
  const general = { ...chat, topicId: null, pinned: false, unread: 0 };
  const quiet = { ...chat, topicId: "quiet", pinned: false, unread: 0 };
  const unread = { ...chat, topicId: "unread", pinned: false, unread: 3 };
  const pinned = { ...chat, topicId: "pinned", pinned: true, unread: 0 };
  assert.deepEqual(orderedTopics([quiet, unread, pinned, general]).map(topic => topic.topicId), [null, "pinned", "unread", "quiet"]);
});

test("channel browse combines description search, type and unread filters", () => {
  const general = { ...chat, topicId: null, title: "Общий поток", description: "" };
  const ballot = { ...chat, topicId: "vote", kind: "ballots" as const, title: "Опросы", description: "Куда идём после пар", unread: 1 };
  const archive = { ...chat, topicId: "archive", title: "Конспекты", description: "После занятий", unread: 0 };
  assert.deepEqual(filterTopics([general, ballot, archive], { query: "  ПОСЛЕ  ", kind: "ballots", unreadOnly: true }).map(topic => topic.topicId), ["vote"]);
  assert.deepEqual(filterTopics([general, ballot, archive], { query: "", kind: "chat", unreadOnly: true }).map(topic => topic.topicId), [null]);
  assert.deepEqual(filterTopics([general, ballot, archive], { query: "несуществующее", kind: "all", unreadOnly: false }), []);
});

test("accent colors use a fixed palette and leave default channels with their existing color", () => {
  assert.equal(channelAccentColor("blue", "Учёба"), "#3d5a80");
  assert.equal(channelAccentColor("red", "Учёба"), "#6b4030");
  assert.equal(channelAccentColor("default", "Учёба"), "#3d5a6b");
});

test("next unread advances through real topics, skips the aggregate and wraps past the current one", () => {
  const general = { ...chat, topicId: null, title: "Общий поток", unread: 2 };
  const quiet = { ...chat, topicId: "quiet", unread: 0 };
  const next = { ...chat, topicId: "next", unread: 3 };
  const ballot = { ...chat, topicId: "poll", kind: "ballots" as const, unread: 7 };
  const aggregate = { ...chat, topicId: null, kind: "ballots" as const, unread: 9 };
  const topics = [quiet, ballot, next, general, aggregate];
  assert.equal(nextUnreadTopic(topics)?.topicId, null);
  assert.equal(nextUnreadTopic(topics, null)?.topicId, "poll");
  assert.equal(nextUnreadTopic(topics, "poll")?.topicId, "next");
  assert.equal(nextUnreadTopic(topics, "next")?.topicId, null);
  assert.equal(nextUnreadTopic([quiet, { ...ballot, unread: 0 }, aggregate], null), null);
});
