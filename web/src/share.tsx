import { useState } from "react";
import { useNavigate } from "react-router-dom";
import * as api from "./api";
import { readCard } from "./cards";
import { parseDay } from "./parity";
import { useApp } from "./store";
import type { SocialFriend } from "./types";

export function ShareMenu({ card, label = "В чат" }: { card: string | null; label?: string }) {
  const app = useApp();
  const [open, setOpen] = useState(false);
  const [people, setPeople] = useState<SocialFriend[]>([]);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);

  async function toggle() {
    const next = !open;
    setOpen(next);
    setNote("");
    if (!next) return;
    if (!app.session?.authenticated) { setNote("Войдите в аккаунт в настройках"); return; }
    try {
      const home = await api.socialHome();
      setPeople(home.friends);
      if (home.friends.length === 0) setNote("Добавьте человека по коду на странице «Друзья»");
    } catch { setNote("Переписка не открылась"); }
  }

  async function send(person: SocialFriend) {
    if (!card || busy) return;
    setBusy(true);
    try {
      await api.socialCard(person.conversationId, card);
      setNote("Отправлено · " + (person.displayName || person.username));
    } catch { setNote("Не отправилось"); }
    finally { setBusy(false); }
  }

  return (
    <div className="share">
      <button className="btn" type="button" disabled={!card} onClick={() => void toggle()}>{label}</button>
      {open && (
        <div className="share-pop">
          {note && <p className="muted">{note}</p>}
          {people.map(person => (
            <button key={person.userId} type="button" disabled={busy} onClick={() => void send(person)}>{person.displayName || person.username}</button>
          ))}
        </div>
      )}
    </div>
  );
}

export function CardView({ body }: { body: string | null }) {
  const app = useApp();
  const navigate = useNavigate();
  const card = readCard(body);
  if (!card) return <div className="text">{body}</div>;
  if (card.type === "schedule") return (
    <div className="zapara-card">
      <b>Расписание · {card.group}</b>
      <span className="muted">{card.title}</span>
      {card.items.map(item => (
        <div className="zapara-row" key={item.time + item.subject}>
          <span>{item.time}</span>
          <span><b>{item.subject}</b>{item.lesson ? ` · ${item.lesson}` : ""}{item.place ? ` · ${item.place}` : ""}</span>
        </div>
      ))}
      <button className="btn" type="button" onClick={() => { app.setDate(parseDay(card.date)); navigate("/schedule"); }}>Открыть этот день</button>
    </div>
  );
  if (card.type === "lesson") return (
    <div className="zapara-card">
      <b>{card.subject}</b>
      <span>{card.time}{card.lesson ? ` · ${card.lesson}` : ""}</span>
      <span className="muted">{[card.place, card.teacher, card.group].filter(Boolean).join(" · ")}</span>
      {card.date && <button className="btn" type="button" onClick={() => { app.setDate(parseDay(card.date)); navigate("/schedule"); }}>Открыть день</button>}
    </div>
  );
  if (card.type === "homework") return (
    <div className="zapara-card">
      <b>Домашка · {card.subject}</b>
      <span>{card.text}</span>
      <button className="btn" type="button" onClick={() => saveTask(app, card.subject, card.text)}>Сохранить себе</button>
    </div>
  );
  if (card.type === "tasks") return (
    <div className="zapara-card">
      <b>Домашка</b>
      {card.items.map(item => <div key={item.subject + item.text}><b>{item.subject}</b><div>{item.text}</div></div>)}
      <button className="btn" type="button" onClick={() => card.items.forEach(item => saveTask(app, item.subject, item.text))}>Сохранить себе</button>
    </div>
  );
  return (
    <div className="zapara-card">
      <b>Куда идти</b>
      <span>{[card.building, card.floor && `${card.floor} этаж`, card.room].filter(Boolean).join(", ")}</span>
      {card.subject && <span className="muted">{card.subject}</span>}
      <button className="btn" type="button" onClick={() => { sessionStorage.setItem("zapara.map.building", card.building); sessionStorage.setItem("zapara.map.floor", card.floor); navigate("/maps"); }}>Открыть карты</button>
    </div>
  );
}

function saveTask(app: ReturnType<typeof useApp>, subject: string, text: string) {
  if (app.homework.some(item => item.subject === subject && item.text === text)) return;
  app.saveHomework({ id: crypto.randomUUID(), subject, text, done: false, created: new Date().toISOString() });
}
