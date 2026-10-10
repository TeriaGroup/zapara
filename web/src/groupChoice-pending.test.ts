import test from "node:test";
import assert from "node:assert/strict";
import { followGroupCommunity, type CommunityFollowState as CommunityFollow } from "./groupChoice.ts";

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
