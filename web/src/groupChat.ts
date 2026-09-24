import type { ChatMessage } from "./types";

type GroupPage = { messages: ChatMessage[]; hasMore: boolean };
export type GroupUpdates = { messages: ChatMessage[]; hasOlder: boolean };
export type GroupMessageCursor = { after?: string; before?: string };

export function groupMessageQuery(topic?: string, cursor?: GroupMessageCursor): string {
  if (cursor?.after && cursor.before) throw new Error("Message cursor must be before or after");
  const query = new URLSearchParams();
  if (topic) query.set("topic", topic);
  if (cursor?.after) query.set("after", cursor.after);
  if (cursor?.before) query.set("before", cursor.before);
  return query.size ? `?${query}` : "";
}

export function mergeGroupMessages(current: ChatMessage[], incoming: ChatMessage[]): ChatMessage[] {
  const map = new Map(current.map(item => [item.messageId, item]));
  for (const item of incoming) map.set(item.messageId, item);
  return [...map.values()].sort((left, right) => left.createdAt.localeCompare(right.createdAt));
}

export async function loadGroupUpdates(
  known: ChatMessage[],
  load: (after?: string) => Promise<GroupPage>,
): Promise<GroupUpdates> {
  const latest = await load();
  if (known.length === 0) return { messages: latest.messages, hasOlder: latest.hasMore };
  const knownIds = new Set(known.map(item => item.messageId));
  if (latest.messages.some(item => knownIds.has(item.messageId))) return { messages: latest.messages, hasOlder: latest.hasMore };

  let after = known.at(-1)!.messageId;
  const cursors = new Set([after]);
  let updates: ChatMessage[] = [];
  while (true) {
    const page = await load(after);
    updates = mergeGroupMessages(updates, page.messages);
    if (!page.hasMore) return { messages: mergeGroupMessages(updates, latest.messages), hasOlder: latest.hasMore };
    const next = page.messages.at(-1)?.messageId;
    if (!next || cursors.has(next)) throw new Error("Group pagination did not advance");
    cursors.add(next);
    after = next;
  }
}

export function createGroupPoller(
  load: (after?: string) => Promise<GroupPage>,
  known: () => ChatMessage[],
  publish: (updates: GroupUpdates, firstLoad: boolean) => void,
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
      const task = loadGroupUpdates(known(), load)
        .then(updates => {
          if (disposed || startedAt !== revision) return;
          publish(updates, !loaded);
          loaded = true;
        })
        .catch(() => { if (!disposed && startedAt === revision) onError(); })
        .finally(() => { if (inFlight === task) inFlight = null; });
      inFlight = task;
      return task;
    },
    changed() { revision += 1; },
    dispose() { disposed = true; revision += 1; },
  };
}

export function newestUnseenIncoming(known: ChatMessage[], updates: ChatMessage[], self: string): ChatMessage | null {
  const knownIds = new Set(known.map(item => item.messageId));
  for (let index = updates.length - 1; index >= 0; index -= 1) {
    const item = updates[index];
    if (item.senderId !== self && !knownIds.has(item.messageId)) return item;
  }
  return null;
}

export function clearSentGroupDraft(drafts: Record<string, string>, key: string, sent: string, unchanged = true): Record<string, string> {
  return unchanged && drafts[key] === sent ? { ...drafts, [key]: "" } : drafts;
}

export function groupMediaSelectionIsCurrent(
  started: { key: string; conversationId: string; epoch: number },
  current: { key: string; conversationId: string; epoch: number },
): boolean {
  return started.key === current.key && started.conversationId === current.conversationId && started.epoch === current.epoch;
}
