import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { topicIcon } from "./topic-icon.ts";

test("server starter icon names render as icons, not words (#30)", () => {
  assert.deepEqual(topicIcon("megaphone", "chat"), { icon: "megaphone" });
  assert.deepEqual(topicIcon("vote", "ballots"), { icon: "ballot" });
});

test("emoji from web templates keep their icons", () => {
  assert.deepEqual(topicIcon("💬", "chat"), { icon: "chat" });
  assert.deepEqual(topicIcon("📌", "chat"), { icon: "pin" });
  assert.deepEqual(topicIcon("🗳️", "ballots"), { icon: "ballot" });
  assert.deepEqual(topicIcon("📎", "materials"), { icon: "paperclip" });
});

test("unknown names and empty values fall back to the channel kind; user emoji stay as typed", () => {
  assert.deepEqual(topicIcon("rocket-launch", "ballots"), { icon: "ballot" });
  assert.deepEqual(topicIcon("", "homework"), { icon: "homework" });
  assert.deepEqual(topicIcon(null, "unknown-kind"), { icon: "chat" });
  assert.deepEqual(topicIcon("🚀", "chat"), { text: "🚀" });
});

test("channel headings use TopicMark instead of the raw icon string (#30)", async () => {
  const pages = await readFile(new URL("./pages.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(pages, /\{thread\.icon\}|\$\{thread\.icon\}/);
  const icons = await readFile(new URL("./icons.tsx", import.meta.url), "utf8");
  assert.match(icons, /\n\s*megaphone: "/);
});
