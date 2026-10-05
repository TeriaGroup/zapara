export function normalizeIntersectionStrictness(value: unknown): number {
  const stored = typeof value === "number" || typeof value === "string" ? Number(value) : NaN;
  if (!Number.isFinite(stored)) return 50;
  const clamped = Math.max(50, Math.min(100, stored));
  return 50 + Math.round((clamped - 50) / 25) * 25;
}
