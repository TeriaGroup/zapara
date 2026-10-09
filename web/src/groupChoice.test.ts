import assert from "node:assert/strict";
import { test } from "node:test";
import { type CommunityFollow, followGroupCommunity, homeworkCommunityId, openGroupFace, resolveStoredGroup } from "./groupChoice.ts";

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

  const failed: CommunityFollow[] = [];
  await followGroupCommunity(
    { authenticated: true, groupId: "3313" },
    async () => { throw new Error("down"); },
    state => failed.push(state),
  );
  assert.deepEqual(failed, [
    { communityId: "", failed: false, pending: true },
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

test("the cleared community is pending, not «no community», until the lookup answers (#32)", async () => {
  let release: (rows: { communityId: string; role: string | null }[]) => void = () => {};
  const states: CommunityFollow[] = [];
  const task = followGroupCommunity({ authenticated: true, groupId: "2888" },
    () => new Promise(resolve => { release = resolve; }), state => states.push(state));
  assert.deepEqual(states, [{ communityId: "", failed: false, pending: true }]);
  release([]);
  await task;
  assert.deepEqual(states.at(-1), { communityId: "", failed: false });

  const guest: CommunityFollow[] = [];
  await followGroupCommunity({ authenticated: false, groupId: "2888" }, async () => [], state => guest.push(state));
  assert.deepEqual(guest.at(-1), { communityId: "", failed: false }, "without a lookup the answer is final at once");
});

test("homework page waits for the community lookup and shows a loading status instead of the empty state (#32)", async () => {
  const { readFile } = await import("node:fs/promises");
  const pages = await readFile(new URL("./pages.tsx", import.meta.url), "utf8");
  const follow = pages.slice(pages.indexOf("void followGroupCommunity("), pages.indexOf("void followGroupCommunity(") + 600);
  assert.match(follow, /if \(state\.pending\) return;[\s\S]*if \(!state\.communityId\) \{ setCopies\(\[\]\)/);
  assert.match(pages, /role="status">Загружаем задания…</);
});
