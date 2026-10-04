export function startVisibleRefresh(
  refresh: () => Promise<void>,
  visibility: Pick<Document, "hidden" | "addEventListener" | "removeEventListener">,
  windowHost: Pick<Window, "addEventListener" | "removeEventListener" | "setInterval" | "clearInterval">,
  interval = 10_000,
): () => void {
  let disposed = false;
  let running = false;
  let queued = false;

  async function run() {
    if (disposed || visibility.hidden) return;
    if (running) {
      queued = true;
      return;
    }
    running = true;
    try {
      do {
        queued = false;
        await refresh();
      } while (!disposed && !visibility.hidden && queued);
    } finally {
      running = false;
    }
  }

  const visible = () => { if (!visibility.hidden) void run(); };
  const timer = windowHost.setInterval(visible, interval);
  visibility.addEventListener("visibilitychange", visible);
  windowHost.addEventListener("focus", visible);
  windowHost.addEventListener("online", visible);
  visible();

  return () => {
    disposed = true;
    queued = false;
    windowHost.clearInterval(timer);
    visibility.removeEventListener("visibilitychange", visible);
    windowHost.removeEventListener("focus", visible);
    windowHost.removeEventListener("online", visible);
  };
}
