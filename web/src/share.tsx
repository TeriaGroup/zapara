import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import * as api from "./api";
import { readCard } from "./cards";
import { parseDay } from "./parity";
import { useApp } from "./store";
import type { SocialFriend } from "./types";
import { Sheet } from "./sheet";
import { SearchField } from "./ux300-controls";
import { noteSearch } from "./ux300";

export function ShareMenu({ card, label = "В чат" }: { card: string | null; label?: string }) {
  const app = useApp();
  const [open, setOpen] = useState(false);
  const [people, setPeople] = useState<SocialFriend[]>([]);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [query,setQuery]=useState("");const[selected,setSelected]=useState<SocialFriend|null>(null);const[sentTo,setSentTo]=useState("");
  const owner=JSON.stringify([app.session?.user?.userId,app.session?.familyId]);
  const currentOwner=useRef(owner);currentOwner.current=owner;
  const currentCard=useRef(card);currentCard.current=card;
  const[openedFor,setOpenedFor]=useState("");const pending=useRef(false);
  const mounted=useRef(true);useEffect(()=>{mounted.current=true;return()=>{mounted.current=false;};},[]);
  useEffect(()=>{setSentTo("");setSelected(null);},[card,owner]);

  async function toggle() {
    const next = !open;
    setOpen(next);
    setOpenedFor(owner);setSelected(null);setPeople([]);setQuery("");
    setNote("");
    if (!next) return;
    if (!app.session?.authenticated) { setNote("Войдите в аккаунт в настройках"); return; }
    try {
      const home = await api.socialHome();
      if(!mounted.current||currentOwner.current!==owner)return;
      setPeople(home.friends);
      if (home.friends.length === 0) setNote("Добавьте человека по коду на странице «Друзья»");
    } catch { if(mounted.current&&currentOwner.current===owner)setNote("Переписка не открылась. Закройте панель и повторите."); }
  }

  async function send(person: SocialFriend) {
    if (!card || pending.current || openedFor!==owner || sentTo===person.conversationId) return;
    pending.current=true;setBusy(true);
    try {
      await api.socialCard(person.conversationId, card);
      if(!mounted.current||currentOwner.current!==owner||currentCard.current!==card)return;
      setNote("Отправлено · " + (person.displayName || person.username));
      setSentTo(person.conversationId);
    } catch { if(mounted.current&&currentOwner.current===owner)setNote("Подтверждение отправки не получено. Проверьте беседу перед повтором."); }
    finally { pending.current=false;if(mounted.current&&currentOwner.current===owner)setBusy(false); }
  }

  return (
    <div className="share">
      <button className="btn" type="button" disabled={!card} onClick={() => void toggle()}>{label}</button>
      {open && openedFor===owner && (
        <Sheet title="Отправить учебную карточку" onClose={()=>{if(!busy)setOpen(false);}}><div className="stack">
          {note && <p className="muted">{note}</p>}
          <SearchField label="Найти адресата" value={query} onChange={setQuery}/>{people.filter(person=>noteSearch(query,person.displayName||"",person.username)).map(person => (
            <button className={selected?.userId===person.userId?"btn primary":"btn"} key={person.userId} type="button" disabled={busy} onClick={() => setSelected(person)}>{person.displayName || person.username}</button>
          ))}
          {selected&&<><p>Получатель: <b>{selected.displayName||selected.username}</b></p><fieldset disabled style={{border:0,padding:0,margin:0}}><CardView body={card}/></fieldset><button className="btn primary" disabled={busy||sentTo===selected.conversationId} onClick={()=>void send(selected)}>{sentTo===selected.conversationId?"Отправлено":busy?"Отправляем…":"Отправить этому человеку"}</button></>}
        </div></Sheet>
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
      <button className="btn" type="button" onClick={() => { const target=app.catalog?.groups.find(group=>group.name.toLocaleLowerCase("ru")===card.group.toLocaleLowerCase("ru"));if(!target){window.alert("Группа этой карточки не найдена в доступном каталоге. Содержимое карточки остаётся ниже.");return;}if(target.id!==app.groupId&&!window.confirm(`Открыть расписание группы ${target.name} вместо выбранной группы?`))return;if(target.id!==app.groupId)app.setGroupId(target.id);app.setDate(parseDay(card.date)); navigate(`/schedule?date=${card.date}`); }}>Открыть этот день</button>
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
