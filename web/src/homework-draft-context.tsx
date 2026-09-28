import { createContext, useContext, useEffect, useReducer, useRef, type ReactNode } from "react";
import { HomeworkDraftController, type HomeworkDraft } from "./homework-draft";
import { useApp } from "./store";
import type { HomeworkItem } from "./types";

const HomeworkDraftContext = createContext<{ controller: HomeworkDraftController; refresh: () => void; readLocal: (id: string) => HomeworkItem | undefined } | null>(null);
export function HomeworkDraftProvider({ children }: { children: ReactNode }) {
  const app = useApp();
  const scopeId = JSON.stringify([app.session?.authenticated ? app.session.user?.userId ?? "account" : "guest", app.groupId]);
  const controller = useRef(new HomeworkDraftController(scopeId)).current;
  const homework = useRef(app.homework);
  homework.current = app.homework;
  // Reset before children read the new identity, including A → B → A transitions.
  controller.scope(scopeId);
  const [, refresh] = useReducer(value => value + 1, 0);
  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => {
      if (!controller.dirty) return;
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [controller]);
  return <HomeworkDraftContext.Provider value={{ controller, refresh, readLocal: id => homework.current.find(item => item.id === id) }}>{children}</HomeworkDraftContext.Provider>;
}
export function useHomeworkDraft() {
  const context = useContext(HomeworkDraftContext);
  if (!context) throw new Error("HomeworkDraftProvider missing");
  const { controller, refresh, readLocal } = context;
  function field<K extends keyof HomeworkDraft>(name: K, value: HomeworkDraft[K]) { controller.field(name, value); refresh(); }
  return { controller, refresh, readLocal, field, draft: controller.draft, busy: controller.busy, note: controller.note };
}
