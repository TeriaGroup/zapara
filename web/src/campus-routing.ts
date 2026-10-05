/** Uses the public campus graph and the same routing rules as Vograph.Core.Campus. */
export type CampusPoint = [number, number];
export type CampusNode = {
  id: string; kind: string; building: string; floor: number; x: number; y: number;
  label?: string; room?: string; group?: string;
};
export type CampusEdge = { from: string; to: string; kind: string; seconds: number; oneWay: boolean; points?: CampusPoint[] };
export type CampusBlocked = { building: string; floor: number; left: number; top: number; right: number; bottom: number; owner?: string };
export type CampusGraph = { version: 1; buildings: string[]; nodes: CampusNode[]; edges: CampusEdge[]; blocked: CampusBlocked[] };
export type CampusLeg = {
  kind: string; building: string; floor: number; toBuilding?: string; toFloor?: number;
  fromId: string; toId: string; points: CampusPoint[];
};
export type CampusStep = { instruction: string; building: string; floor: number };
export type CampusRoute = { fromId: string; toId: string; durationSeconds: number; legs: CampusLeg[]; steps: CampusStep[] };
export type CampusRouteResult = { ok: true; route: CampusRoute } | { ok: false; failure: "unknown_place" | "unreachable" };
export class CampusRoutingError extends Error {
  readonly code: string;
  constructor(code: string) { super("Не удалось прочитать данные маршрутов кампуса."); this.code = code; }
}

const fail = (code = "invalid_json"): never => { throw new CampusRoutingError(code); };
const obj = (value: unknown): Record<string, unknown> => {
  if (!value || typeof value !== "object" || Array.isArray(value)) return fail();
  return value as Record<string, unknown>;
};
const list = (value: unknown): unknown[] => Array.isArray(value) ? value : fail();
const text = (value: unknown): string => typeof value === "string" ? value : fail();
const number = (value: unknown): number => typeof value === "number" && Number.isFinite(value) ? value : fail();
const optionalText = (value: unknown): string | undefined => value == null ? undefined : text(value);
const coordinate = (value: unknown): number => { const n = number(value); return n >= 0 && n <= 1 ? n : fail(); };
const floorNumber = (value: unknown): number => { const n = number(value); return Number.isInteger(n) && n >= 1 && n <= 5 ? n : fail(); };
const nodeKinds = new Set(["entrance", "room", "stair", "landing", "junction", "building_link"]);
const edgeKinds = new Set(["walk", "stair_up", "stair_down", "building_link"]);
const verticalKinds = new Set(["stair", "landing"]);

export function parseCampusGraph(value: unknown): CampusGraph {
  const root = obj(value);
  if (root.version !== 1) fail();
  const buildings = list(root.buildings).map(text);
  if (buildings.some(x => x !== "ГК" && x !== "УЛК") || new Set(buildings).size !== buildings.length) fail();
  const nodes: CampusNode[] = list(root.nodes).map(value => {
    const n = obj(value), id = text(n.id), kind = text(n.kind), building = text(n.building);
    if (!id.trim() || !nodeKinds.has(kind) || !buildings.includes(building)) fail();
    return { id, kind, building, floor: floorNumber(n.floor), x: coordinate(n.x), y: coordinate(n.y),
      label: optionalText(n.label), room: optionalText(n.room), group: optionalText(n.group) };
  });
  const byId = new Map(nodes.map(n => [n.id, n]));
  if (byId.size !== nodes.length) fail();
  const edges: CampusEdge[] = list(root.edges).map(value => {
    const e = obj(value), fromId = text(e.from), toId = text(e.to), kind = text(e.kind), seconds = number(e.seconds);
    const oneWay = typeof e.oneWay === "boolean" ? e.oneWay : fail();
    if (!edgeKinds.has(kind) || seconds <= 0) fail();
    const from = byId.get(fromId), to = byId.get(toId);
    if (!from || !to) return fail("unknown_node");
    if (kind === "walk" && (from.building !== to.building || from.floor !== to.floor)) fail("bad_edge_floor");
    if (kind === "stair_up" || kind === "stair_down") {
      if (from.building !== to.building || !verticalKinds.has(from.kind) || !verticalKinds.has(to.kind)
        || !from.group?.trim() || from.group !== to.group || to.floor - from.floor !== (kind === "stair_up" ? 1 : -1)) fail("bad_stair");
    }
    if (kind === "building_link" && (from.kind !== "building_link" || to.kind !== "building_link")) fail();
    const points: CampusPoint[] | undefined = e.points == null ? undefined : list(e.points).map(value => {
      const p = list(value); if (p.length !== 2) return fail(); return [coordinate(p[0]), coordinate(p[1])];
    });
    if (points?.length) {
      const matches = (p: CampusPoint, n: CampusNode) => Math.abs(p[0] - n.x) <= 0.000001 && Math.abs(p[1] - n.y) <= 0.000001;
      if (!matches(points[0], from) || !matches(points[points.length - 1], to)) fail("bad_edge_geometry");
    }
    return { from: fromId, to: toId, kind, seconds, oneWay, points };
  });
  const blocked: CampusBlocked[] = root.blocked == null ? [] : list(root.blocked).map(value => {
    const b = obj(value), building = text(b.building), floor = floorNumber(b.floor);
    const left = coordinate(b.left), top = coordinate(b.top), right = coordinate(b.right), bottom = coordinate(b.bottom), owner = optionalText(b.owner);
    if (!buildings.includes(building) || left >= right || top >= bottom || (owner !== undefined && !byId.has(owner))) fail();
    return { building, floor, left, top, right, bottom, owner };
  });
  return { version: 1, buildings, nodes, edges, blocked };
}

