import { noteSearch } from "./ux300";
import { SearchField } from "./ux300-controls";
import { FormEvent, useEffect, useState } from "react";
import * as api from "./api";
import { canReadAudit } from "./topic-policy";
import { groupPowers } from "./powers";
import { powerTitles } from "./topics";
import { AvatarEditor } from "./avatar-view";
import type { Classmate, GroupDesk, GroupRole, GroupAuditEvent } from "./types";
export function titlesOf(desk: GroupDesk | null, userId: string) { return desk?.grants.filter(grant => grant.userId === userId).map(grant => desk.roles.find(role => role.roleId === grant.roleId)?.name).filter((name): name is string => !!name) ?? []; }
export function GroupAdmin({ communityId, groupName, classmates, desk, onChange, onReload, onError }: {
    communityId: string;
    groupName: string;
    classmates: Classmate[];
    desk: GroupDesk;
    onChange: (desk: GroupDesk) => void;
    onReload: () => Promise<void>;
    onError: (text: string) => void;
}) {
    const [open, setOpen] = useState(false);
    const [name, setName] = useState("");
    const [busy, setBusy] = useState(false);
    const [editing, setEditing] = useState<GroupRole | null>(null);
    const [roleConflict, setRoleConflict] = useState<GroupRole | "unavailable" | null>(null);
    const [fields, setFields] = useState<{name:string;icon:string;position:number|string}>({ name: "", icon: "", position: 0 });
    const discardEditing = () => !editing || fields.name === editing.name && fields.icon === (editing.icon || "") && Number(fields.position) === (editing.position ?? 0) || window.confirm(`Отменить несохранённые изменения роли «${editing.name}»?`);
    const [events, setEvents] = useState<GroupAuditEvent[] | null>(null);
    const [selectedRoleId, setSelectedRoleId] = useState("");
    const [tab, setTab] = useState<"settings" | "powers" | "members">("settings");
    const [memberSearch, setMemberSearch] = useState("");
    const [roleQuery,setRoleQuery]=useState("");const [auditQuery,setAuditQuery]=useState("");const [auditAfter,setAuditAfter]=useState("");const [auditBefore,setAuditBefore]=useState("");
    const [assignedOnly, setAssignedOnly] = useState(false);
    const can = (code: string) => desk.headman || desk.mine.includes(code);
    const self = classmates.find(person => person.self)?.userId;
    const height = Math.max(0, ...desk.grants.filter(grant => grant.userId === self).map(grant => desk.roles.find(role => role.roleId === grant.roleId)?.position ?? 0));
    const manageable = (role: GroupRole) => desk.headman || (role.position !== undefined && role.position < height && !desk.grants.some(grant => grant.userId === self && grant.roleId === role.roleId) && desk.powers.filter(power => power.roleId === role.roleId).every(power => desk.mine.includes(power.power)));
    const eligible = (person: Classmate, role: GroupRole) => manageable(role) && (desk.headman || (!person.self && person.role === "member" && Math.max(0, ...desk.grants.filter(grant => grant.userId === person.userId).map(grant => desk.roles.find(row => row.roleId === grant.roleId)?.position ?? 0)) < height));
    const selectedRole = desk.roles.find(role => role.roleId === selectedRoleId) || desk.roles[0];
    useEffect(() => { setMemberSearch(""); setAssignedOnly(false); }, [communityId, selectedRole?.roleId]);
    async function run(action: () => Promise<GroupDesk>, baseRole?: GroupRole) {
        if (busy)
            return;
        setBusy(true);
        try {
            onChange(await action());
        }
        catch (error) {
            if (error instanceof Error && error.message === "409" && baseRole) {
                const fresh = await api.groupDesk(communityId).catch(() => null);
                const latest = fresh?.roles.find(role => role.roleId === baseRole.roleId);
                setRoleConflict(latest ? { ...latest } : "unavailable");
                if (fresh)
                    onChange(fresh);
            }
            onError(error instanceof Error && error.message === "409" ? "Роль изменена другим участником. Несохранённые поля оставлены; обновите данные перед повторением." : error instanceof Error && error.message === "403" ? "Недостаточно прав: нельзя менять равную или старшую роль, либо выдавать отсутствующее право." : "Изменение группы не сохранено");
        }
        finally {
            setBusy(false);
        }
    }
    function create(event: FormEvent) {
        event.preventDefault();
        if (name.trim().length < 2)
            return;
        void run(async () => { const next = await api.createGroupRole(communityId, name.trim()); const created = next.roles.find(role => !desk.roles.some(previous => previous.roleId === role.roleId)); if (created) { setSelectedRoleId(created.roleId); setTab("settings"); } setName(""); return next; });
    }
    if (!desk.mine.length && !desk.headman)
        return null;
    return <details className="group-admin-panel" open={open} onToggle={event => setOpen(event.currentTarget.open)}><summary onClick={event => { if (open && (busy || !discardEditing())) event.preventDefault(); }}>Управление группой</summary><section className="card stack"><p className="muted">Роли действуют внутри приложения. Старосту и куратора назначает администрация приложения; их статусы защищены.</p>
    {can("channels") && <AvatarEditor kind="group" id={communityId} name={groupName} />}
    {can("roles") && <form className="row" onSubmit={create}><input aria-label="Название новой роли" value={name} onChange={event => setName(event.target.value)} maxLength={32} placeholder="Название роли"/><button className="btn primary" disabled={busy || name.trim().length < 2 || desk.roles.length >= (desk.capabilities?.maxRoles ?? 12)}>Создать роль</button></form>}
    <SearchField label="Найти роль или возможность" value={roleQuery} onChange={setRoleQuery}/><div className="group-role-picker" role="group" aria-label="Роли группы">{desk.roles.filter(role=>noteSearch(roleQuery,role.name,...desk.powers.filter(power=>power.roleId===role.roleId).map(power=>powerTitles[power.power]||power.power))).sort((a, b) => (b.position ?? 0) - (a.position ?? 0)).map(role => <button className="btn group-role-card" type="button" key={role.roleId} aria-pressed={selectedRole?.roleId === role.roleId} disabled={busy} onClick={() => { if (busy || !discardEditing()) return; setSelectedRoleId(role.roleId); setEditing(null); setRoleConflict(null); setTab("settings"); }}><strong>{role.icon} {role.name}</strong><span className="muted">Уровень {role.position ?? 0} · Назначений: {desk.grants.filter(grant => grant.roleId === role.roleId).length}</span></button>)}</div>
    {selectedRole && <><div className="row group-role-tabs" role="tablist" aria-label={`Роль ${selectedRole.name}`}><button type="button" className="btn" role="tab" aria-selected={tab === "settings"} onClick={() => setTab("settings")}>Настройки</button><button type="button" className="btn" role="tab" aria-selected={tab === "powers"} onClick={() => setTab("powers")}>Возможности</button><button type="button" className="btn" role="tab" aria-selected={tab === "members"} onClick={() => setTab("members")}>Участники</button></div>
    {tab === "settings" && [selectedRole].map(role => <div className="role-line stack" key={role.roleId}><div className="row"><b>{role.icon} {role.name}</b><span className="muted">Уровень {role.position ?? 0} · Назначений: {desk.grants.filter(grant => grant.roleId === role.roleId).length}</span>{can("roles") && manageable(role) && <><button className="btn quiet" type="button" disabled={busy} onClick={() => { if (!discardEditing()) return; setEditing({ ...role }); setRoleConflict(null); setFields({ name: role.name, icon: role.icon || "", position: role.position ?? 0 }); }}>Изменить</button><button className="btn quiet" type="button" disabled={busy} onClick={() => {
                    if (!desk.capabilities) {
                        if (window.confirm(`Удалить роль «${role.name}»? Назначений: ${desk.grants.filter(grant => grant.roleId === role.roleId).length}.`))
                            void run(() => api.deleteGroupRole(communityId, role.roleId));
                        return;
                    }
                    setBusy(true);
                    void api.roleImpact(communityId, role.roleId).then(impact => {
                        if (window.confirm(`Удалить роль «${role.name}»? Назначений: ${impact.assignments}. Правил доступа: ${impact.accessRules}. Официальное членство сохранится.`))
                            return api.deleteGroupRole(communityId, role.roleId).then(onChange);
                    }).catch(() => onError("Не удалось проверить влияние удаления роли")).finally(() => setBusy(false));
                }}>Удалить</button></>}{!manageable(role) && <span className="muted">Равная или старшая роль</span>}</div>
      <p className="muted">{desk.powers.filter(power => power.roleId === role.roleId).map(power => powerTitles[power.power] || power.power).join(", ") || "Без дополнительных прав"}</p>
      {editing?.roleId === role.roleId && <form className="card stack" onSubmit={event => {
                    event.preventDefault();
                    const baseRole = editing;
                    if (fields.position === "" || !Number.isInteger(Number(fields.position)) || Number(fields.position) < 0 || Number(fields.position) > (desk.headman ? 10000 : height - 1)) return;
                    if (!baseRole || baseRole.roleId !== role.roleId || roleConflict || !can("roles") || !manageable(role))
                        return;
                    void run(async () => { const next = desk.capabilities ? await api.saveRoleSettings(communityId, baseRole.roleId, fields.name, fields.icon, Number(fields.position), baseRole.revision ?? 0) : await api.renameGroupRole(communityId, baseRole.roleId, fields.name); setEditing(null); setRoleConflict(null); return next; }, baseRole);
                }}><label className="field">Название<input disabled={busy} value={fields.name} minLength={2} maxLength={32} required onChange={event => setFields(value => ({ ...value, name: event.target.value }))}/></label><label className="field">Значок<input disabled={busy} value={fields.icon} maxLength={8} onChange={event => setFields(value => ({ ...value, icon: event.target.value }))}/></label><label className="field">Уровень<input disabled={busy} type="number" min="0" max={desk.headman ? 10000 : Math.max(0, height - 1)} value={fields.position} onChange={event => setFields(value => ({ ...value, position: event.target.value }))}/></label><p className="muted">Уровень — целое число от 0 до {desk.headman ? 10000 : Math.max(0, height - 1)}.</p><p className="muted">Базовая ревизия редактора: {editing.revision ?? "прежний сервер"}</p>{roleConflict && <div className="banner stack" role="alert"><h2>Роль изменилась на сервере</h2>{roleConflict === "unavailable" ? <p>Актуальные параметры не загрузились или роль удалена. Ваши поля сохранены.</p> : <><p>Актуальные параметры: {roleConflict.name} · {roleConflict.icon} · уровень {roleConflict.position ?? 0} · ревизия {roleConflict.revision}</p><p>Ваши несохранённые поля: {fields.name} · {fields.icon} · уровень {fields.position}</p><button className="btn" type="button" disabled={busy || !can("roles") || !manageable(roleConflict)} onClick={() => {
                            if (window.confirm("Использовать актуальную ревизию для вашего черновика? При сохранении ваши поля заменят показанные параметры сервера.")) {
                                setEditing({ ...roleConflict });
                                setRoleConflict(null);
                            }
                        }}>Использовать актуальную ревизию</button></>}</div>}<p className="muted">Выше можно управлять только более низкими ролями. Права нескольких назначений объединяются.</p><div className="row"><button className="btn primary" disabled={busy || !!roleConflict || !can("roles") || !manageable(role) || fields.position === "" || !Number.isInteger(Number(fields.position)) || Number(fields.position) < 0 || Number(fields.position) > (desk.headman ? 10000 : height - 1)}>{busy ? "Сохраняем…" : "Сохранить"}</button><button className="btn quiet" type="button" onClick={() => { if (!discardEditing()) return; setEditing(null); setRoleConflict(null); }}>Отмена</button></div></form>}
    </div>)}
    {tab === "powers" && <div className="stack" role="tabpanel"><p className="muted">Права этой роли действуют только внутри приложения. Вы можете выдавать только свои возможности.</p>{(desk.capabilities?.powers ?? groupPowers.map(row => row.code)).map(power => { const enabled = desk.powers.some(row => row.roleId === selectedRole.roleId && row.power === power); return <label className="switch-row" key={power}><span>{powerTitles[power] || power}</span><input type="checkbox" role="switch" checked={enabled} disabled={busy || !can("roles") || !manageable(selectedRole) || (!desk.headman && !desk.mine.includes(power))} onChange={() => void run(() => api.setRolePower(communityId, selectedRole.roleId, power, !enabled))}/></label>; })}</div>}
    {tab === "members" && <div className="stack" role="tabpanel"><label className="field">Найти участника<input type="search" value={memberSearch} onChange={event => setMemberSearch(event.target.value)} placeholder="Имя или логин"/></label><label className="check"><input type="checkbox" checked={assignedOnly} onChange={event => setAssignedOnly(event.target.checked)}/>Только с этой ролью</label>{classmates.filter(person => (!assignedOnly || desk.grants.some(grant => grant.roleId === selectedRole.roleId && grant.userId === person.userId)) && `${person.displayName || ""} ${person.username}`.toLocaleLowerCase("ru").includes(memberSearch.toLocaleLowerCase("ru"))).map(person => { const assigned = desk.grants.some(grant => grant.roleId === selectedRole.roleId && grant.userId === person.userId); const full = desk.grants.filter(grant => grant.userId === person.userId).length >= (desk.capabilities?.maxRolesPerMember ?? 3); return <div className="role-person row" key={person.userId}><span><b>{person.displayName || person.username}</b><span className="chip">{person.role === "headman" ? "Староста" : person.role === "curator" ? "Куратор" : "Участник"}</span></span><span>{assigned ? <button className="btn quiet" type="button" disabled={busy || !can("grants") || !eligible(person, selectedRole)} onClick={() => void run(() => api.revokeGroupRole(communityId, selectedRole.roleId, person.userId))}>Снять роль</button> : <button className="btn" type="button" disabled={busy || full || !can("grants") || !eligible(person, selectedRole)} onClick={() => void run(() => api.grantGroupRole(communityId, selectedRole.roleId, person.userId))}>Назначить</button>}{can("exclude") && !person.self && person.role === "member" && <button className="btn quiet" type="button" disabled={busy} onClick={() => { if (window.confirm(`Исключить ${person.displayName || person.username} из группы?`)) void run(() => api.removeGroupMember(communityId, person.userId)); }}>Исключить</button>}</span></div>; })}</div>}</>}
    {!selectedRole && (can("exclude") || can("grants")) && <div className="stack" role="group" aria-label="Участники группы"><h2>Участники</h2><p className="muted">Ролей пока нет. Управление участниками остаётся доступным.</p><label className="field">Найти участника<input type="search" value={memberSearch} onChange={event => setMemberSearch(event.target.value)} placeholder="Имя или логин"/></label>{classmates.filter(person => `${person.displayName || ""} ${person.username}`.toLocaleLowerCase("ru").includes(memberSearch.toLocaleLowerCase("ru"))).map(person => <div className="role-person row" key={person.userId}><span><b>{person.displayName || person.username}</b><span className="chip">{person.role === "headman" ? "Староста" : person.role === "curator" ? "Куратор" : "Участник"}</span></span>{can("exclude") && !person.self && person.role === "member" && <button className="btn quiet" type="button" disabled={busy} onClick={() => { if (window.confirm(`Исключить ${person.displayName || person.username} из группы?`)) void run(() => api.removeGroupMember(communityId, person.userId)); }}>Исключить</button>}</div>)}</div>}
    {can("joins") && desk.applicants.length > 0 && <><h2>Заявки</h2>{desk.applicants.map(person => <div className="row" key={person.requestId}><b>{person.displayName || person.username}</b><button className="btn primary" type="button" disabled={busy} onClick={() => { setBusy(true); void api.acceptJoin(communityId, person.requestId).then(onReload).catch(() => onError("Заявка не принята")).finally(() => setBusy(false)); }}>Принять</button><button className="btn" type="button" disabled={busy} onClick={() => { setBusy(true); void api.rejectJoin(communityId, person.requestId).then(onReload).catch(() => onError("Не удалось отклонить заявку")).finally(() => setBusy(false)); }}>Отклонить</button></div>)}</>}
    {desk.capabilities && canReadAudit(desk) && <><button className="btn" type="button" disabled={busy} onClick={() => {
                setBusy(true);
                if (!canReadAudit(desk)) {
                    setBusy(false);
                    return;
                }
                void api.groupAudit(communityId).then(value => setEvents(value.events)).catch(() => onError("Журнал управления недоступен")).finally(() => setBusy(false));
            }}>Журнал управления</button>{events && <div className="stack"><h2>Последние изменения</h2><SearchField label="Поиск по действию, участнику или роли" value={auditQuery} onChange={setAuditQuery}/><div className="row"><label className="field">С даты<input type="date" value={auditAfter} onChange={event=>setAuditAfter(event.target.value)}/></label><label className="field">По дату<input type="date" value={auditBefore} onChange={event=>setAuditBefore(event.target.value)}/></label><button className="btn quiet" onClick={()=>{setAuditQuery("");setAuditAfter("");setAuditBefore("");}}>Весь загруженный журнал</button></div>{events.length === 0 && <p className="muted">Событий пока нет</p>}{events.filter(event=>(!auditAfter||event.createdAt.slice(0,10)>=auditAfter)&&(!auditBefore||event.createdAt.slice(0,10)<=auditBefore)&&noteSearch(auditQuery,event.action,classmates.find(person=>person.userId===event.actorId)?.displayName||"",desk.roles.find(role=>role.roleId===event.objectId)?.name||"")).map(event => <p key={event.eventId}>{new Date(event.createdAt).toLocaleString("ru-RU")} · {classmates.find(person => person.userId === event.actorId)?.displayName || "Участник"} · {event.action} · {desk.roles.find(role => role.roleId === event.objectId)?.name || "Объект группы"}</p>)}</div>}</>}
  </section></details>;
}
