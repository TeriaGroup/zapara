import assert from "node:assert/strict";
import { test } from "node:test";
import { isNearLatest, matchesBrowseQuery } from "./groupBrowse.ts";

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
