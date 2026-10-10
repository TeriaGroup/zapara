import { test } from "node:test";
import assert from "node:assert/strict";
import { buildingName, findRoom, lessonRoom, nextLessonCaption } from "./map-labels.ts";
import type { Lesson, MapPlan } from "./types.ts";

const plans: MapPlan[] = [
  { id: "gk-2", building: "ГК", floor: 2, url: "" }, { id: "gk-4", building: "ГК", floor: 4, url: "" },
  { id: "ulk-2", building: "УЛК", floor: 2, url: "" }, { id: "ulk-3", building: "УЛК", floor: 3, url: "" },
];
const lesson = (over: Partial<Lesson>) => ({ timeStart: "18:30", timeEnd: "20:00", roomRaw: "268", classroomRaw: "268 (Фесто);", buildingRaw: "УЛК", ...over }) as Lesson;

test("building codes have full names", () => {
  assert.equal(buildingName("ГК"), "Главный корпус");
  assert.equal(buildingName("улк"), "Учебно-лабораторный корпус");
  assert.equal(buildingName("ХЗ"), "ХЗ");
});

test("next lesson caption shows time, room and plan; while running it says «Сейчас»", () => {
  const before = nextLessonCaption(lesson({}), plans, new Date(2026, 9, 12, 17, 0));
  assert.equal(before?.label, "Следующая пара");
  assert.equal(before?.text, "18:30 · 268 (Фесто) · УЛК, 2 этаж");
  assert.equal(before?.plan?.id, "ulk-2");
  const running = nextLessonCaption(lesson({}), plans, new Date(2026, 9, 12, 19, 0));
  assert.equal(running?.label, "Сейчас");
  assert.equal(running?.text, "до 20:00 · 268 (Фесто) · УЛК, 2 этаж");
  assert.equal(nextLessonCaption(null, plans, new Date()), null);
  const unmarked = nextLessonCaption(lesson({ roomRaw: "Дистанционно", classroomRaw: "Дистанционно", buildingRaw: "" }), plans, new Date(2026, 9, 12, 17, 0));
  assert.equal(unmarked?.plan, null);
  assert.equal(lessonRoom(lesson({ classroomRaw: "372*;" })), "372");
});

test("room search opens the matching building and floor", () => {
  assert.deepEqual(findRoom(plans, "УЛК 320"), { plan: plans[3], message: "Аудитория 320: Учебно-лабораторный корпус, 3 этаж" });
  assert.equal(findRoom(plans, "493").plan?.id, "gk-4");
  assert.equal(findRoom(plans, "").message, "");
  assert.match(findRoom(plans, "Фесто").message, /номер аудитории/);
  assert.equal(findRoom(plans, "999").plan, null);
});

test("auto-zoom is off by default; the graph loader keeps the route checks", async () => {
  const { autoZoomNextRoom, loadCampusGraph } = await import("./map-labels.ts");
  const { readFileSync } = await import("node:fs");
  const { createHash } = await import("node:crypto");
  assert.equal(autoZoomNextRoom, false);
  const bytes = readFileSync(new URL("../../src/Vograph.Desktop/Assets/maps/campus-graph.json", import.meta.url));
  const asset = { url: "/api/v1/maps/assets/graph.json", bytes: bytes.byteLength, sha256: createHash("sha256").update(bytes).digest("hex") };
  const fetcher = (async () => new Response(bytes)) as unknown as typeof fetch;
  const graph = await loadCampusGraph(asset, undefined, fetcher, "http://localhost");
  assert.ok(graph.nodes.some(node => node.kind === "room"));
  await assert.rejects(loadCampusGraph({ ...asset, bytes: 1 }, undefined, fetcher, "http://localhost"), /size/);
  await assert.rejects(loadCampusGraph({ ...asset, sha256: "00" }, undefined, fetcher, "http://localhost"), /hash/);
  await assert.rejects(loadCampusGraph({ ...asset, url: "https://evil.example/api/v1/maps/assets/g" }, undefined, fetcher, "http://localhost"), /origin/);
});
