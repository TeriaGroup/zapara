export function mergeDevicePages<T extends { familyId: string }>(previous: T[], incoming: T[]): T[] {
  const rows = new Map(previous.map(row => [row.familyId, row]));
  incoming.forEach(row => rows.set(row.familyId, row));
  return [...rows.values()];
}
export function resetRequestReady(username: string): boolean { return /^[A-Za-z0-9_.-]{3,32}$/.test(username.trim()); }
export function resetConfirmationReady(token: string, password: string): boolean {
  const size = Array.from(password).length;
  return /^[A-Za-z0-9_-]{43,128}$/.test(token.trim()) && size >= 12 && size <= 128 && !/[\u0000-\u001f\u007f]/.test(password);
}
