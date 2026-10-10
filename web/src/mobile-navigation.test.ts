import test from "node:test";
import assert from "node:assert/strict";
import { mobileTabForPath, showGroupChip, studyGroupCaption } from "./mobile-navigation.ts";

test("mobile navigation selects the Android destination for main and nested chat routes", () => {
  for (const path of ["/", "/schedule", "/schedule/"]) assert.equal(mobileTabForPath(path), "schedule");
  assert.equal(mobileTabForPath("/maps"), "maps");
  assert.equal(mobileTabForPath("/homework"), "homework");
  for (const path of ["/chat", "/chat/people", "/chat/person/a", "/group"]) assert.equal(mobileTabForPath(path), "chat");
});

test("#27: pages outside the tab bar, settings and 404 highlight no tab", () => {
  for (const path of ["/week", "/settings", "/friends", "/legal/policy", "/chatty", "/maps-extra", "/group-extra", "/missing"])
    assert.equal(mobileTabForPath(path), null);
});

test("#27: group chip is hidden on settings and where the empty state already offers «Выбрать группу»", () => {
  assert.equal(showGroupChip("/settings", true), false);
  assert.equal(showGroupChip("/settings", false), false);
  assert.equal(showGroupChip("/legal/policy", true), false);
  for (const path of ["/", "/schedule", "/week", "/homework", "/group"]) {
    assert.equal(showGroupChip(path, false), false, path);
    assert.equal(showGroupChip(path, true), true, path);
  }
  assert.equal(showGroupChip("/maps", false), true, "maps has no own CTA");
});

test("group badge follows timetable parity including the user's inversion", () => {
  const period = { start: "2026-09-01", weekCount: 2 };
  assert.equal(studyGroupCaption("Н162С", period, new Date(2026, 8, 1), false), "Н162С · нечётная");
  assert.equal(studyGroupCaption("Н162С", period, new Date(2026, 8, 1), true), "Н162С · чётная");
  assert.equal(studyGroupCaption("Н162С", period, new Date(2026, 8, 8), false), "Н162С · чётная");
});

test("missing group or invalid period does not invent parity", () => {
  const now = new Date(2026, 9, 5);
  assert.equal(studyGroupCaption(undefined, null, now, false), "Выбрать группу");
  assert.equal(studyGroupCaption("   ", null, now, false), "Выбрать группу");
  assert.equal(studyGroupCaption("Н162С", null, now, false), "Н162С");
  assert.equal(studyGroupCaption("Н162С", { start: "bad", weekCount: 2 }, now, false), "Н162С");
  assert.equal(studyGroupCaption("Н162С", { start: "2026-09-01", weekCount: 0 }, now, false), "Н162С");
  assert.equal(studyGroupCaption("Н162С", { start: "2026-09-01", weekCount: 2 }, new Date(NaN), false), "Н162С");
});

test("r2: at 200% zoom (720×450) the bottom nav is compact and pages reserve its real height", async () => {
  const { readFile } = await import("node:fs/promises");
  const { bottomNavReserve } = await import("./mobile-navigation.ts");
  assert.equal(bottomNavReserve(44.4), 45);   // компактная панель — без прежнего минимума 64px
  assert.equal(bottomNavReserve(65), 65);
  assert.equal(bottomNavReserve(0), 64);      // скрыта/не измерена — прежний запас
  assert.equal(bottomNavReserve(Number.NaN), 64);
  const css = await readFile(new URL("./mobile-shell.css", import.meta.url), "utf8");
  const block = (q: string) => { const i = css.indexOf(q); assert.ok(i >= 0, q); return css.slice(i, css.indexOf("\n}\n", i)); };
  const short = block("@media (max-width: 959px) and (max-height: 500px)");
  assert.match(short, /\.bottom a, \.bottom button \{[^}]*min-height: 44px;[^}]*flex-direction: row;/);
  // 44px из 450px ≈ 9.8% высоты (было 65px ≈ 14%), касание не меньше 44px.
  assert.ok(44 / 450 < 0.1);
  const narrow = block("@media (max-width: 559px) and (max-height: 500px)");
  assert.match(narrow, /\.bottom \.bottom-label \{[^}]*clip: rect\(0, 0, 0, 0\)/);
  assert.doesNotMatch(narrow, /display: none/); // подпись остаётся доступным именем
  const app = await readFile(new URL("./App.tsx", import.meta.url), "utf8");
  const nav = app.slice(app.indexOf('<nav className="bottom"'), app.indexOf("</nav>", app.indexOf('<nav className="bottom"')));
  for (const label of ["Расписание", "Карты", "Домашка", "Чаты", "Разделы"]) assert.ok(nav.includes(`<span className="bottom-label">${label}</span>`), label);
  assert.ok(app.includes("bottomNavReserve(node.getBoundingClientRect().height)") && !app.includes("Math.max(64,"));
});
