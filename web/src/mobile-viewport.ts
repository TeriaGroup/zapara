export function mobileViewportBaseline(previous: { width: number; height: number }, width: number, height: number, editing: boolean) {
  if (editing) return previous;
  return { width, height: Math.abs(width - previous.width) > 80 ? height : Math.max(previous.height, height) };
}

export function mobileViewportFrame(input: {
  layoutHeight: number; visibleHeight?: number; offsetTop?: number; baselineHeight: number; editing: boolean; scale?: number;
}): { top: number; bottom: number; keyboardOpen: boolean } {
  const layout = Math.max(0, Number.isFinite(input.layoutHeight) ? input.layoutHeight : 0);
  if (input.scale !== undefined && (!Number.isFinite(input.scale) || Math.abs(input.scale - 1) > 0.05))
    return { top: 0, bottom: 0, keyboardOpen: false };
  const visible = Math.min(layout, Math.max(0, Number.isFinite(input.visibleHeight) ? input.visibleHeight! : layout));
  const top = Math.min(layout - visible, Math.max(0, Number.isFinite(input.offsetTop) ? input.offsetTop! : 0));
  const keyboardOpen = input.editing && Number.isFinite(input.baselineHeight) && input.baselineHeight - visible > 120;
  return { top: keyboardOpen ? top : 0, bottom: keyboardOpen ? layout - top - visible : 0, keyboardOpen };
}
