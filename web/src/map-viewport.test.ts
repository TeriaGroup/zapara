import assert from "node:assert/strict";
import { test } from "node:test";
import { boundedPan, pairCount } from "./map-viewport.ts";

test("pair counts use Russian singular, plural and teen forms", () => {
  assert.deepEqual([0, 1, 2, 4, 5, 11, 12, 14, 21, 22, 25].map(pairCount), ["0 пар", "1 пара", "2 пары", "4 пары", "5 пар", "11 пар", "12 пар", "14 пар", "21 пара", "22 пары", "25 пар"]);
});
test("pan is confined to actual image overflow and resets on fit", () => {
  const image = { width: 600, height: 200 }, viewport = { width: 800, height: 500 };
  assert.deepEqual(boundedPan(1000, -1000, 2, image, viewport), { x: 200, y: 0 });
  assert.deepEqual(boundedPan(200, 100, 1, image, viewport), { x: 0, y: 0 });
  assert.deepEqual(boundedPan(-1000, 1000, 4, image, viewport), { x: -800, y: 150 });
});
