import { FormEvent, useEffect, useState } from "react";
import * as api from "./api";
import type { GroupTopic } from "./types";

const icons = ["📌", "💬", "📅", "📚", "💻", "📎", "❗", "🏀", "🧪", "✏️", "🎵", "🌍"];
const paints = ["#3d6b4f", "#3d5a80", "#8a5a2a", "#6b3d5a", "#3d5a6b", "#5a4a3d", "#6b4030", "#2f5d50"];

function paint(title: string) {
  let hash = 0;
  for (const ch of title) hash = (hash + ch.charCodeAt(0)) % paints.length;
  return paints[hash];
}

function when(iso: string | null) {
  if (!iso) return "";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  const same = date.toDateString() === new Date().toDateString();
  return same
    ? date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })
    : date.toLocaleDateString("ru-RU", { day: "numeric", month: "short" });
}

export function GroupTopics({ communityId, onOpen, onError }: {
  communityId: string;
  onOpen: (topic: GroupTopic) => void;
  onError: (text: string) => void;
}) {
  const [topics, setTopics] = useState<GroupTopic[]>([]);
  const [title, setTitle] = useState("");
  const [icon, setIcon] = useState("📚");
  const [off, setOff] = useState(false);

  useEffect(() => {
    let stop = false;
    const pull = () => api.topics(communityId).then(page => {
      if (stop) return;
      setTopics(page.topics);
      setOff(false);
    }).catch(() => { if (!stop) setOff(true); });
    void pull();
    const timer = window.setInterval(pull, 4000);
    return () => { stop = true; window.clearInterval(timer); };
  }, [communityId]);

  function create(event: FormEvent) {
    event.preventDefault();
    const name = title.trim();
    if (name.length < 2) return;
    setTitle("");
    void api.createTopic(communityId, name, icon).then(page => setTopics(page.topics)).catch(() => { setTitle(name); onError("Не получилось создать раздел"); });
  }

  return (
    <div className="topics">
      <p className="muted">Разделы группы, как темы в переписке. Общий поток остаётся наверху. Всё это только внутри Запары.</p>
      <form className="stack" onSubmit={create}>
        <div className="row" aria-label="Значок раздела">
          {icons.map(item => <button key={item} className={"btn" + (icon === item ? " primary" : "")} type="button" onClick={() => setIcon(item)} aria-label={item}>{item}</button>)}
        </div>
        <div className="row">
          <input value={title} onChange={event => setTitle(event.target.value)} placeholder="Название раздела" aria-label="Название раздела" maxLength={40} />
          <button className="btn primary" type="submit" disabled={title.trim().length < 2}>Создать</button>
        </div>
      </form>
      {off && topics.length === 0 && <p className="muted">Разделы сейчас не открылись.</p>}
      <div className="topic-list">
        {topics.map(topic => (
          <div className="topic" key={topic.topicId ?? "general"}>
            <button className="topic-open" type="button" onClick={() => onOpen(topic)}>
              <span className="topic-icon" style={{ background: paint(topic.title) }}>{topic.icon}</span>
              <span className="topic-main">
                <b>{topic.title}</b>
                <span className="preview">{topic.lastAuthor && topic.lastBody ? `${topic.lastAuthor}: ${topic.lastBody}` : "Пока пусто"}</span>
              </span>
              <span className="topic-meta">
                {when(topic.lastAt)}
                {topic.unread > 0 && <span className="chip">{topic.unread}</span>}
              </span>
            </button>
            {topic.canDelete && topic.topicId && <button className="btn" type="button" onClick={() => {
              if (window.confirm("Удалить раздел и его сообщения?")) void api.deleteTopic(communityId, topic.topicId as string).then(page => setTopics(page.topics)).catch(() => onError("Не получилось удалить раздел"));
            }}>Удалить</button>}
          </div>
        ))}
      </div>
    </div>
  );
}
