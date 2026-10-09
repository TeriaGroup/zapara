import { test } from "node:test";
import assert from "node:assert/strict";
import { groupContextExpanded, groupContextExpandedByDefault, groupContextToggleLabel, groupSubtitle } from "./group-panel.ts";

test("group context panel starts the same on desktop and phone and remembers the choice", () => {
  assert.equal(groupContextExpandedByDefault, false);
  assert.equal(groupContextExpanded({}, "c1"), false);
  assert.equal(groupContextExpanded({ c1: true }, "c1"), true);
  assert.equal(groupContextExpanded({ c1: true }, "c2"), false);
  assert.equal(groupContextToggleLabel(false), "Развернуть");
  assert.equal(groupContextToggleLabel(true), "Свернуть");
});

test("group subtitle does not repeat the group code", () => {
  assert.equal(groupSubtitle("Группа И831Б", "И831Б"), "Группа И831Б");
  assert.equal(groupSubtitle("И831Б", "И831Б"), "И831Б");
  assert.equal(groupSubtitle("Робототехники", "И831Б"), "Робототехники · И831Б");
  assert.equal(groupSubtitle("Группа и831б", "И831Б"), "Группа и831б");
  assert.equal(groupSubtitle("Группа", null), "Группа");
});
