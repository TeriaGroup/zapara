import assert from "node:assert/strict";
import { test } from "node:test";
import { readFileSync } from "node:fs";
import { catalogFailureNotice } from "./catalog-notice.ts";

test("#146: the banner promises a saved schedule only when the current group has one", () => {
  assert.equal(catalogFailureNotice(true, true), "Список групп не обновился. Сохранённое расписание остаётся доступным.");
  const none = catalogFailureNotice(true, false);
  assert.doesNotMatch(none, /расписание остаётся доступным/);
  assert.equal(none, "Список групп не обновился. Проверьте подключение к интернету.");
  assert.equal(catalogFailureNotice(false, false), "Список групп недоступен, сохранённой копии списка нет.");
  assert.equal(catalogFailureNotice(false, true), "Список групп недоступен, сохранённой копии списка нет.");
});

test("#146: the store checks the cached schedule of the current group, not just the cached group list", () => {
  const store = readFileSync(new URL("./store.tsx", import.meta.url), "utf8");
  assert.match(store, /setNotice\(catalogFailureNotice\(!!catalog, !!current && !!api\.readCache\(\)\.lessons\[current\]\)\);/);
  assert.match(store, /const current = groupSelection\.current\.groupId;/);
  assert.doesNotMatch(store, /catalog \? "Список групп не обновился\. Сохранённое расписание остаётся доступным\."/);
});
