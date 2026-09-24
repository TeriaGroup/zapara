import type { SocialMessage, SocialPage } from "./types";

export function mergeSocialMessages(current: SocialMessage[], incoming: SocialMessage[]): SocialMessage[] {
  const map = new Map(current.map(item => [item.messageId, item]));
  for (const item of incoming) map.set(item.messageId, item);
  return [...map.values()].sort((left, right) => left.createdAt.localeCompare(right.createdAt));
}

export async function loadSocialUpdates(
  known: SocialMessage[],
  load: (before?: string) => Promise<SocialPage>,
): Promise<SocialPage> {
  let page = await load();
  const hasMore = page.hasMore;
  let messages = page.messages;
  const knownIds = new Set(known.map(item => item.messageId));
  const cursors = new Set<string>();

  while (knownIds.size > 0 && page.hasMore && !page.messages.some(item => knownIds.has(item.messageId))) {
    const before = page.messages[0]?.messageId;
    if (!before || cursors.has(before)) throw new Error("Social pagination did not advance");
    cursors.add(before);
    page = await load(before);
    messages = [...page.messages, ...messages];
  }

  return { messages, hasMore };
}

export function createSocialPoller(
  load: (before?: string) => Promise<SocialPage>,
  known: () => SocialMessage[],
  publish: (page: SocialPage, firstLoad: boolean) => void,
  onError: () => void,
) {
  let inFlight: Promise<void> | null = null;
  let revision = 0;
  let loaded = false;
  let disposed = false;

  return {
    poll(): Promise<void> {
      if (disposed) return Promise.resolve();
      if (inFlight) return inFlight;
      const startedAt = revision;
      const task = loadSocialUpdates(known(), load)
        .then(page => {
          if (disposed || startedAt !== revision) return;
          publish(page, !loaded);
          loaded = true;
        })
        .catch(() => {
          if (!disposed && startedAt === revision) onError();
        })
        .finally(() => { if (inFlight === task) inFlight = null; });
      inFlight = task;
      return task;
    },
    changed() { revision += 1; },
    dispose() { disposed = true; revision += 1; },
  };
}
