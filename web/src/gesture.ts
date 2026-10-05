type Point = { id: number; x: number; y: number; type: string; primary: boolean };
export class PointerGesture {
  private origin: Point | null = null;
  private locked = false;
  private axis: "horizontal" | "vertical";
  constructor(axis: "horizontal" | "vertical") { this.axis = axis; }
  cancel() { this.origin = null; this.locked = false; }
  start(point: Point) {
    if (this.origin || !point.primary || !["touch", "pen"].includes(point.type)) { this.cancel(); return; }
    this.origin = point;
  }
  move(point: Point) {
    if (!this.origin) return;
    if (point.id !== this.origin.id) { this.cancel(); return; }
    const dx = Math.abs(point.x - this.origin.x), dy = Math.abs(point.y - this.origin.y);
    const along = this.axis === "horizontal" ? dx : dy, across = this.axis === "horizontal" ? dy : dx;
    if (!this.locked && Math.max(along, across) >= 10) {
      if (along <= across * 1.25) this.cancel(); else this.locked = true;
    }
  }
  finish(point: Point): "negative" | "positive" | null {
    this.move(point);
    const origin = this.origin;
    this.cancel();
    if (!origin) return null;
    const dx = point.x - origin.x, dy = point.y - origin.y;
    const along = this.axis === "horizontal" ? dx : dy, across = this.axis === "horizontal" ? dy : dx;
    if (Math.abs(along) < 64 || Math.abs(along) <= Math.abs(across) * 1.25) return null;
    return along < 0 ? "negative" : "positive";
  }
}
