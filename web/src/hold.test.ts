import assert from "node:assert/strict";
import { test } from "node:test";
import { holdActions, runHold } from "./hold.ts";

test("a hold opens message and media actions and a tap does not", () => {
  assert.deepEqual(holdActions("text", true, false, false), []);
  assert.deepEqual(holdActions("image", true, false, false), []);
  assert.deepEqual(holdActions("video", false, false, false), []);
  assert.deepEqual(holdActions("text", true, false, true), ["reply", "reaction", "edit", "delete"]);
  assert.deepEqual(holdActions("image", true, false, true), ["reply", "reaction", "delete"]);
  assert.deepEqual(holdActions("video", true, false, true), ["reply", "reaction", "delete"]);
  assert.deepEqual(holdActions("file", true, false, true), ["reply", "reaction", "delete"]);
  assert.deepEqual(holdActions("voice", false, false, true), ["reply", "reaction"]);
  assert.deepEqual(holdActions("text", true, true, true), []);
  assert.deepEqual(holdActions("video", false, false, true), ["reply", "reaction"]);
  const called: string[] = [];
  const ops = {
    reply: () => called.push("reply"),
    reaction: () => called.push("reaction"),
    edit: () => called.push("edit"),
    delete: () => called.push("delete")
  };
  runHold("reply", ops);
  runHold("reaction", ops);
  runHold("edit", ops);
  runHold("delete", ops);
  assert.deepEqual(called, ["reply", "reaction", "edit", "delete"]);
});
