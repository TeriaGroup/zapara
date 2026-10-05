import test from "node:test";
import assert from "node:assert/strict";
import { mobileViewportFrame, mobileViewportBaseline } from "./mobile-viewport.ts";

test("overlay keyboard reserves only the obscured viewport and hides navigation", () => {
  assert.deepEqual(mobileViewportFrame({ layoutHeight: 800, visibleHeight: 400, offsetTop: 80, baselineHeight: 800, editing: true }),
    { top: 80, bottom: 320, keyboardOpen: true });
});
test("resize keyboard also hides navigation even when no overlay inset remains", () => {
  assert.deepEqual(mobileViewportFrame({ layoutHeight: 400, visibleHeight: 400, baselineHeight: 800, editing: true }),
    { top: 0, bottom: 0, keyboardOpen: true });
});
test("browser toolbar movement, blur and pinch zoom are not keyboard events", () => {
  assert.equal(mobileViewportFrame({ layoutHeight: 800, visibleHeight: 740, baselineHeight: 800, editing: true }).keyboardOpen, false);
  assert.equal(mobileViewportFrame({ layoutHeight: 800, visibleHeight: 400, baselineHeight: 800, editing: false }).keyboardOpen, false);
  assert.deepEqual(mobileViewportFrame({ layoutHeight: 800, visibleHeight: 400, offsetTop: 30, baselineHeight: 800, editing: true, scale: 2 }),
    { top: 0, bottom: 0, keyboardOpen: false });
});
test("missing or malformed viewport values retain a valid layout without negative insets", () => {
  assert.deepEqual(mobileViewportFrame({ layoutHeight: 800, baselineHeight: 800, editing: true }), { top: 0, bottom: 0, keyboardOpen: false });
  assert.deepEqual(mobileViewportFrame({ layoutHeight: 800, visibleHeight: NaN, offsetTop: -10, baselineHeight: 800, editing: false }), { top: 0, bottom: 0, keyboardOpen: false });
});

test("rotation during typing keeps the unobscured baseline until the editor loses focus", () => {
  const portrait = { width: 360, height: 800 };
  const editing = mobileViewportBaseline(portrait, 640, 300, true);
  assert.deepEqual(editing, portrait);
  assert.equal(mobileViewportFrame({ layoutHeight: 300, visibleHeight: 300, baselineHeight: editing.height, editing: true }).keyboardOpen, true);
  assert.deepEqual(mobileViewportBaseline(editing, 640, 400, false), { width: 640, height: 400 });
});
test("non-editing layout resize accepts the new width and unobscured height", () => {
  assert.deepEqual(mobileViewportBaseline({ width: 360, height: 800 }, 640, 400, false), { width: 640, height: 400 });
  assert.deepEqual(mobileViewportBaseline({ width: 360, height: 740 }, 360, 800, false), { width: 360, height: 800 });
});
