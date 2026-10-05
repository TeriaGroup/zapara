import { useEffect, useState } from "react";
const pending = new Map<string, Promise<unknown>>(), listeners = new Map<string, Set<() => void>>();
const notify = (key: string) => listeners.get(key)?.forEach(listener => listener());
export const hasPendingCreation = (key: string) => pending.has(key);
export function submitCreationOnce<T>(key: string, action: () => Promise<T>): Promise<T> | null { if (pending.has(key))
    return null; const operation = Promise.resolve().then(action); pending.set(key, operation); notify(key); void operation.finally(() => { if (pending.get(key) === operation) {
    pending.delete(key);
    notify(key);
} }).catch(() => undefined); return operation; }
export function subscribeCreation(key: string, callback: () => void) { if (!listeners.has(key))
    listeners.set(key, new Set()); listeners.get(key)!.add(callback); return () => { listeners.get(key)?.delete(callback); }; }
export function usePendingCreation(key: string) { const [value, setValue] = useState(() => hasPendingCreation(key)); useEffect(() => { setValue(hasPendingCreation(key)); return subscribeCreation(key, () => setValue(hasPendingCreation(key))); }, [key]); return value; }
