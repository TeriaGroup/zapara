import assert from "node:assert/strict";
import { test } from "node:test";
import { projectAccountReminders } from "./reminder-settings.ts";

const guest = { enabled: true, morning: true, evening: true, morningAt: "08:00", eveningAt: "21:00" };
test("account reminders follow cloud times without changing guest defaults", () => {
  const projected = projectAccountReminders({ notifyTime1: null, notifyTime2: "07:15" }, guest);
  assert.deepEqual(projected, { enabled: true, morning: true, evening: false, morningAt: "07:15", eveningAt: "21:00" });
  assert.deepEqual(guest, { enabled: true, morning: true, evening: true, morningAt: "08:00", eveningAt: "21:00" });
  assert.equal(projectAccountReminders({ notifyTime1: null, notifyTime2: null }, guest).enabled, false);
});
