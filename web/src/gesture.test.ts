import test from "node:test";
import assert from "node:assert/strict";
import { PointerGesture } from "./gesture.ts";

const point = (id = 1, x = 100, y = 100, type = "touch", primary = true) => ({ id, x, y, type, primary });
test("horizontal swipe requires 64 pixels and preserves direction", () => {
  const g = new PointerGesture("horizontal");
  g.start(point()); assert.equal(g.finish(point(1, 37)), null);
  g.start(point()); assert.equal(g.finish(point(1, 36)), "negative");
  g.start(point()); assert.equal(g.finish(point(1, 164)), "positive");
});
test("mouse, secondary pointers, cancellation and foreign pointer cannot navigate", () => {
  const g = new PointerGesture("horizontal");
  g.start(point(1, 100, 100, "mouse")); assert.equal(g.finish(point(1, 0)), null);
  g.start(point()); g.start(point(2, 100, 100, "touch", false)); assert.equal(g.finish(point(1, 0)), null);
  g.start(point()); assert.equal(g.finish(point(2, 0)), null); assert.equal(g.finish(point(1, 0)), null);
  g.start(point()); g.cancel(); assert.equal(g.finish(point(1, 0)), null);
});
test("vertical scroll locks rejection even when later motion turns horizontal", () => {
  const g = new PointerGesture("horizontal");
  g.start(point()); g.move(point(1, 103, 125)); assert.equal(g.finish(point(1, 0, 126)), null);
  g.start(point()); assert.equal(g.finish(point(1, 0, 210)), null);
});
test("sheet gesture distinguishes downward motion and horizontal scrolling", () => {
  const g = new PointerGesture("vertical");
  g.start(point(1, 100, 100, "pen")); assert.equal(g.finish(point(1, 102, 164)), "positive");
  g.start(point()); g.move(point(1, 130, 102)); assert.equal(g.finish(point(1, 130, 200)), null);
  g.start(point()); assert.equal(g.finish(point(1, 100, 36)), "negative");
});
