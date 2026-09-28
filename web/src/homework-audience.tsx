import { useEffect, useState } from "react";
import * as api from "./api";
import type { GroupHome, GroupSpace, HomeworkAudience } from "./types";
import { allHomeworkAudience, audienceMemberIds } from "./homework-audience-policy";
export { allHomeworkAudience, audienceLabel } from "./homework-audience-policy";
export function useHomeworkAudienceData(communityId: string) {
  const [data, setData] = useState<{ home: GroupHome; space: GroupSpace } | null>(null);
  const [error, setError] = useState(false);
  useEffect(() => {
    let cancelled = false;
    setData(null); setError(false);
    if (!communityId) return;
    void Promise.all([api.groupHome(communityId), api.groupSpace(communityId)])
      .then(([home, space]) => { if (!cancelled) setData({ home, space }); })
      .catch(() => { if (!cancelled) setError(true); });
    return () => { cancelled = true; };
  }, [communityId]);
  return { ...data, loading: !!communityId && !data && !error, error,
    supported: !!data?.space.capabilities.homeworkAudience };
}
export function HomeworkRecipients({ communityId, value, onChange, disabled = false, data }: {
  communityId: string; value: HomeworkAudience; onChange: (audience: HomeworkAudience) => void;
  disabled?: boolean; data: ReturnType<typeof useHomeworkAudienceData>;
}) {
  const [search, setSearch] = useState("");
  const home = data.home, space = data.space;
  const selected = value.kind === "selected";
  const count = home && space ? audienceMemberIds(value, home, space).length : 0;
  const toggle = (key: "roleIds" | "userIds", id: string) => onChange({ ...value, kind: "selected",
    [key]: value[key].includes(id) ? value[key].filter(item => item !== id) : [...value[key], id] });
  return <div className="homework-recipients stack" aria-label="Получатели общей домашки">
    <div className="row"><label className="check"><input type="radio" name={`homework-audience-${communityId}`} checked={!selected} disabled={disabled} onChange={() => onChange(allHomeworkAudience())}/>Вся учебная группа</label>
      <label className="check"><input type="radio" name={`homework-audience-${communityId}`} checked={selected} disabled={disabled || !data.supported} onChange={() => onChange({ ...value, kind: "selected" })}/>Подгруппы и участники</label></div>
    {data.loading && <p className="muted" role="status">Загружаем состав группы…</p>}
    {data.error && <p role="alert">Состав группы не загрузился. Выбор получателей недоступен.</p>}
    {!data.loading && !data.error && !data.supported && <p className="muted">Адресная домашка недоступна на этом сервере. Можно отправить всей группе.</p>}
    {selected && home && space && <><p className="muted">Получат {count} из {home.classmates.length} участников. Роль здесь обозначает подгруппу; права управления от выбора не меняются.</p>
      <div className="row homework-recipient-chips">{value.roleIds.map(id => <button className="chip" type="button" disabled={disabled} key={id} onClick={() => toggle("roleIds", id)}>{space.desk.roles.find(role => role.roleId === id)?.name || "Подгруппа"} ×</button>)}{value.userIds.map(id => <button className="chip" type="button" disabled={disabled} key={id} onClick={() => toggle("userIds", id)}>{home.classmates.find(person => person.userId === id)?.displayName || home.classmates.find(person => person.userId === id)?.username || "Участник"} ×</button>)}</div>
      <div className="homework-recipient-list" role="group" aria-label="Подгруппы">{space.desk.roles.map(role => <label className="check" key={role.roleId}><input type="checkbox" checked={value.roleIds.includes(role.roleId)} disabled={disabled} onChange={() => toggle("roleIds", role.roleId)}/>{role.icon} {role.name} <span className="muted">{space.desk.grants.filter(grant => grant.roleId === role.roleId).length}</span></label>)}</div>
      <label className="field">Найти участника<input type="search" value={search} disabled={disabled} onChange={event => setSearch(event.target.value)} placeholder="Имя или логин"/></label>
      <div className="homework-recipient-list" role="group" aria-label="Участники">{home.classmates.filter(person => `${person.displayName || ""} ${person.username}`.toLocaleLowerCase("ru").includes(search.toLocaleLowerCase("ru"))).map(person => <label className="check" key={person.userId}><input type="checkbox" checked={value.userIds.includes(person.userId)} disabled={disabled} onChange={() => toggle("userIds", person.userId)}/>{person.displayName || person.username}</label>)}</div>
      {count === 0 && <p role="alert">Выберите хотя бы одного участника или подгруппу.</p>}</>}
  </div>;
}
