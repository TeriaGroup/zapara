import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { campusPlaces, computeCampusRoute, parseCampusGraph, resolveCampusClassroom } from "./campus-routing.ts";

const node = (id: string, x: number, y: number, kind = "junction", extra: Record<string, unknown> = {}) =>
  ({ id, kind, building: "ГК", floor: 1, x, y, ...extra });
const edge = (from: string, to: string, seconds = 1, extra: Record<string, unknown> = {}) =>
  ({ from, to, kind: "walk", seconds, oneWay: false, ...extra });
const graph = (nodes: unknown[], edges: unknown[], blocked: unknown[] = []) =>
  parseCampusGraph({ version: 1, buildings: ["ГК", "УЛК"], nodes, edges, blocked });

test("unsupported, broken and ambiguous graph data fail before a route is offered", () => {
  for (const input of [null, {}, { version: 2, buildings: [], nodes: [], edges: [] },
    { version: 1, buildings: ["ГК"], nodes: [node("a", 0, 0), node("a", 1, 1)], edges: [] },
    { version: 1, buildings: ["ГК"], nodes: [node("a", Number.NaN, 0)], edges: [] }])
    assert.throws(() => parseCampusGraph(input));
  assert.throws(() => graph([node("a", 0, 0)], [edge("a", "missing")]));
  assert.throws(() => graph([node("a", 0, 0), node("b", 1, 1)], [edge("a", "b", -1)]));
});

test("authored geometry joins actual endpoints and only valid adjacent stairs are accepted", () => {
  assert.throws(() => graph([node("a", 0, 0), node("b", 1, 1)],
    [edge("a", "b", 1, { points: [[0.2, 0.2], [1, 1]] })]));
  assert.throws(() => graph([node("a", 0, 0, "stair", { group: "s" }),
    node("b", 0, 0, "stair", { group: "s", floor: 3 })], [edge("a", "b", 1, { kind: "stair_up" })]));
});

test("unknown, stationary and unreachable destinations remain different outcomes", () => {
  const g = graph([node("a", 0, 0), node("b", 1, 1)], []);
  assert.deepEqual(computeCampusRoute(g, "missing", "a"), { ok: false, failure: "unknown_place" });
  assert.deepEqual(computeCampusRoute(g, "a", "b"), { ok: false, failure: "unreachable" });
  const same = computeCampusRoute(g, "a", "a");
  assert.ok(same.ok); assert.equal(same.route.durationSeconds, 0); assert.deepEqual(same.route.steps, []);
});

test("one-way routes and reverse authored polylines retain their meaning", () => {
  const nodes = [node("a", 0, 0), node("b", 1, 1)];
  const g = graph(nodes, [edge("a", "b", 1.5, { points: [[0, 0], [0.5, 0.7], [1, 1]] })]);
  const reverse = computeCampusRoute(g, "b", "a");
  assert.ok(reverse.ok); assert.equal(reverse.route.durationSeconds, 2);
  assert.deepEqual(reverse.route.legs[0].points, [[1, 1], [0.5, 0.7], [0, 0]]);
  const directed = graph(nodes, [edge("a", "b", 1, { oneWay: true })]);
  assert.deepEqual(computeCampusRoute(directed, "b", "a"), { ok: false, failure: "unreachable" });
});

test("an intermediate room cannot become a shortcut through somebody's classroom", () => {
  const g = graph([node("a", 0, 0), node("room", 0.5, 0.5, "room"), node("b", 1, 1), node("corridor", 0.5, 0)],
    [edge("a", "room"), edge("room", "b"), edge("a", "corridor", 5), edge("corridor", "b", 5)]);
  const route = computeCampusRoute(g, "a", "b");
  assert.ok(route.ok); assert.equal(route.route.durationSeconds, 10);
  const endpoint = computeCampusRoute(g, "room", "b"); assert.ok(endpoint.ok); assert.equal(endpoint.route.durationSeconds, 1);
});

