import { test } from "node:test";
import assert from "node:assert/strict";
import { channelTitle, composerHelp, composerLabels, groupTabs, hasDesktopKeyboard, showInboxFilters, singleChat } from "./chat-ui.ts";

test("group tabs read «Чат · Каналы · Участники» and the general stream is «Чат»", () => {
  assert.deepEqual(groupTabs.map(([, label]) => label), ["Чат", "Каналы", "Участники"]);
  assert.equal(channelTitle({ topicId: null, title: "Общий поток" }), "Чат");
  assert.equal(channelTitle({ topicId: "t1", title: "Объявления" }), "Объявления");
});

test("Shift+Enter hint only with a desktop keyboard", () => {
  assert.equal(composerHelp(12, "", true), "12/2000 · Shift+Enter — новая строка");
  assert.equal(composerHelp(12, "", false), "12/2000");
  assert.equal(composerHelp(3, "Слишком длинно", false), "3/2000 · Слишком длинно");
  const media = (fine: boolean) => ({ matchMedia: (q: string) => ({ matches: fine && q.includes("pointer: fine") }) as MediaQueryList });
  assert.equal(hasDesktopKeyboard(media(true)), true);
  assert.equal(hasDesktopKeyboard(media(false)), false);
  assert.equal(hasDesktopKeyboard(undefined), false);
});

test("every composer icon has a label", () => {
  for (const label of Object.values(composerLabels)) assert.ok(label.length > 3);
  assert.equal(new Set(Object.values(composerLabels)).size, 4);
});

test("inbox filters from five chats; single chat opens directly unless ?all", () => {
  assert.equal(showInboxFilters(4), false);
  assert.equal(showInboxFilters(5), true);
  assert.equal(singleChat(["g"], false, "", ""), "g");
  assert.equal(singleChat(["g"], false, "", "?all=1"), null);
  assert.equal(singleChat(["g"], true, "", ""), null);
  assert.equal(singleChat(["g"], false, "сеть", ""), null);
  assert.equal(singleChat(["g", "p"], false, "", ""), null);
});

test("files compiled by stubbed UI tests inline the same wording", async () => {
  const { readFile } = await import("node:fs/promises");
  const read = (name: string) => readFile(new URL(`./${name}`, import.meta.url), "utf8");
  const [pages, topics, people, composer, chat] = await Promise.all(["pages.tsx", "topics.tsx", "people.tsx", "group-composer.tsx", "chat.tsx"].map(read));
  assert.ok(pages.includes('[ ["general","Чат"], ["channels","Каналы"], ["people","Участники"] ]'));
  assert.ok(!/>Все разделы</.test(pages) && !/"Состав"|"Общее"\]/.test(pages));
  assert.ok(topics.includes('topic.topicId === null ? "Чат" : topic.title'));
  for (const label of [composerLabels.attach, composerLabels.circle, composerLabels.voice]) assert.ok(people.includes(`title="${label}"`), label);
  assert.ok(!/Shift\+Enter — новая строка\{/.test(composer) && composer.includes("composerHelp("));
  assert.ok(chat.includes("Вступить по коду / Новый чат") && !chat.includes("Код, запросы и люди") && !/>Обновить</.test(chat));
});

test("channel headings use TopicMark from #52: icon names never show as text, user emoji stay as typed", async () => {
  const { readFile } = await import("node:fs/promises");
  const { topicIcon } = await import("./topic-icon.ts");
  const pages = await readFile(new URL("./pages.tsx", import.meta.url), "utf8");
  assert.equal((pages.match(/<TopicMark topic=\{thread\} \/> \{thread\.title\}/g) || []).length, 2);
  assert.doesNotMatch(pages, /thread\.icon/);
  assert.match(pages, /setThread\("list"\); \}\}>Каналы<\/button>/);
  assert.deepEqual(topicIcon("megaphone", "chat"), { icon: "megaphone" });
  assert.deepEqual(topicIcon("🦄", "chat"), { text: "🦄" });
});
