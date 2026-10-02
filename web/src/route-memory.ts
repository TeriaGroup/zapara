export type RememberedRoute = { fromId: string; toId: string };
export function rememberRoute(rows: RememberedRoute[], next: RememberedRoute): RememberedRoute[] {
  if(!next.fromId||!next.toId||next.fromId===next.toId)return rows;
  return [next,...rows.filter(row=>row.fromId!==next.fromId||row.toId!==next.toId)].slice(0,6);
}
export function validRouteHistory(rows: RememberedRoute[], places: ReadonlySet<string>): RememberedRoute[] {
  return rows.filter(row=>places.has(row.fromId)&&places.has(row.toId)).slice(0,6);
}
export function togglePinnedPlace(rows: string[], id: string): string[] {
  return rows.includes(id)?rows.filter(value=>value!==id):rows.length<8?[...rows,id]:rows;
}
export function validPinnedPlaces(rows: string[], places: ReadonlySet<string>): string[] {
  return [...new Set(rows)].filter(id=>places.has(id)).slice(0,8);
}
