import { FormEvent, useState } from "react";
import * as api from "./api";
import { emptyId, groupPowers } from "./powers";
import type { Ballot, BallotBoard, Classmate, GroupRole } from "./types";

function plural(n: number, one: string, few: string, many: string) {
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 === 1 && mod100 !== 11) return one;
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return few;
  return many;
}

function dayLabel(n: number) {
  return `${n} ${plural(n, "день", "дня", "дней")}`;
}

function originTitle(origin: string) {
  if (origin === "system") return "Система";
  if (origin === "headman") return "Староста";
  return "Общее";
}

function statusTitle(status: string) {
  if (status === "collecting") return "Сбор поддержки";
  if (status === "open") return "Идёт";
  return "Завершено";
}

function when(iso: string) {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" }).format(date);
}

function failureText(error: unknown, fallback: string) {
  const status = error instanceof Error ? error.message : "";
  if (status === "409") return "Голосование уже закрыто";
  if (status === "403") return "Недостаточно прав для этого действия";
  return fallback;
}

function BallotForm({ title, hint, submitLabel, action, onDone, onError }: {
  title: string;
  hint: string;
  submitLabel: string;
  action: (question: string, options: string[], days: number) => Promise<BallotBoard>;
  onDone: (board: BallotBoard) => void;
  onError: (text: string) => void;
}) {
  const [question, setQuestion] = useState("");
  const [options, setOptions] = useState(["", ""]);
  const [days, setDays] = useState(5);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const text = question.trim();
    const labels = options.map(item => item.trim()).filter(Boolean);
    if (!text || labels.length < 2) {
      onError("Нужны вопрос и хотя бы два варианта");
      return;
    }
    if (new Set(labels).size !== labels.length) {
      onError("Варианты должны отличаться");
      return;
    }
    setBusy(true);
    try {
      onDone(await action(text, labels, days));
      setQuestion("");
      setOptions(["", ""]);
      setDays(5);
    }
    catch (error) { onError(failureText(error, "Не получилось сохранить голосование")); }
    finally { setBusy(false); }
  }

  return (
    <form className="card stack" onSubmit={event => void submit(event)}>
      <h2>{title}</h2>
      <p className="muted">{hint}</p>
      <label className="field">Вопрос
        <input value={question} onChange={event => setQuestion(event.target.value)} maxLength={400} aria-label="Вопрос голосования" />
      </label>
      {options.map((value, index) => (
        <label className="field" key={index}>Вариант {index + 1}
          <span className="row">
            <input className="ballot-choice" value={value} onChange={event => setOptions(current => current.map((item, itemIndex) => itemIndex === index ? event.target.value : item))} maxLength={80} aria-label={`Вариант ${index + 1}`} />
            {options.length > 2 && <button className="btn" type="button" onClick={() => setOptions(current => current.filter((_, itemIndex) => itemIndex !== index))}>Убрать</button>}
          </span>
        </label>
      ))}
      <div className="row">
        <button className="btn" type="button" disabled={options.length >= 6} onClick={() => setOptions(current => [...current, ""])}>Ещё вариант</button>
        <select className="ballot-days" aria-label="Срок голосования" value={days} onChange={event => setDays(Number(event.target.value))}>
          {Array.from({ length: 14 }, (_, index) => index + 1).map(day => <option key={day} value={day}>{dayLabel(day)}</option>)}
        </select>
        <button className="btn primary" type="submit" disabled={busy}>{submitLabel}</button>
      </div>
    </form>
  );
}

function outcomeTitle(outcome: string) {
  if (outcome === "accepted") return "Изменение применено";
  if (outcome === "rejected") return "Изменение не принято";
  if (outcome === "skipped") return "Изменение не удалось применить";
  return "";
}

