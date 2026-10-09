import type { IconName } from "./icons";

// Значок канала приходит либо эмодзи (так создаёт web), либо именем (стартовые каналы сервера: "megaphone", "vote"; #30).
// Имя и известное эмодзи → иконка; неизвестное имя → иконка по типу канала; прочие эмодзи пользователя показываются как есть.
const named: Record<string, IconName> = {
  chat: "chat", message: "chat", "💬": "chat",
  pin: "pin", "📌": "pin",
  megaphone: "megaphone", announcement: "megaphone", announcements: "megaphone", "📢": "megaphone", "📣": "megaphone",
  vote: "ballot", ballot: "ballot", ballots: "ballot", poll: "ballot", polls: "ballot", "🗳️": "ballot", "🗳": "ballot",
  homework: "homework", book: "homework", "📚": "homework", "📒": "homework",
  calendar: "calendar", schedule: "calendar", "📅": "calendar",
  paperclip: "paperclip", materials: "paperclip", "📎": "paperclip",
  file: "file", forms: "file", form: "file",
};

const byKind: Record<string, IconName> = {
  chat: "chat", ballots: "ballot", forms: "file", materials: "paperclip", homework: "homework", schedule: "calendar",
};

export type TopicIconView = { icon: IconName } | { text: string };

export function topicIcon(raw: string | null | undefined, kind: string): TopicIconView {
  const value = (raw ?? "").trim();
  const known = named[value] ?? named[value.toLowerCase()];
  if (known) return { icon: known };
  if (!value || /^[a-z0-9-]+$/i.test(value)) return { icon: byKind[kind] ?? "chat" };
  return { text: value };
}
