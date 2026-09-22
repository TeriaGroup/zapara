import { PointerEvent as ReactPointerEvent, useRef } from "react";

export function useSwipe(onNext: () => void, onPrev: () => void) {
  const start = useRef<{ x: number; y: number } | null>(null);
  return {
    onPointerDown(event: ReactPointerEvent<HTMLElement>) {
      if (event.pointerType === "mouse" && event.button !== 0) return;
      const target = event.target as HTMLElement | null;
      if (target?.closest("button, a, input, textarea, select, label")) return;
      start.current = { x: event.clientX, y: event.clientY };
    },
    onPointerUp(event: ReactPointerEvent<HTMLElement>) {
      if (!start.current) return;
      const dx = event.clientX - start.current.x;
      const dy = event.clientY - start.current.y;
      start.current = null;
      if (Math.abs(dx) < 64 || Math.abs(dx) < Math.abs(dy)) return;
      if (dx < 0) onNext();
      else onPrev();
    },
    onPointerCancel() {
      start.current = null;
    },
  };
}
