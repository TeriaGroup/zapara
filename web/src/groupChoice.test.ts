import assert from "node:assert/strict";
import { test } from "node:test";
import { followGroupCommunity, homeworkCommunityId, openGroupFace, resolveStoredGroup, type GroupFace } from "./groupChoice.ts";

test("a missing or explicit empty choice stays empty until the student selects a group", () => {
  const groups = [{ id: "3313" }, { id: "0901" }];
  assert.equal(resolveStoredGroup(null, groups), "");
  assert.equal(resolveStoredGroup("", groups), "");
  assert.equal(resolveStoredGroup("0901", groups), "0901");
  assert.equal(resolveStoredGroup("gone", groups), "");
});

test("shared homework follows the selected group, not the first name", () => {
  const rows = [
    { communityId: "alpha", role: "member", groupId: "111" },
    { communityId: "zeta", role: "member", groupId: "3313" },
  ];
  assert.equal(homeworkCommunityId("3313", rows), "zeta");
  assert.equal(homeworkCommunityId("", rows), "");
  assert.equal(homeworkCommunityId("3313", [{ communityId: "alpha", role: null, groupId: "3313" }]), "");
});

test("switching groups drops the previous community before the lookup answers", async () => {
  let release: (rows: { communityId: string; role: string | null }[]) => void = () => {};
  const pending = new Promise<{ communityId: string; role: string | null }[]>(resolve => { release = resolve; });
  const seen: string[] = [];
  const task = followGroupCommunity(
    { authenticated: true, groupId: "3313" },
    () => pending,
    state => seen.push(state.communityId),
  );
  assert.deepEqual(seen, [""]);
  release([{ communityId: "zeta", role: "member" }]);
  await task;
  assert.deepEqual(seen, ["", "zeta"]);

  const missed: string[] = [];
  await followGroupCommunity(
    { authenticated: true, groupId: "0901" },
    async () => [{ communityId: "alpha", role: null }],
    state => missed.push(state.communityId),
  );
  assert.deepEqual(missed, ["", ""]);

  const failed: { communityId: string; failed: boolean }[] = [];
  await followGroupCommunity(
    { authenticated: true, groupId: "3313" },
    async () => { throw new Error("down"); },
    state => failed.push(state),
  );
  assert.deepEqual(failed, [
    { communityId: "", failed: false },
    { communityId: "", failed: true },
  ]);
});

test("a group page starts blank and stays blank when the new group has no membership", async () => {
  let release: (rows: { communityId: string; role: string | null }[]) => void = () => {};
  const pending = new Promise<{ communityId: string; role: string | null }[]>(resolve => { release = resolve; });
  const seen: GroupFace[] = [];
  const task = openGroupFace(
    { authenticated: true, groupId: "0901" },
    () => pending,
    face => seen.push(face),
  );
  assert.deepEqual(seen, [{ communityId: "", error: "" }]);
  release([]);
  await task;
  assert.deepEqual(seen, [
    { communityId: "", error: "" },
    { communityId: "", error: "", missing: "membership", candidate: null },
  ]);

  const broken: { communityId: string; error: string }[] = [];
  await openGroupFace(
    { authenticated: true, groupId: "3313" },
    async () => { throw new Error("down"); },
    face => broken.push(face),
  );
  assert.equal(broken[0].communityId, "");
  assert.equal(broken.at(-1)?.communityId, "");
  assert.equal(broken.at(-1)?.error, "Не удалось загрузить группу");
});

test("missing membership is a separate state with the group's community to join, not a load error (#36)", async () => {
  const faces: GroupFace[] = [];
  await openGroupFace({ authenticated: true, groupId: "2888" },
    async () => [{ communityId: "c1", role: null, name: "Группа А131С" }], face => faces.push(face));
  assert.deepEqual(faces.at(-1), { communityId: "", error: "", missing: "membership", candidate: { communityId: "c1", name: "Группа А131С" } });

  const none: GroupFace[] = [];
  await openGroupFace({ authenticated: true, groupId: " " }, async () => { throw new Error("not called"); }, face => none.push(face));
  assert.deepEqual(none.at(-1), { communityId: "", error: "", missing: "group" });

  const member: GroupFace[] = [];
  await openGroupFace({ authenticated: true, groupId: "2888" },
    async () => [{ communityId: "c1", role: "member", name: "Группа А131С" }], face => member.push(face));
  assert.deepEqual(member.at(-1), { communityId: "c1", error: "" });
});
