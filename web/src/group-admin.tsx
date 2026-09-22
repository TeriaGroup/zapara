import { FormEvent, useState } from "react";
import * as api from "./api";
import { groupPowers } from "./powers";
import type { Classmate, GroupDesk } from "./types";

function roleTitle(role: string) {
  if (role === "headman") return "Староста";
  if (role === "curator") return "Куратор";
  return "Участник";
}

export function titlesOf(desk: GroupDesk | null, userId: string) {
  if (!desk) return [];
  return desk.grants.filter(grant => grant.userId === userId).map(grant => desk.roles.find(role => role.roleId === grant.roleId)?.name).filter((name): name is string => !!name);
}

export function GroupAdmin({ communityId, classmates, desk, onChange, onReload, onError }: {
  communityId: string;
  classmates: Classmate[];
  desk: GroupDesk;
  onChange: (desk: GroupDesk) => void;
  onReload: () => Promise<void>;
  onError: (text: string) => void;
}) {
  const [name, setName] = useState("");
  const [pick, setPick] = useState<Record<string, string>>({});
  const can = (code: string) => desk.mine.includes(code);
  if (desk.mine.length === 0) return null;

  async function run(action: () => Promise<GroupDesk>) {
    try { onChange(await action()); }
    catch { onError("Не получилось сохранить изменение группы"); }
  }

  function create(event: FormEvent) {
    event.preventDefault();
    if (name.trim().length < 2) return;
    const value = name.trim();
    setName("");
    void run(() => api.createGroupRole(communityId, value));
  }

  return (
    <section className="card stack">
      <h2>Управление группой</h2>
      <p className="muted">Роли и возможности действуют только внутри «Расписание военмех» и не подтверждены университетом. Старосту и куратора назначает администрация приложения. Возможности роли включает староста или общее голосование.</p>
      {can("roles") && <form className="row" onSubmit={create}>
        <input value={name} onChange={event => setName(event.target.value)} placeholder="Название роли" aria-label="Название роли" maxLength={32} />
        <button className="btn primary" type="submit" disabled={name.trim().length < 2}>Создать роль</button>
      </form>}
      {desk.roles.map(role => {
        const titles = groupPowers.filter(item => desk.powers.some(power => power.roleId === role.roleId && power.power === item.code)).map(item => item.title);
        return (
        <div className="stack" key={role.roleId}>
          <div className="row" style={{ justifyContent: "space-between" }}>
            <b>{role.name}</b>
            {can("roles") && <span className="row">
              <button className="btn" type="button" onClick={() => {
                const next = window.prompt("Новое название", role.name);
                if (next && next.trim() && next.trim() !== role.name) void run(() => api.renameGroupRole(communityId, role.roleId, next.trim()));
              }}>Переименовать</button>
              <button className="btn" type="button" onClick={() => void run(() => api.deleteGroupRole(communityId, role.roleId))}>Удалить</button>
            </span>}
          </div>
          {titles.length > 0 && <p className="muted">{titles.join(", ")}</p>}
          {desk.headman && <span className="row">{groupPowers.map(item => {
            const on = desk.powers.some(power => power.roleId === role.roleId && power.power === item.code);
            return <button key={item.code} className={on ? "btn primary" : "btn"} type="button" onClick={() => void run(() => api.setRolePower(communityId, role.roleId, item.code, !on))}>{item.title}</button>;
          })}</span>}
        </div>
        );
      })}
      {desk.roles.length === 0 && <p className="muted">Своих ролей пока нет. Например: замстаросты, ответственный за домашку.</p>}
      {can("joins") && desk.applicants.length > 0 && <h2>Заявки</h2>}
      {can("joins") && desk.applicants.map(person => (
        <div className="row" key={person.requestId} style={{ justifyContent: "space-between" }}>
          <span><b>{person.displayName || person.username}</b><div className="muted">@{person.username}</div></span>
          <span className="row">
            <button className="btn primary" type="button" onClick={() => void api.acceptJoin(communityId, person.requestId).then(onReload).catch(() => onError("Не получилось сохранить изменение группы"))}>Принять</button>
            <button className="btn" type="button" onClick={() => void api.rejectJoin(communityId, person.requestId).then(onReload).catch(() => onError("Не получилось сохранить изменение группы"))}>Отклонить</button>
          </span>
        </div>
      ))}
      {(can("grants") || can("exclude")) && <h2>Участники</h2>}
      {(can("grants") || can("exclude")) && classmates.map(person => (
        <div className="row" key={person.userId} style={{ justifyContent: "space-between" }}>
          <span>{person.displayName || person.username}</span>
          <span className="row">
            {can("grants") && <>
              <select aria-label={"Роль для " + (person.displayName || person.username)} value={pick[person.userId] || ""} onChange={event => setPick(current => ({ ...current, [person.userId]: event.target.value }))}>
                <option value="">Роль</option>
                {desk.roles.map(role => <option key={role.roleId} value={role.roleId}>{role.name}</option>)}
              </select>
              <button className="btn" type="button" disabled={!pick[person.userId]} onClick={() => void run(() => api.grantGroupRole(communityId, pick[person.userId], person.userId))}>Назначить</button>
            </>}
            {can("exclude") && !person.self && person.role === "member" && <button className="btn" type="button" onClick={() => { if (window.confirm("Исключить участника из группы?")) void api.removeGroupMember(communityId, person.userId).then(onReload).catch(() => onError("Не получилось сохранить изменение группы")); }}>Исключить</button>}
          </span>
        </div>
      ))}
      {can("grants") && <p className="muted">У человека не больше трёх своих ролей. {roleTitle("headman")} и {roleTitle("curator")} этой формой не меняются.</p>}
    </section>
  );
}