const normalize = (value: string) => value.toLocaleLowerCase("ru").replaceAll("ё", "е").trim();
export function campusPlaces(graph: CampusGraph, query = "", building?: string, floor?: number): CampusNode[] {
  const words = normalize(query).split(/\s+/).filter(Boolean);
  return graph.nodes.filter(n => (n.kind === "room" || n.kind === "entrance")
    && (!building || n.building === building) && (floor === undefined || n.floor === floor)
    && words.every(word => normalize(`${n.label ?? ""} ${n.room ?? ""} ${n.building} ${n.floor} ${n.id}`).includes(word)))
    .sort((a, b) => (a.kind === "entrance" ? 0 : 1) - (b.kind === "entrance" ? 0 : 1)
      || (a.room ?? a.label ?? a.id).localeCompare(b.room ?? b.label ?? b.id, "ru", { numeric: true }) || ordinal(a.id, b.id));
}

export function resolveCampusClassroom(graph: CampusGraph, classroom: string | null | undefined): CampusNode | null {
  if (!classroom?.trim()) return null;
  const raw = classroom.trim().replace(/;$/, "").trim();
  const exact = graph.nodes.find(n => n.id === raw);
  if (exact) return exact;
  const building = /ВЦ/i.test(raw) || !raw.includes("*") ? "ГК" : "УЛК";
  const key = raw.replaceAll("*", "").trim().replace(/^ВЦ\s*([0-9]+[а-яa-z]?)$/i, "$1");
  const matches = graph.nodes.filter(n => n.kind === "room" && n.building === building && n.room?.toLowerCase() === key.toLowerCase());
  return matches.length === 1 ? matches[0] : null;
}

function pointsFor(edge: CampusEdge, from: CampusNode, to: CampusNode): CampusPoint[] {
  return edge.points?.length ? edge.points.map(p => [...p] as CampusPoint) : [[from.x, from.y], [to.x, to.y]];
}
function walkBlocked(edge: CampusEdge, from: CampusNode, to: CampusNode, regions: CampusBlocked[]): boolean {
  if (edge.kind !== "walk") return false;
  const points = pointsFor(edge, from, to);
  return regions.some(b => {
    if (b.building !== from.building || b.floor !== from.floor || b.owner === from.id || b.owner === to.id) return false;
    for (let i = 0; i < points.length - 1; i++) {
      const a = points[i], c = points[i + 1];
      for (let s = 1; s < 100; s++) {
        const x = a[0] + (c[0] - a[0]) * s / 100, y = a[1] + (c[1] - a[1]) * s / 100;
        if (x > b.left && x < b.right && y > b.top && y < b.bottom) return true;
      }
    }
    return false;
  });
}
type Cost = { seconds: number; stairs: number };
type QueueEntry = Cost & { id: string };
const ordinal = (a: string, b: string) => a < b ? -1 : a > b ? 1 : 0;
const compareCost = (a: Cost, b: Cost) => a.seconds - b.seconds || a.stairs - b.stairs;
const compareQueue = (a: QueueEntry, b: QueueEntry) => compareCost(a, b) || ordinal(a.id, b.id);
// A binary heap keeps authored campus graphs practical without scanning all nodes on every step.
function push(heap: QueueEntry[], entry: QueueEntry) {
  heap.push(entry); let i = heap.length - 1;
  while (i > 0) { const p = (i - 1) >> 1; if (compareQueue(heap[p], entry) <= 0) break; heap[i] = heap[p]; i = p; }
  heap[i] = entry;
}
function pop(heap: QueueEntry[]): QueueEntry | undefined {
  if (!heap.length) return undefined;
  const first = heap[0], last = heap.pop()!;
  if (heap.length) {
    let i = 0;
    while (i * 2 + 1 < heap.length) {
      let c = i * 2 + 1; if (c + 1 < heap.length && compareQueue(heap[c + 1], heap[c]) < 0) c++;
      if (compareQueue(last, heap[c]) <= 0) break; heap[i] = heap[c]; i = c;
    }
    heap[i] = last;
  }
  return first;
}