function BallotCard({ ballot, canClose, busy, onSupport, onVote, onClose }: {
  ballot: Ballot;
  canClose: boolean;
  busy: boolean;
  onSupport: () => void;
  onVote: (optionId: string) => void;
  onClose: () => void;
}) {
  const total = ballot.options.reduce((sum, option) => sum + option.votes, 0);
  return (
    <article className="stack">
      <div className="row">
        <span className="chip">{originTitle(ballot.origin)}</span>
        {ballot.effect && <span className="chip">Изменение группы</span>}
        <span className="chip">{statusTitle(ballot.status)}</span>
        {when(ballot.deadlineAt) && <span className="muted">до {when(ballot.deadlineAt)}</span>}
      </div>
      <b>{ballot.question}</b>
      {ballot.status === "collecting" && (
        <>
          <p className="muted">Поддержали {ballot.supporters} из {ballot.supportersNeeded}</p>
          <div className="ballot-bar" aria-hidden="true"><span style={{ width: `${Math.min(100, Math.round(ballot.supporters / Math.max(1, ballot.supportersNeeded) * 100))}%` }} /></div>
          <div className="row">{ballot.options.map(option => <span className="chip" key={option.optionId}>{option.label}</span>)}</div>
          {ballot.supported ? <span className="chip">Вы поддержали</span> : <button className="btn primary" type="button" disabled={busy} onClick={onSupport}>Поддержать</button>}
        </>
      )}
      {ballot.status !== "collecting" && ballot.options.map(option => {
        const width = total === 0 ? 0 : Math.round(option.votes / total * 100);
        return (
          <div className="ballot-option" key={option.optionId}>
            {ballot.status === "open" ? (
              <button className={option.chosen ? "btn primary" : "btn"} type="button" disabled={busy} onClick={() => onVote(option.optionId)}>
                <span>{option.label}{option.chosen ? " · ваш выбор" : ""}</span>
                <span>{option.votes}</span>
              </button>
            ) : (
              <div className="row" style={{ justifyContent: "space-between" }}>
                <span>{option.label}{option.chosen ? " · ваш выбор" : ""}</span>
                <span>{option.votes}</span>
              </div>
            )}
            <div className="ballot-bar" aria-hidden="true"><span style={{ width: `${width}%` }} /></div>
          </div>
        );
      })}
      {ballot.status === "open" && <p className="muted">{ballot.effect ? "Если «Принять» победит и наберёт не меньше порога, изменение вступит в силу в конце срока. Свой выбор можно сменить." : "Свой вариант можно сменить до конца срока."}</p>}
      {ballot.status === "closed" && outcomeTitle(ballot.outcome) && <p className="muted">{outcomeTitle(ballot.outcome)}</p>}
      {canClose && !ballot.effect && ballot.status !== "closed" && <button className="btn" type="button" disabled={busy} onClick={onClose}>Завершить</button>}
    </article>
  );
}

function ChangeForm({ communityId, classmates, roles, onDone, onError }: {
  communityId: string;
  classmates: Classmate[];
  roles: GroupRole[];
  onDone: (board: BallotBoard) => void;
  onError: (text: string) => void;
}) {
  const [kind, setKind] = useState("power");
  const [roleId, setRoleId] = useState("");
  const [userId, setUserId] = useState("");
  const [name, setName] = useState("");
  const [power, setPower] = useState("joins");
  const [enabled, setEnabled] = useState(true);
  const [days, setDays] = useState(5);
  const [busy, setBusy] = useState(false);
  const needsRole = kind === "power" || kind === "grant" || kind === "revoke_grant" || kind === "rename_role" || kind === "delete_role";
  const needsPerson = kind === "grant" || kind === "revoke_grant" || kind === "remove_member";
  const needsName = kind === "create_role" || kind === "rename_role";

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (needsRole && !roleId) { onError("Выберите роль"); return; }
    if (needsPerson && !userId) { onError("Выберите участника"); return; }
    if (needsName && name.trim().length < 2) { onError("Название роли должно быть от 2 символов"); return; }
    setBusy(true);
    try {
      onDone(await api.proposeChange(communityId, {
        kind, days,
        roleId: needsRole ? roleId : emptyId,
        userId: needsPerson ? userId : emptyId,
        name: needsName ? name.trim() : "",
        power: kind === "power" ? power : "",
        enabled: kind === "power" ? enabled : false
      }));
      setName("");
    }
    catch (error) { onError(failureText(error, "Не получилось предложить изменение")); }
    finally { setBusy(false); }
  }

  return (
    <form className="card stack" onSubmit={event => void submit(event)}>
      <h2>Изменение группы</h2>
      <p className="muted">Общее голосование. Варианты всегда «Принять» и «Отклонить». Старосту и куратора так назначить нельзя. Решение действует только внутри Запары.</p>
      <label className="field">Что меняем
        <select aria-label="Вид изменения" value={kind} onChange={event => setKind(event.target.value)}>
          <option value="power">Возможность роли</option>
          <option value="grant">Назначить роль</option>
          <option value="revoke_grant">Снять роль</option>
          <option value="create_role">Создать роль</option>
          <option value="rename_role">Переименовать роль</option>
          <option value="delete_role">Удалить роль</option>
          <option value="remove_member">Исключить участника</option>
        </select>
      </label>
      {needsRole && <label className="field">Роль
        <select aria-label="Роль для изменения" value={roleId} onChange={event => setRoleId(event.target.value)}>
          <option value="">Выберите роль</option>
          {roles.map(role => <option key={role.roleId} value={role.roleId}>{role.name}</option>)}
        </select>
      </label>}
      {needsPerson && <label className="field">Участник
        <select aria-label="Участник" value={userId} onChange={event => setUserId(event.target.value)}>
          <option value="">Выберите участника</option>
          {classmates.filter(person => kind !== "remove_member" || (!person.self && person.role === "member")).map(person => <option key={person.userId} value={person.userId}>{person.displayName || person.username}</option>)}
        </select>
      </label>}
      {kind === "power" && <label className="field">Возможность
        <select aria-label="Возможность" value={power} onChange={event => setPower(event.target.value)}>
          {groupPowers.map(item => <option key={item.code} value={item.code}>{item.title}</option>)}
        </select>
      </label>}
      {kind === "power" && <label className="field">Действие
        <select aria-label="Дать или забрать возможность" value={enabled ? "on" : "off"} onChange={event => setEnabled(event.target.value === "on")}>
          <option value="on">Дать</option>
          <option value="off">Забрать</option>
        </select>
      </label>}
      {needsName && <label className="field">Название
        <input value={name} onChange={event => setName(event.target.value)} maxLength={32} aria-label="Название роли" />
      </label>}
      <div className="row">
        <select className="ballot-days" aria-label="Срок изменения" value={days} onChange={event => setDays(Number(event.target.value))}>
          {Array.from({ length: 14 }, (_, index) => index + 1).map(day => <option key={day} value={day}>{dayLabel(day)}</option>)}
        </select>
        <button className="btn primary" type="submit" disabled={busy}>Предложить изменение</button>
      </div>
    </form>
  );
}

