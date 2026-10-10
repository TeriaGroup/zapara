import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { channelTitle, groupTabs } from "./chat-ui.ts";
import { S } from "./strings.gen.ts";

const pages = readFileSync(new URL("./pages.tsx", import.meta.url), "utf8");
const css = readFileSync(new URL("./styles.css", import.meta.url), "utf8");

/** Все объявления свойства prop для точного селектора, в порядке файла: последнее побеждает. */
function declarations(selector: string, prop: string): string[] {
  const out: string[] = [];
  const rule = /([^{}]+)\{([^{}]*)\}/g;
  for (let m; (m = rule.exec(css));) {
    const selectors = m[1].replace(/\/\*[\s\S]*?\*\//g, "").split(",").map(s => s.trim());
    if (!selectors.includes(selector)) continue;
    for (const d of m[2].split(";")) {
      const [k, ...v] = d.split(":");
      if (k?.trim() === prop) out.push(v.join(":").trim());
    }
  }
  return out;
}

test("r2 regression: group sender name sits on its own line above the text (#69 made .message-text inline)", () => {
  assert.match(pages, /<b className="message-sender">\{message\.senderName\}<\/b>/);
  assert.doesNotMatch(pages, /<b>\{message\.senderName\}<\/b>/);
  // Текст и правда inline (время в строке с текстом, #69) — значит, имени нужен свой блок.
  assert.equal(declarations(".bubble > .message-text", "display").at(-1), "inline");
  assert.equal(declarations(".bubble > .message-sender", "display").at(-1), "block");
});

test("r2: the group chat header does not shrink under the message list on a phone", () => {
  assert.equal(declarations(".chat > .group-thread-tools", "flex").at(-1), "none");
  assert.equal(declarations(".chat > .group-thread-tools", "overflow-y").at(-1), "auto");
  assert.match(pages, /<div className="group-thread-tools">/);
});

test("r2: the general group chat is called «Чат» from the catalog, not the server's «Чатик»", () => {
  assert.equal(S.groupChat, "Чат");
  assert.equal(channelTitle({ topicId: null, title: "Чатик" }), "Чат");
  assert.equal(channelTitle({ topicId: "t-1", title: "Важное" }), "Важное");
  assert.equal(groupTabs[0][1], S.groupChat);
  assert.match(pages, /<span>\{channelTitle\(topic\)\}<\/span>/);
  assert.doesNotMatch(pages, /<span>\{topic\.title\}<\/span>/);
  assert.match(pages, /\{groupTabs\.map\(/);
});
