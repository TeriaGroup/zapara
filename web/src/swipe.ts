import { PointerEvent as ReactPointerEvent, useEffect, useLayoutEffect, useRef } from "react";
import { PointerGesture } from "./gesture";

const excluded = "button, a, input, textarea, select, label, audio, video, iframe, canvas, [role=button], [role=slider], [role=link], [contenteditable]:not([contenteditable=false])";
function blockedTarget(event: ReactPointerEvent<HTMLElement>, axis: "horizontal" | "vertical") {
  const target = event.target instanceof Element ? event.target : null;
  if (!target || target.closest(excluded)) return true;
  for (let node: Element | null = target; node; node = node.parentElement) {
    if (axis === "horizontal" && node instanceof HTMLElement && node.scrollWidth > node.clientWidth + 1 && /auto|scroll/.test(getComputedStyle(node).overflowX)) return true;
    if (node === event.currentTarget) break;
  }
  return false;
}
function usePointerGesture(axis: "horizontal" | "vertical", onNegative: () => void, onPositive: () => void, resetKey?: string) {
  const gesture = useRef(new PointerGesture(axis));
  const surface = useRef<HTMLElement | null>(null);
  const activePointers = useRef(new Set<number>());
  useLayoutEffect(() => { gesture.current.cancel(); }, [resetKey]);
  useEffect(() => {
    const cancel = () => { gesture.current.cancel(); activePointers.current.clear(); };
    const anotherPointer = (event: PointerEvent) => {
      if (!["touch", "pen"].includes(event.pointerType)) return;
      activePointers.current.add(event.pointerId);
      if (!event.isPrimary || activePointers.current.size > 1) gesture.current.cancel();
    };
    const release = (event: PointerEvent) => { activePointers.current.delete(event.pointerId); gesture.current.cancel(); };
    window.addEventListener("pointerdown", anotherPointer, true);
    window.addEventListener("blur", cancel);
    window.addEventListener("pointerup", release);
    window.addEventListener("pointercancel", release);
    return () => { cancel(); window.removeEventListener("pointerdown", anotherPointer, true); window.removeEventListener("blur", cancel); window.removeEventListener("pointerup", release); window.removeEventListener("pointercancel", release); };
  }, []);
  useLayoutEffect(() => {
    const root = surface.current;
    if (!root || axis !== "horizontal") return;
    const update = () => {
      const hasHorizontalScroll = [root, ...root.querySelectorAll<HTMLElement>("*")].some(node =>
        node.scrollWidth > node.clientWidth + 1 && /auto|scroll/.test(getComputedStyle(node).overflowX));
      root.style.touchAction = hasHorizontalScroll ? "auto" : "pan-y pinch-zoom";
      if (hasHorizontalScroll) gesture.current.cancel();
    };
    update();
    const resize = new ResizeObserver(update);
    resize.observe(root);
    const mutations = new MutationObserver(update);
    mutations.observe(root, { childList: true, subtree: true, characterData: true });
    return () => { resize.disconnect(); mutations.disconnect(); root.style.removeProperty("touch-action"); };
  });
  const point = (event: ReactPointerEvent<HTMLElement>) => ({ id: event.pointerId, x: event.clientX, y: event.clientY, type: event.pointerType, primary: event.isPrimary });
  return {
    ref: (node: HTMLElement | null) => { surface.current = node; },
    onPointerDown(event: ReactPointerEvent<HTMLElement>) {
      if (activePointers.current.size > 1 || blockedTarget(event, axis) || event.clientX <= 24 || event.clientX >= window.innerWidth - 24) { gesture.current.cancel(); return; }
      gesture.current.start(point(event));
    },
    onPointerMove(event: ReactPointerEvent<HTMLElement>) { gesture.current.move(point(event)); },
    onPointerUp(event: ReactPointerEvent<HTMLElement>) {
      const result = gesture.current.finish(point(event));
      if (result === "negative") onNegative(); else if (result === "positive") onPositive();
    },
    onPointerCancel() { gesture.current.cancel(); },
    onLostPointerCapture() { gesture.current.cancel(); },
  };
}
export function useSwipe(onNext: () => void, onPrev: () => void, dateKey: string) {
  return usePointerGesture("horizontal", onNext, onPrev, dateKey);
}
export function useSheetDismiss(onClose: () => void) {
  return usePointerGesture("vertical", () => {}, onClose);
}
export function useDateReveal(dateKey: string) {
  const ref = useRef<HTMLDivElement | null>(null);
  const previous = useRef(dateKey);
  const alternate = useRef(false);
  useLayoutEffect(() => {
    if (previous.current === dateKey) return;
    previous.current = dateKey;
    alternate.current = !alternate.current;
    ref.current?.setAttribute("data-date-reveal", alternate.current ? "a" : "b");
  }, [dateKey]);
  return ref;
}