export function BallotBoardView({ communityId, board, classmates, roles, onChange, onError }: {
  communityId: string;
  board: BallotBoard;
  classmates: Classmate[];
  roles: GroupRole[];
  onChange: (board: BallotBoard) => void;
  onError: (text: string) => void;
}) {
  const [busy, setBusy] = useState("");
  async function run(id: string, action: () => Promise<BallotBoard>, fallback: string) {
    setBusy(id);
    try { onChange(await action()); }
    catch (error) { onError(failureText(error, fallback)); }
    finally { setBusy(""); }
  }

  return (
    <section className="ballots">
      <div className="card stack">
        <h2>Голосования</h2>
        <p className="muted">
          Система раз в неделю спрашивает, как прошла учёба, если в группе хотя бы три человека. Староста открывает голосование сразу.
          Общее начинается после {board.supportersNeeded} {plural(board.supportersNeeded, "подписи", "подписей", "подписей")}: в группе {board.members} {plural(board.members, "человек", "человека", "человек")}.
          Таким голосованием можно менять роли, возможности и состав группы. Одновременно идут не больше пяти голосований.
        </p>
        {board.ballots.map(ballot => (
          <BallotCard
            key={ballot.ballotId}
            ballot={ballot}
            canClose={board.canClose}
            busy={busy === ballot.ballotId}
            onSupport={() => void run(ballot.ballotId, () => api.supportBallot(communityId, ballot.ballotId), "Не получилось поддержать голосование")}
            onVote={optionId => void run(ballot.ballotId, () => api.voteBallot(communityId, ballot.ballotId, optionId), "Не получилось проголосовать")}
            onClose={() => {
              if (window.confirm("Завершить голосование до срока?")) void run(ballot.ballotId, () => api.closeBallot(communityId, ballot.ballotId), "Не получилось завершить голосование");
            }}
          />
        ))}
        {board.ballots.length === 0 && <p className="muted">Голосований пока нет.</p>}
      </div>
      <div className={board.canOpen ? "ballot-grid" : "stack"}>
        {board.canOpen && (
          <BallotForm
            title="Объявить голосование"
            hint="Откроется сразу для всей группы."
            submitLabel="Объявить"
            action={(question, options, days) => api.openHeadmanBallot(communityId, question, options, days)}
            onDone={onChange}
            onError={onError}
          />
        )}
        <BallotForm
          title="Предложить голосование"
          hint="Автор уже считается поддержавшим. Голосование откроется, когда подписей будет достаточно."
          submitLabel="Предложить"
          action={(question, options, days) => api.proposeBallot(communityId, question, options, days)}
          onDone={onChange}
          onError={onError}
        />
      </div>
      <ChangeForm communityId={communityId} classmates={classmates} roles={roles} onDone={onChange} onError={onError} />
    </section>
  );
}