export function computeCampusRoute(graph: CampusGraph, fromId: string, toId: string): CampusRouteResult {
  const nodes = new Map(graph.nodes.map(n => [n.id, n]));
  if (!nodes.has(fromId) || !nodes.has(toId)) return { ok: false, failure: "unknown_place" };
  if (fromId === toId) return { ok: true, route: { fromId, toId, durationSeconds: 0, legs: [], steps: [] } };
  type Link = { to: string; edge: CampusEdge; reverse: boolean };
  const adjacency = new Map<string, Link[]>();
  const add = (from: string, link: Link) => { const links = adjacency.get(from) ?? []; links.push(link); adjacency.set(from, links); };
  for (const edge of graph.edges) {
    const from = nodes.get(edge.from), to = nodes.get(edge.to);
    if (!from || !to || walkBlocked(edge, from, to, graph.blocked)) continue;
    add(from.id, { to: to.id, edge, reverse: false });
    if (!edge.oneWay) add(to.id, { to: from.id, edge, reverse: true });
  }
  const scores = new Map<string, Cost>([[fromId, { seconds: 0, stairs: 0 }]]);
  const previous = new Map<string, { fromId: string; link: Link }>();
  const closed = new Set<string>(), heap: QueueEntry[] = [];
  push(heap, { id: fromId, seconds: 0, stairs: 0 });
  while (heap.length) {
    const current = pop(heap)!;
    if (closed.has(current.id)) continue;
    closed.add(current.id);
    if (current.id === toId) {
      const legs: CampusLeg[] = [];
      for (let id = toId; id !== fromId;) {
        const step = previous.get(id)!, edge = step.link.edge;
        const from = nodes.get(step.fromId)!, to = nodes.get(id)!;
        const kind = step.link.reverse && edge.kind === "stair_up" ? "stair_down"
          : step.link.reverse && edge.kind === "stair_down" ? "stair_up" : edge.kind;
        const points = pointsFor(edge, nodes.get(edge.from)!, nodes.get(edge.to)!);
        if (step.link.reverse) points.reverse();
        legs.push({ kind, building: from.building, floor: from.floor, fromId: from.id, toId: to.id, points,
          ...(kind === "walk" ? {} : { toBuilding: to.building, toFloor: to.floor }) });
        id = step.fromId;
      }
      legs.reverse();
      return { ok: true, route: { fromId, toId, durationSeconds: Math.round(scores.get(toId)!.seconds), legs, steps: routeSteps(legs) } };
    }
    if (current.id !== fromId && nodes.get(current.id)!.kind === "room") continue;
    const cost = scores.get(current.id)!;
    for (const link of adjacency.get(current.id) ?? []) {
      if (closed.has(link.to)) continue;
      const next = { seconds: cost.seconds + link.edge.seconds, stairs: cost.stairs + (link.edge.kind === "stair_up" || link.edge.kind === "stair_down" ? 1 : 0) };
      const known = scores.get(link.to);
      if (known && compareCost(next, known) >= 0) continue;
      scores.set(link.to, next); previous.set(link.to, { fromId: current.id, link }); push(heap, { id: link.to, ...next });
    }
  }
  return { ok: false, failure: "unreachable" };
}

function routeSteps(legs: CampusLeg[]): CampusStep[] {
  const steps: CampusStep[] = []; let previous: CampusLeg | undefined;
  for (const leg of legs) {
    const continues = leg.kind === "walk" && previous?.kind === "walk" && leg.building === previous.building && leg.floor === previous.floor;
    previous = leg; if (continues) continue;
    const floor = leg.toFloor ?? leg.floor, building = leg.toBuilding ?? leg.building;
    const instruction = leg.kind === "stair_up" ? `Поднимитесь на ${floor} этаж`
      : leg.kind === "stair_down" ? `Спуститесь на ${floor} этаж`
      : leg.kind === "building_link" ? `Перейдите в корпус ${building}, ${floor} этаж`
      : `Пройдите по коридору (${leg.floor} этаж)`;
    steps.push({ instruction, building, floor });
  }
  return steps;
}
