import { moveQuestion, changeQuestionKind, questionWire, questionFilled } from "./form-draft";
import { FormEvent, useEffect, useRef, useState, type SetStateAction } from "react";
import * as api from "./api";
import { canonicalUtc, localDateTimeInput } from "./utc";
import { HomeworkRequestScope, scopedValue, subjectHomework } from "./homework-request-scope";
import { HomeworkRecipients, audienceLabel, allHomeworkAudience, useHomeworkAudienceData } from "./homework-audience";
import { createMaterialHistory, type MaterialHistory } from "./material-history";
import { draftKey, revokeGroupDrafts, useStoredDraft } from "./draft-store";
import { scopeLease, scopeLeaseValid } from "./draft-revocation";
import { useApp } from "./store";
import { addDays, isoDay, lessonsOn, sameSubject } from "./parity";
import { visibleLessons } from "./subgroups";
import { useCommunityTimetable } from "./use-community-timetable";
import { buildGroupChatContext } from "./groupChatContext";
import { absoluteDate, localDay } from "./planner";
import { groupMediaDownload } from "./group-media";
import { GroupInlineMedia } from "./group-inline-media";
import { formResponsesCsv } from "./form-export";
import { validateFormAnswers, validateFormQuestions, requiredFormProgress } from "./forms";
import type { ChatMessage, FormAnswer, FormQuestion, FormResponse, GroupForm, GroupHomeworkCopy, GroupTopic, HomeworkAudience } from "./types";
export function SpecializedChannel({ communityId, conversationId, groupName, topic, onError }: {
    communityId: string;
    conversationId: string;
    groupName: string | null;
    topic: GroupTopic;
    onError: (text: string) => void;
}) {
    if (topic.supported === false)
        return <p className="banner">Этот тип канала не поддерживается. Обновите приложение.</p>;
    if (!topic.topicId)
        return null;
    if (topic.kind === "forms")
        return <FormsChannel key={topic.topicId} communityId={communityId} topic={topic} onError={onError}/>;
    if (topic.kind === "materials")
        return <MaterialsChannel key={topic.topicId} communityId={communityId} conversationId={conversationId} topic={topic} onError={onError}/>;
    if (topic.kind === "homework")
        return <HomeworkChannel key={topic.topicId} communityId={communityId} topic={topic} onError={onError}/>;
    if (topic.kind === "schedule")
        return <ScheduleChannel groupName={groupName} topic={topic}/>;
    return topic.kind === "ballots" ? null : <p className="banner">Этот тип канала не поддерживается. Обновите приложение.</p>;
}
const can = (topic: GroupTopic, power: string) => !topic.archived && topic.supported !== false && !!topic.permissions?.includes(power);
const problem = (error: unknown) => error instanceof Error && (error.message === "403" || error.message === "404") ? "Доступ к каналу изменился" : "Не удалось загрузить или сохранить данные. Ввод сохранён.";
function emptyQuestion(): FormQuestion { return { questionId: crypto.randomUUID(), title: "", kind: "shortText", required: true, options: [] }; }
const questionTitles = { shortText: "Короткий текст", longText: "Развёрнутый текст", singleChoice: "Один вариант", multipleChoice: "Несколько вариантов" };
function FormsChannel({ communityId, topic, onError }: {
    communityId: string;
    topic: GroupTopic;
    onError: (text: string) => void;
}) {
    const [forms, setForms] = useState<GroupForm[] | null>(null);
    const app = useApp();
    const [createDraft, , clearCreateDraft, setCreateField] = useStoredDraft(draftKey(app.session?.user?.userId, communityId, topic.topicId!, "form-create"), () => ({ creating: false, title: "", description: "", deadline: "", anonymous: false, questions: [emptyQuestion()] }));
    const { creating, title, description, deadline, anonymous, questions } = createDraft;
    const setCreating = (next: SetStateAction<boolean>) => setCreateField("creating", next);
    const setTitle = (next: string) => setCreateField("title", next);
    const setDescription = (next: string) => setCreateField("description", next);
    const setDeadline = (next: string) => setCreateField("deadline", next);
    const setAnonymous = (next: boolean) => setCreateField("anonymous", next);
    const setQuestions = (next: SetStateAction<FormQuestion[]>) => setCreateField("questions", next);
    const [busy, setBusy] = useState(false);
    const [retry, setRetry] = useState(0);
    useEffect(() => {
        let stop = false;
        const pull = () => void api.groupForms(communityId, topic.topicId!).then(value => {
            if (!stop)
                setForms(value.forms);
        }).catch(error => {
            if (!stop) {
                if (error instanceof Error && ["401", "403", "404"].includes(error.message)) {
                    revokeGroupDrafts(error.message === "401" ? { owner: app.session?.user?.userId || "guest" } : { owner: app.session?.user?.userId || "guest", community: communityId, topic: topic.topicId! });
                    setForms([]);
                }
                onError(problem(error));
            }
        });
        pull();
        const timer = window.setInterval(pull, 4000);
        return () => { stop = true; window.clearInterval(timer); };
    }, [communityId, topic.topicId, retry]);
    async function create(event: FormEvent) {
        event.preventDefault();
        if (busy || !can(topic, "forms"))
            return;
        const validation = validateFormQuestions(title, questions, deadline);
        if (validation) {
            onError(validation);
            return;
        }
        const sentDraft = createDraft;
        setBusy(true);
        try {
            const next = await api.createGroupForm(communityId, topic.topicId!, { title: title.trim(), description: description.trim(), deadlineAt: deadline ? canonicalUtc(deadline) : null, anonymous, questions: questions.map(questionWire) });
            setForms(next.forms);
            clearCreateDraft(sentDraft);
        }
        catch (error) {
            onError(problem(error));
        }
        finally {
            setBusy(false);
        }
    }
    return <div className="stack"><div className="row"><h2>Анкеты</h2><button className="btn quiet" type="button" onClick={() => setRetry(value => value + 1)}>Обновить</button>{can(topic, "forms") && <button className="btn primary" type="button" onClick={() => setCreating(value => !value)}>{creating ? "Скрыть редактор" : "Создать анкету"}</button>}</div>
    {creating && can(topic, "forms") && <form className="card stack" onSubmit={event => void create(event)}><label className="field">Название<input required maxLength={200} value={title} onChange={event => setTitle(event.target.value)}/></label><label className="field">Описание<textarea maxLength={2000} value={description} onChange={event => setDescription(event.target.value)}/></label><label className="field">Принимать ответы до<input type="datetime-local" value={deadline} onChange={event => setDeadline(event.target.value)}/></label><label className="check"><input type="checkbox" checked={anonymous} onChange={event => setAnonymous(event.target.checked)}/>Анонимные ответы</label>
      {questions.map((question, index) => <fieldset className="stack card" key={question.questionId}><legend>Вопрос {index + 1}</legend><input aria-label={`Текст вопроса ${index + 1}`} required maxLength={400} value={question.title} onChange={event => setQuestions(rows => rows.map(row => row.questionId === question.questionId ? { ...row, title: event.target.value } : row))}/><select aria-label={`Тип вопроса ${index + 1}`} value={question.kind} onChange={event => setQuestions(rows => rows.map(row => row.questionId === question.questionId ? changeQuestionKind(row, event.target.value as FormQuestion["kind"]) : row))}>{Object.entries(questionTitles).map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select><label className="check"><input type="checkbox" checked={question.required} onChange={event => setQuestions(rows => rows.map(row => row.questionId === question.questionId ? { ...row, required: event.target.checked } : row))}/>Обязательный</label>
      {["singleChoice", "multipleChoice"].includes(question.kind) && question.options.map((option, i) => <div className="row" key={i}><input aria-label={`Вариант ${i + 1} вопроса ${index + 1}`} value={option} maxLength={160} required onChange={event => setQuestions(rows => rows.map(row => row.questionId === question.questionId ? { ...row, options: row.options.map((text, j) => j === i ? event.target.value : text) } : row))}/><button className="btn quiet" disabled={question.options.length <= 2} type="button" onClick={() => setQuestions(rows => rows.map(row => row.questionId === question.questionId ? { ...row, options: row.options.filter((_, j) => i !== j) } : row))}>Убрать</button></div>)}
      {["singleChoice", "multipleChoice"].includes(question.kind) && question.options.length > 0 && <button className="btn" disabled={question.options.length >= 10} type="button" onClick={() => setQuestions(rows => rows.map(row => row.questionId === question.questionId ? { ...row, options: [...row.options, ""] } : row))}>Добавить вариант</button>}<button className="btn quiet" disabled={questions.length <= 1} type="button" onClick={() => { if (!questionFilled(question) || window.confirm(`Убрать вопрос «${question.title || "Без названия"}» и его варианты?`)) setQuestions(rows => rows.filter(row => row.questionId !== question.questionId)); }}>Убрать вопрос</button><button className="btn quiet" type="button" disabled={index === 0} onClick={() => setQuestions(rows => moveQuestion(rows, question.questionId, -1))}>Выше</button><button className="btn quiet" type="button" disabled={index === questions.length - 1} onClick={() => setQuestions(rows => moveQuestion(rows, question.questionId, 1))}>Ниже</button></fieldset>)}
      <button className="btn" disabled={questions.length >= 20} type="button" onClick={() => setQuestions(rows => [...rows, emptyQuestion()])}>Добавить вопрос</button><p className="muted">После публикации вопросы и анонимность сохраняются. Ответы видит автор анкеты; личность в анонимной анкете скрыта.</p><button className="btn primary" disabled={busy}>{busy ? "Публикуем…" : "Опубликовать"}</button><button className="btn quiet" type="button" disabled={busy} onClick={() => {
                if (window.confirm("Удалить несохранённый черновик анкеты?"))
                    clearCreateDraft(createDraft);
            }}>Удалить черновик</button></form>}
    {forms === null ? <p role="status">Загрузка анкет…</p> : forms.length === 0 ? <p className="muted">Анкет пока нет</p> : forms.map(form => <FormCard key={form.formId} communityId={communityId} form={form} writable={can(topic, "formsRespond")} onUpdate={next => setForms(rows => rows?.map(row => row.formId === next.formId ? next : row) ?? [next])} onError={onError}/>)}
  </div>;
}
function FormCard({ communityId, form, writable, onUpdate, onError }: {
    communityId: string;
    form: GroupForm;
    writable: boolean;
    onUpdate: (form: GroupForm) => void;
    onError: (text: string) => void;
}) {
    const app = useApp();
    const [answers, setAnswers, clearAnswerDraft, , adoptAnswers] = useStoredDraft<FormAnswer[]>(draftKey(app.session?.user?.userId, communityId, form.topicId, "form-answer", form.formId), () => form.ownResponse?.answers ?? []);
    useEffect(() => { adoptAnswers(form.ownResponse?.answers ?? []); }, [form.formId, form.ownResponse?.updatedAt]);
    const [responses, setResponses] = useState<FormResponse[] | null>(null);
    const [nextCursor, setNextCursor] = useState<string | null>(null);
    const [totalResponses, setTotalResponses] = useState(0);
    const [busy, setBusy] = useState(false);
    const responseScope = { owner: app.session?.user?.userId || "guest", community: communityId, topic: form.topicId };
    const responseLease = scopeLease(sessionStorage, responseScope);
    const editable = writable && form.canRespond;
    const progress = requiredFormProgress(form.questions, answers);
    const formElement = useRef<HTMLFormElement>(null);
    function focusMissing() {
        if (!progress.firstMissing) return;
        const index = form.questions.findIndex(question => question.questionId === progress.firstMissing?.questionId);
        formElement.current?.querySelectorAll<HTMLFieldSetElement>("fieldset")[index]?.querySelector<HTMLElement>("input, textarea")?.focus();
    }
    function answer(id: string): FormAnswer { return answers.find(row => row.questionId === id) ?? { questionId: id, text: null, choices: [] }; }
    function update(value: FormAnswer) { setAnswers(rows => [...rows.filter(row => row.questionId !== value.questionId), value]); }
    async function submit(event: FormEvent) {
        event.preventDefault();
        if (!editable || busy)
            return;
        const validation = validateFormAnswers(form.questions, answers);
        if (validation) {
            onError(validation);
            focusMissing();
            return;
        }
        const sentAnswers = answers;
        setBusy(true);
        try {
            const saved = await api.submitGroupForm(communityId, form.formId, sentAnswers);
            clearAnswerDraft(sentAnswers, saved.ownResponse?.answers ?? sentAnswers);
            onUpdate(saved);
        }
        catch (error) {
            onError(problem(error));
        }
        finally {
            setBusy(false);
        }
    }
    async function exportResponses() {
        if (busy || !form.canViewResponses)
            return;
        setBusy(true);
        try {
            let after: string | undefined;
            const rows: FormResponse[] = [];
            const cursors = new Set<string>();
            do {
                const page = await api.groupFormResponses(communityId, form.formId, after);
                rows.push(...page.responses);
                after = page.nextCursor || undefined;
                if (after) {
                    if (cursors.has(after))
                        throw new Error("invalid cursor");
                    cursors.add(after);
                }
            } while (after);
            const url = URL.createObjectURL(new Blob([formResponsesCsv(form.questions, rows)], { type: "text/csv;charset=utf-8" }));
            const anchor = document.createElement("a");
            anchor.href = url;
            anchor.download = `anketa-${form.formId}.csv`;
            anchor.click();
            window.setTimeout(() => URL.revokeObjectURL(url), 60000);
        }
        catch (error) {
            onError(problem(error));
        }
        finally {
            setBusy(false);
        }
    }
    return <article className="card stack"><h2>{form.title}</h2><p>{form.description}</p><p className="muted">{form.anonymous ? "Анонимная" : "Именная"} · Ответов: {form.responseCount}{form.deadlineAt && ` · До ${new Date(form.deadlineAt).toLocaleString("ru-RU")}`}</p>
    {editable && <div className="row"><span className="muted" role="status">Обязательные ответы: {progress.answered} из {progress.total}{progress.firstMissing ? ` · осталось: ${progress.firstMissing.title}` : ""}</span>{progress.firstMissing && <button className="btn quiet" type="button" disabled={busy} onClick={focusMissing}>К первому пропуску</button>}</div>}
    <form ref={formElement} noValidate className="stack" onSubmit={event => void submit(event)}>{form.questions.map(question => <fieldset key={question.questionId} className="form-question" disabled={!editable || busy}><legend>{question.title}{question.required ? " *" : ""}</legend>{question.kind === "shortText" ? <input aria-label={question.title} required={question.required} maxLength={1000} value={answer(question.questionId).text || ""} onChange={event => update({ ...answer(question.questionId), text: event.target.value })}/> : question.kind === "longText" ? <textarea aria-label={question.title} required={question.required} maxLength={4000} value={answer(question.questionId).text || ""} onChange={event => update({ ...answer(question.questionId), text: event.target.value })}/> : question.options.map(option => <label className="check" key={option}><input type={question.kind === "singleChoice" ? "radio" : "checkbox"} name={form.formId + question.questionId} checked={answer(question.questionId).choices.includes(option)} onChange={event => update({ ...answer(question.questionId), choices: question.kind === "singleChoice" ? [option] : event.target.checked ? [...answer(question.questionId).choices, option] : answer(question.questionId).choices.filter(value => value !== option) })}/>{option}</label>)}</fieldset>)}{editable ? <button className="btn primary" disabled={busy}>{busy ? "Сохраняем…" : form.ownResponse ? "Обновить мой ответ" : "Ответить"}</button> : <p className="muted">{form.ownResponse ? "Ваш ответ сохранён" : "Приём ответов недоступен"}</p>}</form>
    {form.canViewResponses && <button className="btn quiet" type="button" disabled={busy} onClick={() => void exportResponses()}>Выгрузить ответы CSV</button>}
    {form.canViewResponses && <button className="btn" type="button" disabled={busy} onClick={() => {
                setBusy(true);
                void api.groupFormResponses(communityId, form.formId).then(value => { if (!scopeLeaseValid(sessionStorage, responseScope, responseLease))
                    return; setResponses(value.responses); setNextCursor(value.nextCursor); setTotalResponses(value.totalResponses); }).catch(error => {
                    if (error instanceof Error && ["403", "404"].includes(error.message))
                        setResponses(null);
                    onError(problem(error));
                }).finally(() => setBusy(false));
            }}>Ответы участников</button>}
    {responses && form.canViewResponses && <div className="stack"><h2>Ответы · {responses.length} из {totalResponses}</h2>{nextCursor && <button className="btn" type="button" disabled={busy} onClick={() => {
                    setBusy(true);
                    void api.groupFormResponses(communityId, form.formId, nextCursor).then(value => { if (!scopeLeaseValid(sessionStorage, responseScope, responseLease))
                        return; setResponses(rows => [...(rows ?? []), ...value.responses]); setNextCursor(value.nextCursor); setTotalResponses(value.totalResponses); }).catch(error => {
                        if (error instanceof Error && ["403", "404"].includes(error.message))
                            setResponses(null);
                        onError(problem(error));
                    }).finally(() => setBusy(false));
                }}>Загрузить ещё</button>}{responses.map((response, i) => <div className="card" key={i}><p>{response.respondentId || "Анонимный ответ"} · {new Date(response.updatedAt).toLocaleString("ru-RU")}</p>{response.answers.map(row => <p key={row.questionId}><b>{form.questions.find(question => question.questionId === row.questionId)?.title}</b>: {row.text || row.choices.join(", ") || "Нет ответа"}</p>)}</div>)}</div>}
  </article>;
}
export function ScheduleChannel({ groupName, topic, initialDate }: {
    groupName: string | null;
    topic: GroupTopic;
    initialDate?: Date;
}) {
    const app = useApp();
    const timetable = useCommunityTimetable(groupName);
    const [selectedDay, setSelectedDay] = useState(() => new Date(initialDate ?? app.date));
    const days = Array.from({ length: 7 }, (_, i) => addDays(selectedDay, i));
    const payload = timetable.payload;
    if (!payload)
        return <div className="stack"><p role={timetable.error ? "alert" : "status"}>{timetable.error || "Загружаем расписание группы…"}</p><button className="btn" type="button" disabled={timetable.loading} onClick={timetable.reload}>Повторить</button></div>;
    return <div className="stack"><div className="row"><h2>{payload.group.name}</h2><button className="btn quiet" type="button" disabled={timetable.loading} onClick={timetable.reload}>{timetable.loading ? "Обновляем…" : "Обновить"}</button></div>{timetable.error && <p className="banner" role="status">{timetable.error}</p>}<p className="muted">Копия группы: {new Date(payload.meta.fetchedAt).toLocaleString("ru-RU")}</p><div className="row"><button className="btn quiet" type="button" onClick={() => setSelectedDay(date => addDays(date, -7))}>Предыдущие дни</button><button className="btn quiet" type="button" onClick={() => setSelectedDay(new Date())}>Сегодня</button><button className="btn quiet" type="button" onClick={() => setSelectedDay(date => addDays(date, 7))}>Следующие дни</button></div>{days.map(date => { const outside = isoDay(date) < payload.period.start.slice(0, 10); const lessons = outside ? [] : lessonsOn(visibleLessons(payload.lessons, app.subgroups[payload.group.id] || {}), date, payload.period.start, payload.period.weekCount, app.invert).filter(lesson => !topic.subject || sameSubject(lesson.subjectRaw, topic.subject)); return <article className="card" key={isoDay(date)}><h2>{absoluteDate(date)}</h2>{lessons.map((lesson, i) => <p key={i}>{lesson.timeStart}–{lesson.timeEnd} · {lesson.subjectRaw} · {lesson.roomRaw || "Аудитория не указана"}</p>)}{outside ? <p className="muted">Дата вне известного учебного периода · нет данных</p> : lessons.length === 0 && <p className="muted">Без пар</p>}</article>; })}</div>;
}
function HomeworkChannel({ communityId, topic, onError }: {
    communityId: string;
    topic: GroupTopic;
    onError: (text: string) => void;
}) {
    const [items, setItems] = useState<GroupHomeworkCopy[] | null>(null);
    const app = useApp();
    const recipients = useHomeworkAudienceData(communityId);
    type ChannelDraft = { title: string; body: string; deadline: string; audience: HomeworkAudience; operationId: string; attemptedSnapshot: string };
    const [homeworkDraft, setHomeworkDraft, clearHomeworkDraft, setHomeworkField] = useStoredDraft<ChannelDraft>(draftKey(app.session?.user?.userId, communityId, topic.topicId!, "homework-create"), () => ({ title: topic.subject || "", body: "", deadline: "", audience: allHomeworkAudience(), operationId: "", attemptedSnapshot: "" }));
    const { title, body, deadline } = homeworkDraft;
    const draftAudience = homeworkDraft.audience || allHomeworkAudience();
    const setTitle = (next: string) => setHomeworkField("title", next);
    const setBody = (next: string) => setHomeworkField("body", next);
    const setDeadline = (next: string) => setHomeworkField("deadline", next);
    const [busy, setBusy] = useState(false);
    const busyRef = useRef(false);
    const requestRef = useRef(0);
    const [error, setError] = useState("");
    const [filter, setFilter] = useState<"active" | "done" | "all">("active");
    const [editing, setEditing] = useState<{ id: string; revision: number; title: string; body: string; deadline: string; audience: HomeworkAudience } | null>(null);
    const [editConflict, setEditConflict] = useState(false);
    function refresh() {
        const request = ++requestRef.current;
        return api.groupHomework(communityId, topic.topicId!).then(value => {
            if (request === requestRef.current && !busyRef.current) setItems(value);
        }).catch(error => {
            if (request !== requestRef.current) return;
            if (error instanceof Error && ["401", "403", "404"].includes(error.message))
                revokeGroupDrafts(error.message === "401" ? { owner: app.session?.user?.userId || "guest" } : { owner: app.session?.user?.userId || "guest", community: communityId, topic: topic.topicId! });
            setError("Задания не загрузились. Повторите запрос.");
            onError(problem(error));
        });
    }
    useEffect(() => {
        let stop = false;
        setItems(null);
        void refresh();
        const timer = window.setInterval(() => { if (!stop && !busyRef.current) void refresh(); }, 4000);
        return () => { stop = true; ++requestRef.current; window.clearInterval(timer); };
    }, [communityId, topic.topicId]);
    const visible = (items ?? []).filter(item => item.topicId === topic.topicId && (item.homeworkId === editing?.id || filter === "all" || item.completed === (filter === "done")));
    async function publish(event: FormEvent) {
        event.preventDefault();
        if (busyRef.current || recipients.loading) return;
        if (draftAudience.kind === "selected" && (!recipients.supported || !draftAudience.roleIds.length && !draftAudience.userIds.length)) { setError("Выберите получателей. Адресная отправка требует обновлённый сервер."); return; }
        const snapshot = JSON.stringify([title.trim(), body.trim(), deadline ? canonicalUtc(deadline) : null, topic.topicId, draftAudience]);
        if (homeworkDraft.attemptedSnapshot && homeworkDraft.attemptedSnapshot !== snapshot) { setError("Предыдущая отправка не подтверждена. Верните прежний текст, срок и получателей для повтора либо удалите черновик и создайте новую публикацию."); return; }
        if (homeworkDraft.attemptedSnapshot && !recipients.supported) { setError("Предыдущая отправка не подтверждена. Проверьте общую домашку перед новой публикацией на этом сервере."); return; }
        const sentDraft = { ...homeworkDraft, audience: draftAudience, operationId: homeworkDraft.operationId || crypto.randomUUID(), attemptedSnapshot: snapshot };
        setHomeworkDraft(sentDraft);
        busyRef.current = true; ++requestRef.current; setBusy(true); setError("");
        try {
            await api.shareHomework(communityId, title.trim(), body.trim(), deadline ? canonicalUtc(deadline) : null, topic.topicId, draftAudience.kind === "selected" ? draftAudience : undefined, recipients.supported ? sentDraft.operationId : undefined);
            clearHomeworkDraft(sentDraft);
            busyRef.current = false;
            await refresh();
        } catch (cause) { setError(cause instanceof Error && cause.message === "409" ? "Публикация с этим номером отличается от сохранённой. Проверьте исходный черновик." : "Отправка не подтверждена. Повторите сохранение — номер публикации сохранён."); }
        finally { busyRef.current = false; setBusy(false); }
    }
    async function mark(item: GroupHomeworkCopy) {
        if (busyRef.current || item.canComplete === false) return;
        busyRef.current = true; ++requestRef.current; setBusy(true); setError("");
        try {
            const saved = await api.completeHomework(communityId, item.homeworkId, !item.completed, item.completionRevision);
            setItems(rows => rows?.map(row => row.homeworkId === item.homeworkId ? { ...row, completed: saved.completed, completionRevision: saved.revision } : row) ?? []);
        } catch (cause) { setError(cause instanceof Error && cause.message === "409" ? "Отметка изменилась. Загрузите актуальное состояние." : "Отметку не удалось сохранить."); if (cause instanceof Error && cause.message === "409") { busyRef.current = false; await refresh(); } }
        finally { busyRef.current = false; setBusy(false); }
    }
    async function saveEdit(event: FormEvent) {
        event.preventDefault();
        if (!editing || busyRef.current || editConflict) return;
        if (editing.audience.kind === "selected" && !recipients.supported) { setError("Адресная домашка недоступна на этом сервере."); return; }
        busyRef.current = true; ++requestRef.current; setBusy(true); setError("");
        try {
            await api.editHomework(communityId, editing.id, { title: editing.title.trim(), body: editing.body.trim(), deadlineAt: editing.deadline ? canonicalUtc(editing.deadline) : null, topicId: topic.topicId, ...(recipients.supported ? { audience: editing.audience } : {}) }, editing.revision);
            setEditing(null); busyRef.current = false; await refresh();
        } catch (cause) { if (cause instanceof Error && cause.message === "409") { setEditConflict(true); busyRef.current = false; await refresh(); setError("Задание изменилось на сервере. Ваш черновик сохранён; сверьте его с актуальной версией."); } else setError("Изменения не сохранились. Черновик сохранён."); }
        finally { busyRef.current = false; setBusy(false); }
    }
    return <div className="stack">{can(topic, "homework") && <form className="card stack" onSubmit={event => void publish(event)}><h2>Задание в канале</h2><fieldset className="stack" disabled={busy}><label className="field">Предмет<input required value={title} onChange={event => setTitle(event.target.value)}/></label><label className="field">Задание<textarea required value={body} onChange={event => setBody(event.target.value)}/></label><label className="field">Срок<input type="datetime-local" value={deadline} onChange={event => setDeadline(event.target.value)}/></label><HomeworkRecipients communityId={communityId} value={draftAudience} onChange={audience => setHomeworkField("audience", audience)} data={recipients}/><p className="muted">Канал: {topic.title} · {audienceLabel(draftAudience)} · {deadline ? new Date(deadline).toLocaleString("ru-RU") : "Без срока"}</p><div className="row"><button className="btn primary">{busy ? "Сохраняем…" : "Опубликовать"}</button><button className="btn quiet" type="button" onClick={() => { clearHomeworkDraft(homeworkDraft); setError(""); }}>Удалить черновик</button></div></fieldset></form>}
    {error && <div className="banner row" role="alert"><span>{error}</span><button className="btn" type="button" disabled={busy} onClick={() => void refresh()}>Обновить задания</button></div>}
    <div className="row homework-filters" role="group" aria-label="Показать задания"><button className="btn" type="button" aria-pressed={filter === "active"} onClick={() => setFilter("active")}>Активные</button><button className="btn" type="button" aria-pressed={filter === "done"} onClick={() => setFilter("done")}>Готово у меня</button><button className="btn" type="button" aria-pressed={filter === "all"} onClick={() => setFilter("all")}>Все</button></div>
    {items === null ? <p role="status">Загрузка заданий…</p> : visible.length === 0 ? <p className="muted">Заданий в этом разделе пока нет.</p> : visible.map(item => <article className="card stack" key={item.homeworkId}><div className="row"><h2>{item.title}</h2><span className="chip">{audienceLabel(item.audience)}</span></div><p className={item.completed ? "done-title" : ""}>{item.body}</p><p className="muted">{item.deadlineAt ? new Date(item.deadlineAt).toLocaleString("ru-RU") : "Без срока"}</p><div className="row">{item.canComplete !== false && <button className="btn" type="button" disabled={busy || !!topic.archived} onClick={() => void mark(item)}>{item.completed ? "Снять отметку" : "Готово у меня"}</button>}{item.canEdit && !topic.archived && <button className="btn quiet" type="button" disabled={busy} onClick={() => { setEditing({ id: item.homeworkId, revision: item.revision, title: item.title, body: item.body, deadline: localDateTimeInput(item.deadlineAt), audience: item.audience || allHomeworkAudience() }); setEditConflict(false); }}>Изменить публикацию</button>}</div>
      {editing?.id === item.homeworkId && <form className="stack" onSubmit={event => void saveEdit(event)}><label className="field">Предмет<input required value={editing.title} onChange={event => setEditing({ ...editing, title: event.target.value })}/></label><label className="field">Задание<textarea required value={editing.body} onChange={event => setEditing({ ...editing, body: event.target.value })}/></label><label className="field">Срок<input type="datetime-local" value={editing.deadline} onChange={event => setEditing({ ...editing, deadline: event.target.value })}/></label><HomeworkRecipients communityId={`${communityId}-edit`} value={editing.audience} onChange={audience => setEditing({ ...editing, audience })} disabled={busy} data={recipients}/>{editConflict && <div className="banner" role="alert">Актуальная ревизия: {items.find(row => row.homeworkId === item.homeworkId)?.revision}. Сверьте поля и подтвердите использование новой ревизии.<button className="btn" type="button" onClick={() => { setEditing({ ...editing, revision: items.find(row => row.homeworkId === item.homeworkId)?.revision ?? editing.revision }); setEditConflict(false); }}>Использовать актуальную ревизию</button></div>}<div className="row"><button className="btn primary" disabled={busy || editConflict}>Сохранить изменения</button><button className="btn quiet" type="button" onClick={() => { setEditing(null); setEditConflict(false); }}>Отмена</button></div></form>}</article>)}
  </div>;
}
function MaterialsChannel({ communityId, conversationId, topic, onError }: {
    communityId: string;
    conversationId: string;
    topic: GroupTopic;
    onError: (text: string) => void;
}) {
    const [history, setHistory] = useState<MaterialHistory>({ messages: null, hasOlder: false, loadingOlder: false });
    const historyRef = useRef<ReturnType<typeof createMaterialHistory> | null>(null);
    const messages = history.messages;
    const app = useApp();
    const [link, setLink, clearLinkDraft] = useStoredDraft<string>(draftKey(app.session?.user?.userId, communityId, topic.topicId!, "material-link"), () => "");
    const [busy, setBusy] = useState(false);
    useEffect(() => {
        const source = createMaterialHistory(cursor => api.messages(conversationId, topic.topicId!, cursor), setHistory, error => { if (error instanceof Error && ["401", "403", "404"].includes(error.message))
            revokeGroupDrafts(error.message === "401" ? { owner: app.session?.user?.userId || "guest" } : { owner: app.session?.user?.userId || "guest", community: communityId, topic: topic.topicId! }); onError(problem(error)); });
        historyRef.current = source;
        void source.poll();
        const timer = window.setInterval(() => void source.poll(), 4000);
        return () => {
            source.dispose();
            if (historyRef.current === source)
                historyRef.current = null;
            window.clearInterval(timer);
        };
    }, [conversationId, topic.topicId]);
    async function save(action: () => Promise<ChatMessage>, linkSubmission = false) {
        if (busy)
            return;
        const sentLink = link;
        setBusy(true);
        try {
            const message = await action();
            historyRef.current?.append(message);
            if (linkSubmission)
                clearLinkDraft(sentLink);
        }
        catch (error) {
            onError(problem(error));
        }
        finally {
            setBusy(false);
        }
    }
    const writable = can(topic, "post");
    return <div className="stack"><h2>Файлы и ссылки</h2>{writable && <><form className="row" onSubmit={event => {
                event.preventDefault();
                try {
                    const url = new URL(link);
                    if (!["http:", "https:"].includes(url.protocol))
                        throw new Error();
                    void save(() => api.sendTopicMessage(conversationId, url.href, topic.topicId), true);
                }
                catch {
                    onError("Введите ссылку https:// или http://");
                }
            }}><input type="url" aria-label="Ссылка на материал" placeholder="https://" required value={link} onChange={event => setLink(event.target.value)}/><button className="btn primary" disabled={busy}>Добавить ссылку</button></form>{can(topic, "media") && <label className="btn file">Добавить файл<input type="file" disabled={busy} onChange={event => {
                    const file = event.target.files?.[0];
                    event.target.value = "";
                    if (file)
                        void save(() => api.sendGroupMedia(conversationId, file.type.startsWith("image/") ? "image" : "file", file.name, file, undefined, undefined, topic.topicId!));
                }}/></label>}</>}
    {history.hasOlder && <button className="btn" type="button" disabled={history.loadingOlder} onClick={() => void historyRef.current?.earlier()}>{history.loadingOlder ? "Загружаем ранние материалы…" : "Загрузить ранние материалы"}</button>}
    {history.error && <div className="banner" role="alert">Материалы не загрузились. Ранее загруженные материалы сохранены.<button className="btn" disabled={history.loading} onClick={() => void historyRef.current?.poll()}>Повторить загрузку</button></div>}
    {messages !== null && !history.error && messages.every(message => message.deleted) && <p className="muted">Материалов пока нет.{writable ? " Добавьте ссылку или файл выше." : " Здесь появятся опубликованные файлы и ссылки."}</p>}
    {messages === null ? history.loading ? <p role="status">Загрузка материалов…</p> : null : messages.filter(message => !message.deleted).map(message => { const media = groupMediaDownload(conversationId, message); return <article className="card" key={message.messageId}><p className="muted">{message.senderName} · {new Date(message.createdAt).toLocaleString("ru-RU")}</p>{media ? <GroupInlineMedia key={message.messageId} download={media} busy={busy} onDownload={() => { setBusy(true); void api.groupMedia(media).then(blob => { const url = URL.createObjectURL(blob); const anchor = document.createElement("a"); anchor.href = url; anchor.download = media.filename; anchor.click(); window.setTimeout(() => URL.revokeObjectURL(url), 60000); }).catch(() => onError("Файл не загрузился")).finally(() => setBusy(false)); }}/> : /^https?:\/\/\S+$/.test(message.body) ? <a href={message.body} target="_blank" rel="noopener noreferrer">{message.body}</a> : <p>{message.body}</p>}</article>; })}
  </div>;
}
export function SubjectChannelContext({ communityId, groupName, topic, onError }: {
    communityId: string;
    groupName: string | null;
    topic: GroupTopic;
    onError: (text: string) => void;
}) {
    const app = useApp();
    const timetable = useCommunityTimetable(groupName);
    const [selectedIso, setSelectedIso] = useStoredDraft(draftKey(app.session?.user?.userId, communityId, topic.topicId ?? "general", "subject-day"), () => isoDay(app.date));
    const selected = localDay(selectedIso) ?? app.date;
    const [panel, setPanel] = useState<"overview" | "schedule" | "homework">("overview");
    const [homework, setHomework] = useState<GroupHomeworkCopy[] | null>(null);
    const [homeworkScopeTag, setHomeworkScopeTag] = useState("");
    const [homeworkError, setHomeworkError] = useState("");
    const [markBusy, setMarkBusy] = useState(false);
    const subjectScopeKey = JSON.stringify([app.session?.user?.userId, communityId, topic.topicId, topic.subject]);
    const subjectRequests = useRef(new HomeworkRequestScope()).current;
    subjectRequests.scope(subjectScopeKey);
    const visibleSubjectHomework = scopedValue(homework, homeworkScopeTag, subjectScopeKey, null);
    const subjectRead = useRef(0);
    const subjectBusy = useRef(false);
    useEffect(() => { subjectBusy.current = false; setMarkBusy(false); setHomework(null); setHomeworkScopeTag(""); }, [subjectScopeKey]);
    async function refreshSubject(force = false) {
        const ticket = subjectRequests.capture();
        const serial = ++subjectRead.current;
        try {
            const rows = await api.groupHomework(communityId);
            if (!subjectRequests.owns(ticket) || serial !== subjectRead.current || subjectBusy.current && !force) return;
            setHomeworkScopeTag(subjectScopeKey);
            setHomework(subjectHomework(rows, topic.subject));
            setHomeworkError("");
        } catch (error) {
            if (!subjectRequests.owns(ticket) || serial !== subjectRead.current) return;
            if (error instanceof Error && ["401", "403", "404"].includes(error.message)) { setHomeworkScopeTag(subjectScopeKey); setHomework([]); }
            setHomeworkError(problem(error));
        }
    }
    useEffect(() => {
        let stopped = false;
        void refreshSubject();
        const timer = window.setInterval(() => { if (!stopped && !subjectBusy.current) void refreshSubject(); }, 4000);
        return () => { stopped = true; ++subjectRead.current; window.clearInterval(timer); };
    }, [subjectScopeKey]);
    async function markSubjectCopy(item: GroupHomeworkCopy) {
        if (subjectBusy.current || item.canComplete === false) return;
        const action = subjectRequests.begin();
        ++subjectRead.current;
        subjectBusy.current = true;
        setMarkBusy(true);
        setHomeworkError("");
        try {
            const saved = await api.completeHomework(communityId, item.homeworkId, !item.completed, item.completionRevision);
            if (subjectRequests.active(action)) setHomework(rows => rows?.map(row => row.homeworkId === item.homeworkId ? { ...row, completed: saved.completed, completionRevision: saved.revision } : row) ?? []);
        } catch (error) {
            if (!subjectRequests.active(action)) return;
            setHomeworkError(error instanceof Error && error.message === "409" ? "Отметка изменилась. Загружаем актуальное состояние." : problem(error));
            if (error instanceof Error && error.message === "409") { subjectBusy.current = false; await refreshSubject(true); }
            if (subjectRequests.active(action) && !(error instanceof Error && error.message === "409")) onError(problem(error));
        } finally { if (subjectRequests.active(action)) { subjectBusy.current = false; setMarkBusy(false); } }
    }
    const payload = timetable.payload;
    const source = payload ? visibleLessons(payload.lessons, app.subgroups[payload.group.id] ?? {}) : [];
    const lessons = payload ? lessonsOn(source, selected, payload.period.start, payload.period.weekCount, app.invert).filter(lesson => !!topic.subject && sameSubject(lesson.subjectRaw, topic.subject)) : [];
    const context = buildGroupChatContext({ communityGroupName: payload?.group.name ?? null, selectedGroupName: payload?.group.name ?? null, timetableAvailable: !!payload, lessons: source.filter(lesson => !!topic.subject && sameSubject(lesson.subjectRaw, topic.subject)), subgroupChoices: {}, period: payload?.period ?? null, invert: app.invert, topics: [], now: isoDay(new Date()) === selectedIso ? new Date() : selected });
    return <section className="card stack subject-context" aria-label="Данные предмета своей группы"><h2>{topic.subject}</h2><p className="muted">{payload?.group.name || groupName} · {absoluteDate(selected)}</p><label className="field">День предмета<input type="date" value={selectedIso} onChange={event => {
            if (localDay(event.target.value))
                setSelectedIso(event.target.value);
        }}/></label>
      <div className="row"><button className="btn" type="button" aria-pressed={panel === "overview"} onClick={() => setPanel("overview")}>Обзор предмета</button><button className="btn" type="button" aria-pressed={panel === "homework"} onClick={() => setPanel("homework")}>Домашка предмета</button><button className="btn quiet" type="button" aria-pressed={panel === "schedule"} onClick={() => setPanel("schedule")}>Расписание группы</button></div>
      {panel === "overview" && <>{!payload ? <div><p role="status">{timetable.error || "Загружаем расписание группы…"}</p><button className="btn quiet" type="button" disabled={timetable.loading} onClick={timetable.reload}>Повторить</button></div> : <>{timetable.error && <p className="banner">{timetable.error}</p>}<p className="muted">Копия группы: {new Date(payload.meta.fetchedAt).toLocaleString("ru-RU")}</p>{lessons.length === 0 && <p className="muted">На выбранную дату пар по этому предмету нет</p>}{lessons.map((lesson, i) => <p key={i}>{lesson.timeStart}–{lesson.timeEnd} · {lesson.typeRaw} · {lesson.roomRaw || "Аудитория не указана"}</p>)}{context.nextLesson && <div className="card"><h2>Ближайшее занятие по выбранной дате</h2><p>{absoluteDate(context.nextLesson.date)} · {context.nextLesson.time} · {context.nextLesson.room}</p></div>}</>}</>}
      {panel === "schedule" && <ScheduleChannel groupName={groupName} topic={topic} initialDate={selected}/>}
      {panel === "homework" && <div className="stack">{homeworkError && <p role="alert">{homeworkError}</p>}{visibleSubjectHomework === null ? <p role="status">Загружаем домашку группы…</p> : visibleSubjectHomework.length === 0 ? <p className="muted">Заданий группы по этому предмету нет</p> : visibleSubjectHomework.map(item => <article className="card" key={item.homeworkId}><h2>{item.title}</h2><p>{item.body}</p><p className="muted">{groupName} · {audienceLabel(item.audience)} · {item.deadlineAt ? new Date(item.deadlineAt).toLocaleString("ru-RU") : "Без срока"}</p>{item.canComplete !== false && <button className="btn quiet" type="button" disabled={markBusy} onClick={() => void markSubjectCopy(item)}>{item.completed ? "Готово у меня · снять отметку" : "Отметить готово у меня"}</button>}</article>)}</div>}
    </section>;
}
