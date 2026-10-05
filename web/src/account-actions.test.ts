import test from "node:test";
import assert from "node:assert/strict";
import { mergeDevicePages, resetRequestReady, resetConfirmationReady } from "./account-actions.ts";
test("device page retry deduplicates by immutable session family and refreshes existing row", () => {
  assert.deepEqual(mergeDevicePages([{familyId:"a",name:"Old"}],[{familyId:"a",name:"New"},{familyId:"b",name:"Phone"}]),[{familyId:"a",name:"New"},{familyId:"b",name:"Phone"}]);
});
test("reset steps require their own fields and respect existing password/token contract", () => {
  assert.equal(resetRequestReady("  user_1 "),true); assert.equal(resetRequestReady("x"),false);
  assert.equal(resetConfirmationReady("a".repeat(43),"a".repeat(12)),true);
  assert.equal(resetConfirmationReady("short","a".repeat(12)),false);
  assert.equal(resetConfirmationReady("a".repeat(43),"a".repeat(11)),false);
});
