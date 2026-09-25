import { FormEvent, useState } from "react";
import * as api from "./api";
import { ballotBoardAfterMutation } from "./channels";
import { ballotDeadlineLabel, ballotStatusTitle, ballotSummary, ballotVoteTotal, filterBallots, isBallotDeadlineSoon, voteShare, type BallotBrowseFilter } from "./ballotBrowse";
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
  onDone: (board: BallotBoard) => void | Promise<void>;
  onError: (text: string) => void;
}) {
  const [draft, setDraft] = useState({ question: "", options: ["", ""], days: 5 });
  const { question, options, days } = draft;
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
      await onDone(await action(text, labels, days));
      setDraft({ question: "", options: ["", ""], days: 5 });
    }
    catch (error) { onError(failureText(error, "Не получилось сохранить голосование")); }
    finally { setBusy(false); }
  }

  return (
    <form className="card stack" onSubmit={event => void submit(event)}>
      <h2>{title}</h2>
      <p className="muted">{hint}</p>
      <label className="field">Вопрос
        <input value={question} onChange={event => setDraft(current => ({ ...current, question: event.target.value }))} maxLength={400} aria-label="Вопрос голосования" />
      </label>
      {options.map((value, index) => (
        <label className="field" key={index}>Вариант {index + 1}
          <span className="row">
            <input className="ballot-choice" value={value} onChange={event => setDraft(current => ({ ...current, options: current.options.map((item, itemIndex) => itemIndex === index ? event.target.value : item) }))} maxLength={80} aria-label={`Вариант ${index + 1}`} />
            {options.length > 2 && <button className="btn" type="button" onClick={() => setDraft(current => ({ ...current, options: current.options.filter((_, itemIndex) => itemIndex !== index) }))}>Убрать</button>}
          </span>
        </label>
      ))}
      <div className="row">
        <button className="btn" type="button" disabled={options.length >= 6} onClick={() => setDraft(current => ({ ...current, options: [...current.options, ""] }))}>Ещё вариант</button>
        <select className="ballot-days" aria-label="Срок голосования" value={days} onChange={event => setDraft(current => ({ ...current, days: Number(event.target.value) }))}>
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

function BallotCard({ ballot, canClose, busy, onSupport, onVote, onClose, onCopy }: {
  ballot: Ballot;
  canClose: boolean;
  busy: boolean;
  onSupport: () => void;
  onVote: (optionId: string) => void;
  onClose: () => void;
  onCopy: () => void;
}) {
  const total = ballotVoteTotal(ballot);
  const soon = isBallotDeadlineSoon(ballot, Date.now());
  const supportPercent = Math.min(100, Math.round(ballot.supporters / Math.max(1, ballot.supportersNeeded) * 100));
  return (
    <article className="stack ballot-card">
      <div className="row">
        <span className="chip">{originTitle(ballot.origin)}</span>
        {ballot.effect && <span className="chip">Изменение группы</span>}
        <span className="chip">{ballotStatusTitle(ballot.status)}</span>
        <span className="muted">Срок: {ballotDeadlineLabel(ballot.deadlineAt)}</span>
        {soon && <span className="chip ballot-soon">Скоро завершится</span>}
      </div>
      <b>{ballot.question}</b>
      {ballot.status === "collecting" && (
        <>
          <p className="muted">Поддержали {ballot.supporters} из {ballot.supportersNeeded}</p>
          <div className="ballot-bar" role="progressbar" aria-label="Поддержка голосования" aria-valuemin={0} aria-valuemax={100} aria-valuenow={supportPercent}>
            <span style={{ width: `${supportPercent}%` }} /></div>
          <p className="muted">Доля голосов: пока {total} {plural(total, "голос", "голоса", "голосов")}.</p>
          <div className="stack">{ballot.options.map(option => {
            const percent = voteShare(option.votes, total);
            return <div className="ballot-option" key={option.optionId}>
              <div className="row" style={{ justifyContent: "space-between" }}>
                <span>{option.label}</span>
                <span>{option.votes} {plural(option.votes, "голос", "голоса", "голосов")} · {percent}%</span>
              </div>
              <div className="ballot-bar" role="progressbar" aria-label={`Доля голосов за «${option.label}»`}
                aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}><span style={{ width: `${percent}%` }} /></div>
            </div>;
          })}</div>
          {ballot.supported ? <span className="chip">Вы поддержали</span> : <button className="btn primary" type="button" disabled={busy} onClick={onSupport}>Поддержать</button>}
        </>
      )}
      {ballot.status !== "collecting" && <p className="muted">Всего {total} {plural(total, "голос", "голоса", "голосов")}. Проценты показывают долю голосов.</p>}
      {ballot.status !== "collecting" && ballot.options.map(option => {
        const width = voteShare(option.votes, total);
        return (
          <div className="ballot-option" key={option.optionId}>
            {ballot.status === "open" ? (
              <button className={option.chosen ? "btn primary" : "btn"} type="button" disabled={busy} onClick={() => onVote(option.optionId)}>
                <span>{option.label}{option.chosen ? " · ваш выбор" : ""}</span>
                <span>{option.votes} {plural(option.votes, "голос", "голоса", "голосов")} · {width}%</span>
              </button>
            ) : (
              <div className="row" style={{ justifyContent: "space-between" }}>
                <span>{option.label}{option.chosen ? " · ваш выбор" : ""}</span>
                <span>{option.votes} {plural(option.votes, "голос", "голоса", "голосов")} · {width}%</span>
              </div>
            )}
            <div className="ballot-bar" role="progressbar" aria-label={`Доля голосов за «${option.label}»`}
              aria-valuemin={0} aria-valuemax={100} aria-valuenow={width}><span style={{ width: `${width}%` }} /></div>
          </div>
        );
      })}
      {ballot.status === "open" && <p className="muted">{ballot.effect ? "Если «Принять» победит и наберёт не меньше порога, изменение вступит в силу в конце срока. Свой выбор можно сменить." : "Свой вариант можно сменить до конца срока."}</p>}
      {ballot.status === "closed" && outcomeTitle(ballot.outcome) && <p className="muted">{outcomeTitle(ballot.outcome)}</p>}
      <button className="btn ballot-copy" type="button" onClick={onCopy}>Копировать сводку</button>
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
      <p className="muted">Общее голосование. Варианты всегда «Принять» и «Отклонить». Старосту и куратора так назначить нельзя. Решение действует только внутри «Расписание военмех».</p>
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

export function BallotBoardView({ communityId, board, classmates, roles, topicId, title, canCreate = true, onChange, onError }: {
  communityId: string;
  board: BallotBoard;
  classmates: Classmate[];
  roles: GroupRole[];
  topicId?: string;
  title?: string;
  canCreate?: boolean;
  onChange: (board: BallotBoard) => void;
  onError: (text: string) => void;
}) {
  const [busy, setBusy] = useState("");
  const [browse, setBrowse] = useState<BallotBrowseFilter>({ query: "", status: "all", sort: "default" });
  const [createOpen, setCreateOpen] = useState(false);
  const [copyNotice, setCopyNotice] = useState("");
  const visible = filterBallots(board.ballots, browse);
  const filtered = !!browse.query.trim() || browse.status !== "all" || browse.sort !== "default";
  async function refreshResult(result: BallotBoard) {
    onChange(await ballotBoardAfterMutation(result, topicId, selected => api.ballots(communityId, selected)));
  }
  async function run(id: string, action: () => Promise<BallotBoard>, fallback: string) {
    setBusy(id);
    try { await refreshResult(await action()); }
    catch (error) { onError(failureText(error, fallback)); }
    finally { setBusy(""); }
  }

  async function copySummary(ballot: Ballot) {
    try {
      await navigator.clipboard.writeText(ballotSummary(ballot));
      setCopyNotice("Сводка скопирована");
    } catch { setCopyNotice("Не удалось скопировать сводку"); }
  }

  return (
    <section className={topicId ? "ballots channel-ballots" : "ballots"}>
      <div className="card stack ballot-board-head">
        <div className="row ballot-head-row"><h2>{title || "Голосования"}</h2>
          {canCreate && <button className="btn" type="button" aria-expanded={createOpen} onClick={() => setCreateOpen(value => !value)}>
            {createOpen ? "Свернуть создание" : "Создать голосование"}</button>}
        </div>
        {topicId ? <p className="muted">Здесь только голосования группы. Выберите вариант в карточке или предложите свой вопрос.</p> : <p className="muted">
          Система раз в неделю спрашивает, как прошла учёба, если в группе хотя бы три человека. Староста открывает голосование сразу.
          Общее начинается после {board.supportersNeeded} {plural(board.supportersNeeded, "подписи", "подписей", "подписей")}: в группе {board.members} {plural(board.members, "человек", "человека", "человек")}.
          Таким голосованием можно менять роли, возможности и состав группы. Одновременно идут не больше пяти голосований.
        </p>}
        <div className="ballot-browse" role="group" aria-label="Поиск и фильтры голосований">
          <label className="field">Поиск голосования
            <input type="search" value={browse.query} onChange={event => setBrowse(current => ({ ...current, query: event.target.value }))}
              placeholder="Вопрос или вариант" />
          </label>
          <label className="field">Статус
            <select value={browse.status} onChange={event => setBrowse(current => ({ ...current, status: event.target.value as BallotBrowseFilter["status"] }))}>
              <option value="all">Все</option><option value="collecting">Сбор поддержки</option>
              <option value="open">Идёт</option><option value="closed">Завершено</option>
            </select>
          </label>
          <label className="field">Порядок
            <select value={browse.sort} onChange={event => setBrowse(current => ({ ...current, sort: event.target.value as BallotBrowseFilter["sort"] }))}>
              <option value="default">Как на доске</option><option value="nearest">Ближайший срок</option>
              <option value="farthest">Дальний срок</option>
            </select>
          </label>
        </div>
        <div className="row ballot-browse-summary"><span className="muted">На текущей доске: {visible.length} из {board.ballots.length}</span>
          {filtered && <button className="btn" type="button" onClick={() => setBrowse({ query: "", status: "all", sort: "default" })}>Сбросить фильтры</button>}
        </div>
      </div>
      {canCreate ? <div className="ballot-create stack" hidden={!createOpen}>
        <div className={board.canOpen ? "ballot-grid" : "stack"}>
          {board.canOpen && <BallotForm
            title="Объявить голосование"
            hint="Откроется сразу для всей группы."
            submitLabel="Объявить"
            action={(question, options, days) => api.openHeadmanBallot(communityId, question, options, days, topicId)}
            onDone={refreshResult}
            onError={onError}
          />}
          <BallotForm
            title="Предложить голосование"
            hint="Автор уже считается поддержавшим. Голосование откроется, когда подписей будет достаточно."
            submitLabel="Предложить"
            action={(question, options, days) => api.proposeBallot(communityId, question, options, days, topicId)}
            onDone={refreshResult}
            onError={onError}
          />
        </div>
        {!topicId && <ChangeForm communityId={communityId} classmates={classmates} roles={roles} onDone={onChange} onError={onError} />}
      </div> : <p className="muted">Создавать голосования здесь могут только управляющие разделами.</p>}
      <div className="card stack ballot-list">
        {copyNotice && <p className="muted" role="status">{copyNotice}</p>}
        {visible.map(ballot => (
          <BallotCard
            key={ballot.ballotId}
            ballot={ballot}
            canClose={board.canClose}
            busy={busy === ballot.ballotId}
            onSupport={() => void run(ballot.ballotId, () => api.supportBallot(communityId, ballot.ballotId), "Не получилось поддержать голосование")}
            onVote={optionId => void run(ballot.ballotId, () => api.voteBallot(communityId, ballot.ballotId, optionId), "Не получилось проголосовать")}
            onCopy={() => void copySummary(ballot)}
            onClose={() => {
              if (window.confirm("Завершить голосование до срока?")) void run(ballot.ballotId, () => api.closeBallot(communityId, ballot.ballotId), "Не получилось завершить голосование");
            }}
          />
        ))}
        {board.ballots.length === 0 && <div className="empty">Голосований пока нет.
          {canCreate && <button className="btn" type="button" onClick={() => setCreateOpen(true)}>Создать голосование</button>}</div>}
        {board.ballots.length > 0 && visible.length === 0 && <div className="empty">На текущей доске совпадений нет.
          <button className="btn" type="button" onClick={() => setBrowse({ query: "", status: "all", sort: "default" })}>Показать все</button></div>}
      </div>
    </section>
  );
}