test("blocked interiors are avoided but the destination's own doorway is allowed", () => {
  const nodes = [node("a", 0.1, 0.5), node("b", 0.9, 0.5), node("corridor", 0.5, 0.1)];
  const region = { building: "ГК", floor: 1, left: 0.4, top: 0.4, right: 0.6, bottom: 0.6 };
  const g = graph(nodes, [edge("a", "b"), edge("a", "corridor", 5), edge("corridor", "b", 5)], [region]);
  const route = computeCampusRoute(g, "a", "b"); assert.ok(route.ok); assert.equal(route.route.durationSeconds, 10);
  const own = graph([node("door", 0.1, 0.5), node("room", 0.5, 0.5, "room")], [edge("door", "room")], [{ ...region, owner: "room" }]);
  assert.equal(computeCampusRoute(own, "door", "room").ok, true);
});

test("reverse stairs produce the actual destination floor and Russian instructions", () => {
  const g = graph([node("a", 0.2, 0.2, "stair", { group: "s" }),
    node("b", 0.2, 0.2, "stair", { group: "s", floor: 2 })], [edge("a", "b", 30, { kind: "stair_up" })]);
  const route = computeCampusRoute(g, "b", "a"); assert.ok(route.ok);
  assert.equal(route.route.legs[0].kind, "stair_down");
  assert.deepEqual(route.route.steps, [{ instruction: "Спуститесь на 1 этаж", building: "ГК", floor: 1 }]);
});

test("equal-time routing prefers fewer stairs instead of an arbitrary floor detour", () => {
  const g = graph([node("a", 0, 0, "building_link"), node("b", 1, 1, "building_link", { floor: 2 }), node("s1", 0.1, 0.1, "stair", { group: "s" }),
    node("s2", 0.1, 0.1, "stair", { group: "s", floor: 2 })],
  [edge("a", "s1"), edge("s1", "s2", 1, { kind: "stair_up" }),
    edge("s2", "b", 1), edge("a", "b", 3, { kind: "building_link" })]);
  const route = computeCampusRoute(g, "a", "b"); assert.ok(route.ok);
  assert.equal(route.route.legs.length, 1);
});

test("schedule classroom notation resolves buildings without choosing ambiguous rooms", () => {
  const g = graph([node("g", 0, 0, "room", { room: "101" }),
    node("u", 1, 1, "room", { room: "101", building: "УЛК" }),
    node("vc", 0.3, 0.3, "room", { room: "243" })], []);
  assert.equal(resolveCampusClassroom(g, "101;")?.id, "g");
  assert.equal(resolveCampusClassroom(g, "101*;")?.id, "u");
  assert.equal(resolveCampusClassroom(g, "ВЦ 243")?.id, "vc");
  assert.equal(resolveCampusClassroom(g, "u")?.id, "u");
  assert.equal(resolveCampusClassroom(g, "999"), null);
});

test("place search matches independent words and hides transit nodes", () => {
  const g = graph([node("room", 0, 0, "room", { room: "101", label: "Лаборатория физики" }),
    node("door", 1, 1, "entrance", { label: "Главный вход" }), node("junction", 0.5, 0.5)], []);
  assert.deepEqual(campusPlaces(g, "физики 101").map(x => x.id), ["room"]);
  assert.deepEqual(campusPlaces(g, "главный вход").map(x => x.id), ["door"]);
  assert.equal(campusPlaces(g, "", "УЛК").length, 0);
  assert.equal(campusPlaces(g).length, 2);
});

test("the packaged graph follows the same six authored routes as the native client", () => {
  const g = parseCampusGraph(JSON.parse(readFileSync(new URL("../../src/Vograph.Desktop/Assets/maps/campus-graph.json", import.meta.url), "utf8")));
  for (const [from, to, kind] of [["ulk.entrance.main", "ulk.room.320", "walk"],
    ["gk.room.493", "gk.room.401", "stair_down"], ["ulk.room.320", "gk.room.493", "building_link"],
    ["ulk.room.320", "ulk.room.325", "walk"], ["ulk.room.507", "ulk.room.320", "stair_down"],
    ["gk.entrance.main", "gk.room.493", "stair_up"]]) {
    const route = computeCampusRoute(g, from, to); assert.ok(route.ok, `${from} → ${to}`);
    assert.ok(route.route.legs.some(x => x.kind === kind));
  }
});
