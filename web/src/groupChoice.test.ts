import assert from "node:assert/strict";
import { test } from "node:test";
import { followGroupCommunity, homeworkCommunityId, openGroupFace, resolveStoredGroup } from "./groupChoice.ts";

test("an explicit empty group stays empty and a missing choice takes the first group", () => {
  const groups = [{ id: "3313" }, { id: "0901" }];
  assert.equal(resolveStoredGroup(null, groups), "3313");
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
  const seen: { communityId: string; error: string }[] = [];
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
    { communityId: "", error: "Вы ещё не в группе" },
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
