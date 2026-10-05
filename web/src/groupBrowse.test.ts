import assert from "node:assert/strict";
import { test } from "node:test";
import { isNearLatest, matchesBrowseQuery, unreadBadgeDescription, unreadBadgeText } from "./groupBrowse.ts";

test("group and member search ignores case and surrounding spaces", () => {
  assert.equal(matchesBrowseQuery("  ВОЕН  ", "Группа Военмеха"), true);
  assert.equal(matchesBrowseQuery("глеб", "Максим", "Глеб Бова", "user42"), true);
  assert.equal(matchesBrowseQuery("user42", "Глеб Бова", "user42"), true);
  assert.equal(matchesBrowseQuery("другая", "Глеб Бова", "user42"), false);
  assert.equal(matchesBrowseQuery("   ", "Любая группа"), true);
});

test("jump to latest is needed when the chat is more than 48px from the bottom", () => {
  assert.equal(isNearLatest(500, 400, 920), true);
  assert.equal(isNearLatest(200, 400, 920), false);
});

test("unread badges cap the visible number but preserve the exact accessible count", () => {
  assert.equal(unreadBadgeText(1), "1");
  assert.equal(unreadBadgeText(99), "99");
  assert.equal(unreadBadgeText(100), "99+");
  assert.equal(unreadBadgeText(1000), "99+");
  assert.equal(unreadBadgeDescription(100), "Непрочитанных сообщений: 100");
});
