import assert from "node:assert/strict";
import { test } from "node:test";
import { allHomeworkAudience, audienceLabel, audienceMemberIds } from "./homework-audience-policy.ts";
import type { GroupHome, GroupSpace } from "./types.ts";

test("selected roles and people form one deduplicated set of active recipients", () => {
  const home = { classmates: [{ userId: "a" }, { userId: "b" }, { userId: "c" }] } as GroupHome;
  const space = { desk: { grants: [{ roleId: "r", userId: "a" }, { roleId: "r", userId: "b" }, { roleId: "r", userId: "former" }] } } as GroupSpace;
  assert.deepEqual(audienceMemberIds({ kind: "selected", roleIds: ["r"], userIds: ["a", "c"] }, home, space), ["a", "b", "c"]);
  assert.deepEqual(audienceMemberIds(allHomeworkAudience(), home, space), ["a", "b", "c"]);
  assert.equal(audienceLabel(null), "Вся группа");
});
