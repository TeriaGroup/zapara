export function createSessionRefresher<T>(
  generation: () => number,
  load: () => Promise<T>,
  apply: (value: T) => void,
  fail: (error: unknown) => void,
): () => Promise<void> {
  let serial = 0;
  let active: { generation: number; promise: Promise<void> } | null = null;
  return () => {
    const currentGeneration = generation();
    if (active?.generation === currentGeneration) return active.promise;
    const ticket = ++serial;
    const promise = load().then(value => {
      if (ticket === serial && currentGeneration === generation()) apply(value);
    }).catch(error => {
      if (ticket !== serial || currentGeneration !== generation() || error instanceof Error && error.message === "stale-session") return;
      fail(error);
      throw error;
    }).finally(() => { if (active?.promise === promise) active = null; });
    active = { generation: currentGeneration, promise };
    return promise;
  };
}
