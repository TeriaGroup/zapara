import assert from "node:assert/strict";
import { test } from "node:test";
import { normalizeIntersectionStrictness } from "./intersectionStrictness.ts";

test("legacy university, missing and invalid preferences fall back to building", () => {
  for (const value of [25, "25", null, undefined, "", "bad", NaN, Infinity, -Infinity]) {
    assert.equal(normalizeIntersectionStrictness(value), 50, String(value));
  }
});

test("preferences select the nearest supported level and clamp safely", () => {
  for (const [value, expected] of [[50, 50], [75, 75], [100, 100], [62, 50], [63, 75], [87, 75], [88, 100], [-1, 50], [0, 50], [101, 100], [Number.MAX_VALUE, 100], [-Number.MAX_VALUE, 50]]) {
    assert.equal(normalizeIntersectionStrictness(value), expected);
  }
  assert.equal(normalizeIntersectionStrictness("75"), 75);
});
