import type { BallotBoard, GroupTopic } from "./types";

export function canComposeChannel(topic: GroupTopic): boolean { return topic.kind === "chat" && topic.canPost; }

export function canCreateBallot(topic: GroupTopic, canManageChannels: boolean): boolean {
  return topic.kind === "ballots" && (topic.writePolicy === "all" || canManageChannels);
}

export function orderedTopics(topics: GroupTopic[]): GroupTopic[] {
  const rank = (topic: GroupTopic) => topic.topicId === null ? 0 : topic.pinned ? 1 : 2;
  return [...topics].sort((left, right) => rank(left) - rank(right) || Number(right.unread > 0) - Number(left.unread > 0));
}

export type TopicBrowseFilter = { query: string; kind: "all" | "chat" | "ballots"; unreadOnly: boolean };

export function filterTopics(topics: GroupTopic[], filter: TopicBrowseFilter): GroupTopic[] {
  const query = filter.query.trim().toLocaleLowerCase("ru-RU");
  return orderedTopics(topics).filter(topic =>
    (filter.kind === "all" || topic.kind === filter.kind) &&
    (!filter.unreadOnly || topic.unread > 0) &&
    (!query || `${topic.title} ${topic.description}`.toLocaleLowerCase("ru-RU").includes(query)));
}

const paints = ["#3d6b4f", "#3d5a80", "#8a5a2a", "#6b3d5a", "#3d5a6b", "#5a4a3d", "#6b4030", "#2f5d50"];
const accents: Record<string, string> = {
  blue: "#3d5a80", green: "#3d6b4f", purple: "#6b3d5a", orange: "#8a5a2a", red: "#6b4030",
};

export function channelAccentColor(accent: string, title: string): string {
  if (accent !== "default" && accents[accent]) return accents[accent];
  let hash = 0;
  for (const ch of title) hash = (hash + ch.charCodeAt(0)) % paints.length;
  return paints[hash];
}

export async function ballotBoardAfterMutation(board: BallotBoard, topicId: string | undefined, load: (topicId: string) => Promise<BallotBoard>): Promise<BallotBoard> {
  return topicId ? load(topicId) : board;
}

export function isChatChannel(topic: GroupTopic): boolean {
  return topic.kind === "chat";
}

export function topicPreview(topic: GroupTopic): string {
  if (isChatChannel(topic)) {
    if (!topic.lastBody) return "Сообщений пока нет";
    return topic.lastAuthor ? `${topic.lastAuthor}: ${topic.lastBody}` : topic.lastBody;
  }
  const count = topic.activeBallots;
  const activity = count > 0 ? `${count} активн${count % 10 === 1 && count % 100 !== 11 ? "ое голосование" : count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 12 || count % 100 > 14) ? "ых голосования" : "ых голосований"}` : "";
  return [topic.lastBody || (activity ? "" : "Голосований пока нет"), activity].filter(Boolean).join(" · ");
}
