import { createContext, useContext, useReducer, useRef, type ReactNode } from "react";
import { PersonalComposerStore } from "./personal-composer";
import { useApp } from "./store";

const Context = createContext<{ store: PersonalComposerStore; refresh: () => void } | null>(null);
export function PersonalComposerProvider({ children }: { children: ReactNode }) {
  const app = useApp();
  const owner = app.session?.authenticated ? app.session.user?.userId ?? "account" : "guest";
  const store = useRef(new PersonalComposerStore(owner)).current;
  // Clear before route children read the new identity, including A → B → A.
  store.scope(owner);
  const [, refresh] = useReducer(value => value + 1, 0);
  return <Context.Provider key={owner} value={{ store, refresh }}>{children}</Context.Provider>;
}
export function usePersonalComposer(conversationId: string) {
  const context = useContext(Context);
  if (!context) throw new Error("PersonalComposerProvider missing");
  return { ...context, composer: context.store.read(conversationId) };
}
export function usePersonalDrafts() {
  const context=useContext(Context);
  if(!context)throw new Error("PersonalComposerProvider missing");
  return context.store.inboxDrafts();
}
