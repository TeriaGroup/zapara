export type StudyInterval = { start: string; end: string };
export type FreeStudyInterval = StudyInterval & { minutes: number };
export type TransferAssessment = { status: 'unknown' | 'overlap' | 'tight' | 'fits'; availableSeconds: number | null; routeSeconds: number | null };
const minutes = (value: string): number | null => {
  const match = /^(\d{1,2}):(\d{2})$/.exec(value);
  return match && +match[1] < 24 && +match[2] < 60 ? +match[1] * 60 + +match[2] : null;
};
const time = (value: number) => `${Math.floor(value / 60).toString().padStart(2, '0')}:${(value % 60).toString().padStart(2, '0')}`;

/** Only common gaps inside both known study-day spans, never a guessed all-day availability. */
export function commonFreeIntervals(first: StudyInterval[], second: StudyInterval[], minimumMinutes = 15): FreeStudyInterval[] {
  if (!first.length || !second.length || !Number.isInteger(minimumMinutes) || minimumMinutes < 1) return [];
  const convert = (rows: StudyInterval[]) => rows.map(row => ({ start: minutes(row.start), end: minutes(row.end) }));
  const a = convert(first), b = convert(second);
  if ([...a, ...b].some(row => row.start === null || row.end === null || row.end <= row.start)) return [];
  const validA = a as { start: number; end: number }[], validB = b as { start: number; end: number }[];
  const low = Math.max(Math.min(...validA.map(row => row.start)), Math.min(...validB.map(row => row.start)));
  const high = Math.min(Math.max(...validA.map(row => row.end)), Math.max(...validB.map(row => row.end)));
  if (high <= low) return [];
  const busy = [...validA, ...validB].sort((x, y) => x.start - y.start || x.end - y.end);
  const result: FreeStudyInterval[] = []; let cursor = low;
  for (const row of busy) {
    if (row.end <= low || row.start >= high) continue;
    const start = Math.max(low, row.start), end = Math.min(high, row.end);
    if (start - cursor >= minimumMinutes) result.push({ start: time(cursor), end: time(start), minutes: start - cursor });
    cursor = Math.max(cursor, end);
  }
  if (high - cursor >= minimumMinutes) result.push({ start: time(cursor), end: time(high), minutes: high - cursor });
  return result;
}

export function assessTransfer(previousEnd: string, nextStart: string, routeSeconds: number | null): TransferAssessment {
  const end = minutes(previousEnd), start = minutes(nextStart);
  const route = routeSeconds !== null && Number.isFinite(routeSeconds) && routeSeconds >= 0 ? routeSeconds : null;
  if (end === null || start === null) return { status: 'unknown', availableSeconds: null, routeSeconds: route };
  const availableSeconds = (start - end) * 60;
  return { status: availableSeconds < 0 ? 'overlap' : route === null ? 'unknown' : route > availableSeconds ? 'tight' : 'fits', availableSeconds, routeSeconds: route };
}
