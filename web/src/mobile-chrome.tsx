import { createContext, useContext, useEffect, useLayoutEffect, useState, type Dispatch, type SetStateAction } from "react";
import { mobileViewportBaseline, mobileViewportFrame } from "./mobile-viewport";

export type MobileChrome = {
  compact: boolean;
  actionsHost: HTMLElement | null;
  setTitle: Dispatch<SetStateAction<string | null>>;
};

export const MobileChromeContext = createContext<MobileChrome | null>(null);

export function useCompactLayout() {
  const [compact, setCompact] = useState(() => window.matchMedia("(max-width: 959px)").matches);
  useEffect(() => {
    const query = window.matchMedia("(max-width: 959px)");
    const update = () => setCompact(query.matches);
    update();
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);
  return compact;
}

export function useMobileScreenTitle(title: string | null) {
  const chrome = useContext(MobileChromeContext);
  const setTitle = chrome?.setTitle;
  useLayoutEffect(() => {
    if (!setTitle || !title) return;
    setTitle(title);
    return () => setTitle(null);
  }, [setTitle, title]);
  return chrome;
}

export function useMobileKeyboard(compact: boolean) {
  const [open, setOpen] = useState(false);
  useLayoutEffect(() => {
    const viewport = window.visualViewport;
    let baseline = { width: window.innerWidth, height: window.innerHeight };
    const update = () => {
      const active = document.activeElement;
      const editing = active instanceof HTMLElement && active.matches("textarea,input,[contenteditable=true]") &&
        !(active instanceof HTMLInputElement && ["button", "submit", "reset", "checkbox", "radio", "range", "color", "file", "hidden"].includes(active.type)) &&
        !(active instanceof HTMLInputElement || active instanceof HTMLTextAreaElement ? active.readOnly || active.disabled : false);
      baseline = mobileViewportBaseline(baseline, window.innerWidth, window.innerHeight, editing);
      const frame = mobileViewportFrame({ layoutHeight: window.innerHeight, visibleHeight: viewport?.height,
        offsetTop: viewport?.offsetTop, scale: viewport?.scale, baselineHeight: baseline.height, editing: compact && editing });
      document.documentElement.style.setProperty("--mobile-viewport-top", `${frame.top}px`);
      document.documentElement.style.setProperty("--mobile-keyboard-inset", `${frame.bottom}px`);
      setOpen(frame.keyboardOpen);
    };
    update();
    window.addEventListener("resize", update);
    viewport?.addEventListener("resize", update);
    viewport?.addEventListener("scroll", update);
    document.addEventListener("focusin", update);
    document.addEventListener("focusout", update);
    return () => {
      window.removeEventListener("resize", update);
      viewport?.removeEventListener("resize", update);
      viewport?.removeEventListener("scroll", update);
      document.removeEventListener("focusin", update);
      document.removeEventListener("focusout", update);
      document.documentElement.style.removeProperty("--mobile-viewport-top");
      document.documentElement.style.removeProperty("--mobile-keyboard-inset");
    };
  }, [compact]);
  return open;
}
