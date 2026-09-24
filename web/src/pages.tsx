import { ChangeEvent, FormEvent, ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useSwipe } from "./swipe";
import * as api from "./api";
import { followGroupCommunity, openGroupFace } from "./groupChoice";
import { completeGroupCopy, saveEditorHomework } from "./groupHomework";
import { addDays, dayTitle, friendRoomMark, isoDay, lessonsOn, longDate, sameSubject, weekday } from "./parity";
import { composeSummary } from "./summary";
import { lessonsOfGroupTeacher, teacherCode, teacherRows, teacherWeek, type TeacherRow } from "./teachers";
import { subgroupIndex, subgroupMark, visibleLessons } from "./subgroups";
import { HOMEWORK_FILE_LIMIT, checkHomeworkFile, compressHomeworkPhoto, deleteHomeworkBlob, putHomeworkBlob, readHomeworkBlob } from "./homework-files";
import { supportAppend, supportDraft, supportFiles } from "./support";
import { holdActions, runHold } from "./hold";
import { groupBubbleText, GroupMediaError } from "./group-media";
import { legalDocument, type LegalId } from "./legal";
import { useApp } from "./store";
import { homeworkCard, lessonFrom, placeCard, scheduleCard } from "./cards";
import { BallotBoardView } from "./ballots";
import { GroupTopics } from "./topics";
import { GroupAdmin, titlesOf } from "./group-admin";
import { PeoplePanel } from "./people";
import { ShareMenu } from "./share";
import { Icon } from "./icons";
import { VkMark, YandexMark } from "./brands";
import type { BallotBoard, ChatMessage, Community, Conversation, FriendItem, GroupDesk, GroupHome, GroupHomeworkCopy, GroupTopic, HomeworkFile, Lesson, MapPlan, Teacher, TeacherLesson } from "./types";

function Head({ title, text, children }: { title: string; text?: string; children?: ReactNode }) {
  return (
    <div className="page-head">
      <div><h1>{title}</h1>{text && <p className="sub">{text}</p>}</div>
      <div className="row controls">{children}</div>
    </div>
  );
}

function lessonKind(type: string) {
  const value = type.trim().toLowerCase();
  if (value === "лек" || value === "лекция") return "lecture";
  if (value === "пр" || value === "практика") return "practice";
  if (value === "лаб" || value === "лабораторная" || value === "лабораторная работа") return "lab";
  if (value === "конс" || value === "консультация") return "consult";
  if (value === "зач" || value === "зачёт" || value === "зачет") return "credit";
  if (value === "экз" || value === "экзамен") return "exam";
  if (value === "курс" || value === "курсовая") return "course";
  return "";
}

const typeLabels: Record<string, string> = {
  lecture: "Лекция", practice: "Практика", lab: "Лаба", consult: "Консульт.",
  credit: "Зачёт", exam: "Экзамен", course: "Курсовая"
};

function TypeChip({ type }: { type: string }) {
  const kind = lessonKind(type);
  return <span className={"type" + (kind ? " " + kind : "")}><i />{kind ? typeLabels[kind] : type}</span>;
}

function LessonCard({ lesson, mark, share, subgroup, onPick }: { lesson: Lesson; mark?: string; share?: string | null; subgroup?: ReturnType<typeof subgroupMark>; onPick?: (streamId: string, optionId: string) => void }) {
  return (
    <article className="lesson">
      <div className="lesson-top">
        <span className="time">{lesson.timeStart} – {lesson.timeEnd}</span>
        {lesson.typeRaw && <TypeChip type={lesson.typeRaw} />}
        <span className="chip">{lesson.roomRaw || lesson.classroomRaw || "—"}</span>
      </div>
      <div>
        <strong className="subject">{lesson.subjectRaw}</strong>
        <div className="muted">{lesson.teacherRaw}</div>
        {subgroup?.showChooser && (
          <div className="row" style={{ marginTop: 8 }}>
            <span className="muted">{subgroup.chosenId ? "Ваша подгруппа" : "Выберите подгруппу"}</span>
            {subgroup.options.map(option => (
              <button key={option.id} className={"chip" + (option.id === subgroup.chosenId ? " on" : "")} type="button" onClick={() => onPick?.(subgroup.streamId, option.id)}>{option.label}</button>
            ))}
          </div>
        )}
        <div className="row" style={{ marginTop: 6 }}>
          {mark && <span className="chip">{mark}</span>}
          <ShareMenu card={share ?? null} />
        </div>
      </div>
    </article>
  );
}

export function SchedulePage() {
  const app = useApp();
  const period = app.catalog?.period;
  const choices = app.subgroups[app.groupId] || {};
  const index = useMemo(() => subgroupIndex(app.lessons), [app.lessons]);
  const shown = useMemo(() => visibleLessons(app.lessons, choices), [app.lessons, app.subgroups, app.groupId]);
  const lessons = period ? lessonsOn(shown, app.date, period.start, period.weekCount, app.invert) : [];
  const groupName = app.catalog?.groups.find(group => group.id === app.groupId)?.name || "";
  const dayCard = scheduleCard(groupName, app.date, period ? longDate(app.date, period.start, period.weekCount, app.invert) : dayTitle(app.date), lessons);
  const today = new Date();
  const strip = Array.from({ length: 7 }, (_, index) => addDays(addDays(app.date, -((weekday(app.date) + 6) % 7)), index));
  const swipe = useSwipe(() => app.setDate(addDays(app.date, 1)), () => app.setDate(addDays(app.date, -1)));
  return (
    <section className="page">
      <Head title={dayTitle(app.date, today)} text={period ? longDate(app.date, period.start, period.weekCount, app.invert) : "Загружаем расписание"}>
        <button className="icon-btn" type="button" aria-label="Предыдущий день" onClick={() => app.setDate(addDays(app.date, -1))}><Icon name="left" /></button>
        <button className="btn" type="button" onClick={() => app.setDate(new Date())}>Сегодня</button>
        <button className="icon-btn" type="button" aria-label="Следующий день" onClick={() => app.setDate(addDays(app.date, 1))}><Icon name="right" /></button>
        <button className="btn" type="button" onClick={app.refresh} disabled={app.loading}><Icon name="refresh" size={16} />Обновить</button>
        <ShareMenu card={dayCard} label="День в чат" />
      </Head>
      <div className="dates">
        {strip.map(date => (
          <button key={isoDay(date)} className={"date" + (isoDay(date) === isoDay(app.date) ? " active" : "")} type="button" onClick={() => app.setDate(date)}>
            <span>{["вс", "пн", "вт", "ср", "чт", "пт", "сб"][date.getDay()]}</span>
            <strong>{date.getDate()}</strong>
          </button>
        ))}
      </div>
      <p className="swipe-hint">Смахните влево или вправо, чтобы сменить день</p>
      <div className="stack swipe" {...swipe}>
        {lessons.length === 0 && (
          <div className="card empty">
            <p>{app.groupId ? "В этот день пар нет" : "Группа ещё не выбрана. Расписание, карты и домашка останутся на этом устройстве."}</p>
            {!app.groupId && <Link className="btn primary" to="/settings">Выбрать группу</Link>}
          </div>
        )}
        {lessons.map(lesson => {
          const friends = app.friends.filter(item => item.enabled).map(item => {
            const cached = api.readCache().lessons[item.groupName];
            return { enabled: true, lessons: cached && period ? lessonsOn(cached.lessons, app.date, period.start, period.weekCount, app.invert) : [] };
          });
          return <LessonCard key={lesson.index + lesson.timeStart + lesson.subjectRaw + (lesson.teacherRaw || "")} lesson={lesson} mark={friendRoomMark(lesson, friends)} share={lessonFrom(groupName, app.date, lesson)} subgroup={subgroupMark(lesson, lessons, index, choices)} onPick={app.pickSubgroup} />;
        })}
      </div>
    </section>
  );
}

export function WeekPage() {
  const app = useApp();
  const navigate = useNavigate();
  const period = app.catalog?.period;
  const shown = useMemo(() => visibleLessons(app.lessons, app.subgroups[app.groupId] || {}), [app.lessons, app.subgroups, app.groupId]);
  const monday = addDays(app.date, -((weekday(app.date) + 6) % 7));
  const days = Array.from({ length: 6 }, (_, index) => addDays(monday, index));
  const swipe = useSwipe(() => app.setDate(addDays(app.date, 7)), () => app.setDate(addDays(app.date, -7)));
  return (
    <section className="page">
      <Head title="Неделя" text={period?.title}>
        <button className="icon-btn" type="button" aria-label="Предыдущая неделя" onClick={() => app.setDate(addDays(app.date, -7))}><Icon name="left" /></button>
        <button className="btn" type="button" onClick={() => app.setDate(new Date())}>Сегодня</button>
        <button className="icon-btn" type="button" aria-label="Следующая неделя" onClick={() => app.setDate(addDays(app.date, 7))}><Icon name="right" /></button>
      </Head>
      <p className="swipe-hint">Смахните, чтобы сменить неделю</p>
      <div className="week swipe" {...swipe}>
        {days.map(date => (
          <article className="card" key={isoDay(date)}>
            <h2>{dayTitle(date)} <span className="muted">{date.getDate()}</span></h2>
            <button className="btn" type="button" onClick={() => { app.setDate(date); navigate("/schedule"); }}>Открыть день</button>
            <div className="stack">
              {period && lessonsOn(shown, date, period.start, period.weekCount, app.invert).map(lesson => (
                <div key={lesson.timeStart + lesson.subjectRaw + (lesson.teacherRaw || "")}><b>{lesson.timeStart}</b> {lesson.subjectRaw}<div className="muted">{lesson.roomRaw}</div></div>
              ))}
              {period && lessonsOn(shown, date, period.start, period.weekCount, app.invert).length === 0 && <span className="muted">Нет пар</span>}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

const summarySegments = ["Нечётная", "Чётная", "Обе"];

export function SummaryPage() {
  const app = useApp();
  const [segment, setSegment] = useState(2);
  const summary = useMemo(
    () => composeSummary(app.lessons, app.subgroups[app.groupId] || {}, segment, app.invert),
    [app.lessons, app.subgroups, app.groupId, segment, app.invert],
  );
  return (
    <section className="page">
      <Head title="Сводка" text={app.groupId ? "Сколько пар в выбранной группе" : "Группа не выбрана"} />
      {!app.groupId ? (
        <div className="card empty">
          <p>Выберите группу, чтобы открыть расписание</p>
          <Link className="btn primary" to="/settings">Выбрать группу</Link>
        </div>
      ) : (
        <div className="stack">
          <div className="seg" role="tablist" aria-label="Неделя сводки">
            {summarySegments.map((label, index) => (
              <button key={label} className={segment === index ? "active" : ""} type="button" aria-pressed={segment === index} onClick={() => setSegment(index)}>{label}</button>
            ))}
          </div>
          <article className="card">
            <div className="muted">Пар в неделю</div>
            <b className="summary-total">{summary.total}</b>
          </article>
          <CountCard title="По дням" rows={summary.byDay} />
          <CountCard title="По типам" rows={summary.byType} />
          <CountCard title="По предметам" rows={summary.bySubject} />
          <CountCard title="По преподавателям" rows={summary.byTeacher} />
          <CountCard title="По аудиториям" rows={summary.byRoom} empty="Аудитории не указаны" />
        </div>
      )}
    </section>
  );
}

function CountCard({ title, rows, empty }: { title: string; rows: { name: string; count: number }[]; empty?: string }) {
  return (
    <article className="card stack">
      <h2>{title}</h2>
      {rows.length === 0 && empty && <p className="muted">{empty}</p>}
      {rows.map(row => (
        <div className="count" key={title + row.name}>
          <span>{row.name}</span>
          <b>{row.count}</b>
        </div>
      ))}
    </article>
  );
}

const teacherFilters = ["Обе", "Нечётная", "Чётная"];

export function TeachersPage() {
  const app = useApp();
  const [query, setQuery] = useState("");
  const [onlyMine, setOnlyMine] = useState(true);
  const [catalog, setCatalog] = useState<Teacher[]>([]);
  const [selected, setSelected] = useState<TeacherRow | null>(null);
  const [lessons, setLessons] = useState<TeacherLesson[]>([]);
  const [filter, setFilter] = useState(0);
  const [error, setError] = useState("");
  const groupName = app.catalog?.groups.find(group => group.id === app.groupId)?.name || "";
  useEffect(() => { api.loadTeachers().then(data => setCatalog(data.lecturers || [])).catch(() => setError("Список преподавателей не открылся. Показаны преподаватели выбранной группы.")); }, []);
  const attended = useMemo(
    () => visibleLessons(app.lessons, app.subgroups[app.groupId] || {}),
    [app.lessons, app.subgroups, app.groupId],
  );
  const found = useMemo(() => teacherRows(catalog, attended, query, onlyMine), [catalog, attended, query, onlyMine]);
  const week = useMemo(
    () => teacherWeek(lessons, teacherCode(filter, app.invert), app.groupId, groupName, app.invert),
    [lessons, filter, app.invert, app.groupId, groupName],
  );
  async function open(row: TeacherRow) {
    setSelected(row);
    const own = lessonsOfGroupTeacher(attended, row.name, app.groupId, groupName);
    setLessons(own);
    if (row.id.startsWith("group:")) return;
    try { setLessons((await api.loadTeacher(row.id)).lessons || own); }
    catch { setError("Полное расписание преподавателя не открылось. Показаны пары вашей группы."); }
  }
  return (
    <section className="page">
      <Head title="Преподаватели" text={selected ? selected.name : error || "Поиск по фамилии или предмету"} />
      {selected ? (
        <div className="stack">
          <button className="btn teacher-back" type="button" onClick={() => setSelected(null)}>Назад</button>
          <h2>{selected.name}</h2>
          <div className="seg" role="tablist" aria-label="Неделя преподавателя">
            {teacherFilters.map((label, index) => (
              <button key={label} className={filter === index ? "active" : ""} type="button" aria-pressed={filter === index} onClick={() => setFilter(index)}>{label}</button>
            ))}
          </div>
          {week.length === 0 && <div className="card empty"><p>На этой неделе пар нет</p></div>}
          {week.map(day => (
            <article className="card stack" key={day.day}>
              <h2>{day.title}</h2>
              {day.rows.map((row, index) => (
                <div key={day.day + row.time + row.subject + index}>
                  <div className="time">{row.time}</div>
                  <strong className="subject">{row.subject}</strong>
                  {row.groups && <div className="muted">{row.groups}</div>}
                  <div className="muted">{row.room}</div>
                  <div>{row.parityLabel}</div>
                  {row.mine && <div>Моя группа</div>}
                </div>
              ))}
            </article>
          ))}
        </div>
      ) : (
        <div className="stack">
          <input className="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Фамилия или предмет" aria-label="Поиск преподавателя" />
          <div className="row" style={{ justifyContent: "space-between" }}>
            <span>Только мои</span>
            <button className={"switch" + (onlyMine ? " on" : "")} type="button" aria-pressed={onlyMine} aria-label="Только мои" onClick={() => setOnlyMine(value => !value)}><i /></button>
          </div>
          <p className="muted">Найдено {found.rows.length} из {found.total}</p>
          {found.rows.length === 0 && <div className="card empty"><p>Никого не нашлось</p></div>}
          {found.rows.map(row => (
            <button className="person teacher-person" key={row.id} type="button" onClick={() => void open(row)}>
              {row.mine && <i className="mine-mark" />}
              <span><b>{row.name}</b><div className="muted">{row.detail}</div></span>
            </button>
          ))}
        </div>
      )}
    </section>
  );
}

export function MapsPage() {
  const [plans, setPlans] = useState<MapPlan[]>([]);
  const [plan, setPlan] = useState<MapPlan | null>(null);
  const [error, setError] = useState("");
  useEffect(() => {
    api.loadMaps().then(data => {
      setPlans(data.maps);
      const building = sessionStorage.getItem("zapara.map.building");
      const floor = sessionStorage.getItem("zapara.map.floor");
      setPlan(data.maps.find(item => item.building === building && String(item.floor) === floor) || data.maps.find(item => item.building === building) || data.maps[0] || null);
    }).catch(() => setError("Карты не загрузились"));
  }, []);
  const buildings = [...new Set(plans.map(item => item.building))];
  const floors = plans.filter(item => item.building === plan?.building).sort((a, b) => a.floor - b.floor);
  const floorAt = floors.findIndex(item => item.id === plan?.id);
  const swipe = useSwipe(
    () => { if (floorAt >= 0 && floorAt < floors.length - 1) setPlan(floors[floorAt + 1]); },
    () => { if (floorAt > 0) setPlan(floors[floorAt - 1]); },
  );
  return (
    <section className="page">
      <Head title="Карты" text={error || "Планы Военмеха · ГК и УЛК"}>
        <ShareMenu card={plan ? placeCard(plan.building, String(plan.floor), "", `${plan.building}, ${plan.floor} этаж`) : null} label="Этаж в чат" />
      </Head>
      <div className="map-tools">
        {buildings.map(building => <button key={building} className={"btn" + (plan?.building === building ? " primary" : "")} type="button" onClick={() => setPlan(plans.find(item => item.building === building) || null)}>{building}</button>)}
        {floors.map(item => (
          <button key={item.id} className={"btn" + (item.id === plan?.id ? " primary" : "")} type="button" onClick={() => setPlan(item)}>{item.floor} этаж</button>
        ))}
        <button className="btn" type="button" disabled={floorAt <= 0} onClick={() => floorAt > 0 && setPlan(floors[floorAt - 1])}><Icon name="down" size={16} />Ниже</button>
        <button className="btn" type="button" disabled={floorAt < 0 || floorAt >= floors.length - 1} onClick={() => floorAt >= 0 && floorAt < floors.length - 1 && setPlan(floors[floorAt + 1])}><Icon name="up" size={16} />Выше</button>
      </div>
      <p className="swipe-hint">Смахните по плану, чтобы сменить этаж</p>
      <div className="map-frame swipe" {...swipe}>{plan ? <img src={plan.url} alt={`${plan.building}, ${plan.floor} этаж`} /> : <span className="muted">Нет плана</span>}</div>
    </section>
  );
}

export function FriendsPage() {
  const app = useApp();
  const [name, setName] = useState("");
  const [members, setMembers] = useState("");
  function add(event: FormEvent) {
    event.preventDefault();
    if (!name.trim() || app.friends.length >= 5) return;
    app.saveFriends([...app.friends, { id: crypto.randomUUID(), groupName: name.trim(), members: members.trim(), enabled: true, color: "#e0527a" }]);
    setName(""); setMembers("");
    const known = app.catalog?.groups.find(group => group.name === name.trim() || group.id === name.trim());
    if (known) void api.loadTimetable(known.id).then(payload => { const cache = api.readCache(); cache.lessons[known.id] = payload; cache.lessons[known.name] = payload; api.writeCache(cache); });
  }
  return (
    <section className="page">
      <Head title="Друзья" text="Код аккаунта для переписки и группы в расписании." />
      <PeoplePanel />
      <h2 style={{ marginTop: 28 }}>Группы в расписании</h2>
      <p className="sub">До пяти групп. Их пары отмечаются на вашем дне.</p>
      <form className="card stack" onSubmit={add} style={{ marginTop: 12 }}>
        <label className="field">Группа<input value={name} onChange={event => setName(event.target.value)} placeholder="А863С" /></label>
        <label className="field">Имена<input value={members} onChange={event => setMembers(event.target.value)} placeholder="Необязательно" /></label>
        <button className="btn primary" type="submit" disabled={app.friends.length >= 5}>Добавить</button>
      </form>
      <div className="stack" style={{ marginTop: 12 }}>
        {app.friends.map(friend => <FriendRow key={friend.id} friend={friend} />)}
      </div>
    </section>
  );
}

function FriendRow({ friend }: { friend: FriendItem }) {
  const app = useApp();
  return (
    <article className="card row" style={{ justifyContent: "space-between" }}>
      <div><b>{friend.groupName}</b><div className="muted">{friend.members}</div></div>
      <div className="row">
        <button className={"switch" + (friend.enabled ? " on" : "")} type="button" aria-label="Показывать" onClick={() => app.saveFriends(app.friends.map(item => item.id === friend.id ? { ...item, enabled: !item.enabled } : item))}><i /></button>
        <button className="btn" type="button" onClick={() => app.saveFriends(app.friends.filter(item => item.id !== friend.id))}>Удалить</button>
      </div>
    </article>
  );
}

function HomeworkAttachments({ files }: { files: HomeworkFile[] }) {
  const [urls, setUrls] = useState<Record<string, string>>({});
  useEffect(() => {
    let stop = false;
    const created: string[] = [];
    void Promise.all(files.map(async file => {
      const blob = await readHomeworkBlob(file.id);
      if (!blob || stop) return;
      const url = URL.createObjectURL(blob);
      created.push(url);
      if (!stop) setUrls(current => ({ ...current, [file.id]: url }));
    }));
    return () => { stop = true; created.forEach(url => URL.revokeObjectURL(url)); };
  }, [files]);
  if (files.length === 0) return null;
  return (
    <div className="row">
      {files.map(file => {
        const url = urls[file.id];
        if (!url) return <span className="chip" key={file.id}>{file.name}</span>;
        return file.kind === "photo"
          ? <a key={file.id} href={url} target="_blank" rel="noreferrer"><img className="hw-file" alt={file.name} src={url} /></a>
          : <a key={file.id} className="chip" href={url} download={file.name}>{file.name}</a>;
      })}
    </div>
  );
}

export function HomeworkPage() {
  const app = useApp();
  const [subject, setSubject] = useState("");
  const [text, setText] = useState("");
  const [share, setShare] = useState(false);
  const [pending, setPending] = useState<{ file: File; kind: "photo" | "document" }[]>([]);
  const [communityId, setCommunityId] = useState("");
  const [copies, setCopies] = useState<GroupHomeworkCopy[]>([]);
  const [note, setNote] = useState("");
  const subjects = [...new Set(app.lessons.map(lesson => lesson.subjectRaw))];
  useEffect(() => {
    let stop = false;
    void followGroupCommunity(
      { authenticated: !!app.session?.authenticated, groupId: app.groupId },
      groupId => api.communities(groupId),
      state => {
        if (stop) return;
        setCommunityId(state.communityId);
        if (!state.communityId) setCopies([]);
        if (state.failed) setNote("Общая домашка не открылась");
        else if (state.communityId) void api.groupHomework(state.communityId).then(loaded => { if (!stop) setCopies(loaded); });
      },
    );
    return () => { stop = true; };
  }, [app.session, app.groupId]);
  function addPending(list: FileList | null, kind: "photo" | "document") {
    const file = list?.[0];
    if (!file) return;
    if (pending.length >= HOMEWORK_FILE_LIMIT) {
      setNote("Можно приложить не больше шести файлов");
      return;
    }
    try {
      checkHomeworkFile(kind, file.name, file.size);
      setPending(current => current.length >= HOMEWORK_FILE_LIMIT ? current : [...current, { file, kind }]);
      setNote("");
    } catch (error) {
      const code = error instanceof Error ? error.message : "";
      setNote(code === "big" ? "Файл слишком большой" : "Такой файл приложить нельзя");
    }
  }
  async function add(event: FormEvent) {
    event.preventDefault();
    const title = subject.trim();
    const body = text.trim();
    if (!title || !body) return;
    const files: HomeworkFile[] = [];
    const stored: string[] = [];
    try {
      for (const item of pending.slice(0, HOMEWORK_FILE_LIMIT)) {
        const name = checkHomeworkFile(item.kind, item.file.name, item.file.size);
        const blob = item.kind === "photo" ? await compressHomeworkPhoto(item.file) : item.file;
        const id = crypto.randomUUID();
        await putHomeworkBlob(id, blob);
        if (app.session?.authenticated) {
          const data = new FormData();
          data.append("file", blob, name);
          if (app.groupId) data.append("groupId", app.groupId);
          const uploaded = await fetch("/web-api/files", { method: "POST", credentials: "same-origin", body: data });
          if (uploaded.status === 413) {
            await deleteHomeworkBlob(id);
            throw new Error("quota");
          }
        }
        stored.push(id);
        files.push({ id, kind: item.kind, name: item.kind === "photo" ? name.replace(/\.[^.]+$/, ".jpg") : name, mime: item.kind === "photo" ? "image/jpeg" : item.file.type || "application/octet-stream" });
      }
    } catch (error) {
      await Promise.all(stored.map(id => deleteHomeworkBlob(id).catch(() => undefined)));
      const code = error instanceof Error ? error.message : "";
      setNote(code === "quota" ? "Превышен лимит трафика." : code === "big" ? "Файл слишком большой" : code === "full" ? "Можно приложить не больше шести файлов" : "Такой файл приложить нельзя");
      return;
    }
    const outcome = await saveEditorHomework(
      { subject: title, text: body, share, isNew: true },
      !!app.session?.authenticated,
      communityId,
      (subject, text) => { app.saveHomework({ id: crypto.randomUUID(), subject, text, done: false, created: new Date().toISOString(), files }); },
      async (subject, text) => { await api.shareHomework(communityId, subject, text); },
    );
    setText("");
    setPending([]);
    setNote(outcome.note);
    if (outcome.sent) setCopies(await api.groupHomework(communityId));
  }
  async function toggleCopy(item: GroupHomeworkCopy) {
    if (!communityId) return;
    const actor = app.session?.user?.userId ?? "";
    if (!actor) return;
    const next = completeGroupCopy(
      app.homework.map(row => ({ id: row.id, done: row.done })),
      copies.map(row => ({ homeworkId: row.homeworkId, memberId: actor, completed: row.completed })),
      actor,
      item.homeworkId,
      !item.completed,
    );
    if (next.local.some((row, index) => row.done !== app.homework[index]?.done)) return;
    try {
      const saved = await api.completeHomework(communityId, item.homeworkId, next.copies.find(row => row.homeworkId === item.homeworkId)?.completed ?? !item.completed, item.completionRevision);
      const completed = next.copies.find(row => row.homeworkId === item.homeworkId && row.memberId === actor)?.completed ?? saved.completed;
      setCopies(list => list.map(row => row.homeworkId === item.homeworkId ? { ...row, completed, completionRevision: saved.revision } : row));
    }
    catch { setNote("Отметку у общей домашки сохранить не получилось"); }
  }
  return (
    <section className="page">
      <Head title="Домашка" text={note || "Хранится на этом устройстве. Галочка отправляет ту же домашку всей группе."} />
      <form className="card stack" onSubmit={event => void add(event)}>
        <label className="field">Предмет<input list="subjects" value={subject} onChange={event => setSubject(event.target.value)} /></label>
        <datalist id="subjects">{subjects.map(item => <option key={item} value={item} />)}</datalist>
        <label className="field">Задание<textarea value={text} onChange={event => setText(event.target.value)} /></label>
        <div className="row">
          <label className="btn" style={{ position: "relative" }}>Фото<input className="sr" type="file" accept="image/jpeg,image/png,image/webp,image/gif" onChange={event => { addPending(event.target.files, "photo"); event.target.value = ""; }} /></label>
          <label className="btn" style={{ position: "relative" }}>Документ<input className="sr" type="file" accept=".pdf,.txt,.csv,.rtf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.odt,.ods,.odp,.zip" onChange={event => { addPending(event.target.files, "document"); event.target.value = ""; }} /></label>
        </div>
        {pending.map((item, index) => (
          <span className="chip" style={{ alignSelf: "flex-start" }} key={`${item.file.name}-${item.file.size}-${index}`}>
            {item.file.name}
            <button type="button" style={{ border: 0, background: "transparent", color: "inherit", padding: 0 }} onClick={() => setPending(current => current.filter((_, at) => at !== index))}>Убрать</button>
          </span>
        ))}
        <label className="check">
          <input type="checkbox" checked={share} onChange={event => setShare(event.target.checked)} />
          <span>Дублировать всей группе<span className="muted"> — одна и та же домашка появится у всех участников</span></span>
        </label>
        <button className="btn primary" type="submit">Сохранить</button>
      </form>
      {copies.length > 0 && <h2 style={{ marginTop: 18 }}>Всей группе</h2>}
      <div className="stack" style={{ marginTop: copies.length > 0 ? 12 : 0 }}>
        {copies.map(item => (
          <article className="card" key={item.homeworkId}>
            <div className="row" style={{ justifyContent: "space-between" }}>
              <span className="row"><b>{item.title}</b><span className="chip">Группа</span></span>
              <button className="btn" type="button" onClick={() => void toggleCopy(item)}>{item.completed ? "Снова открыть" : "Сделано"}</button>
            </div>
            <p>{item.body}</p>
          </article>
        ))}
      </div>
      <div className="stack" style={{ marginTop: 12 }}>
        {app.homework.map(item => (
          <article className="card" key={item.id}>
            <div className="row" style={{ justifyContent: "space-between" }}>
              <b>{item.subject}</b>
              <button className="btn" type="button" onClick={() => app.saveHomework({ ...item, done: !item.done })}>{item.done ? "Снова открыть" : "Сделано"}</button>
            </div>
            <p>{item.text}</p>
            <HomeworkAttachments files={item.files || []} />
            <div className="row">
              {app.lessons.some(lesson => sameSubject(lesson.subjectRaw, item.subject)) && <span className="chip">есть в расписании</span>}
              <ShareMenu card={homeworkCard(item)} />
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

export function CommunityPage() {
  const app = useApp();
  const [list, setList] = useState<Community[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    if (!app.session?.authenticated) return;
    api.communities(app.groupId).then(setList).catch(() => setError("Сообщества не открылись"));
  }, [app.session, app.groupId]);
  if (!app.session?.authenticated) return <section className="page"><div className="card empty"><h1>Сообщество</h1><p>Войдите в аккаунт, чтобы видеть сообщества своей группы.</p><Link className="btn primary" to="/settings">Открыть настройки</Link></div></section>;
  return (
    <section className="page">
      <Head title="Сообщество" text={error || "Роли старосты и куратора действуют только внутри «Расписание военмех» и не подтверждены университетом."} />
      <div className="stack">
        {list.map(item => (
          <article className="card" key={item.communityId}>
            <h2>{item.name}</h2>
            <p className="muted">{item.description}</p>
            <div className="row">
              <span className="chip">{item.role || "не в группе"}</span>
              {!item.role && <button className="btn" type="button" onClick={() => void api.joinCommunity(item.communityId).then(() => api.communities(app.groupId).then(setList))}>Подать заявку</button>}
            </div>
          </article>
        ))}
        {list.length === 0 && <div className="empty">Сообществ пока нет</div>}
      </div>
    </section>
  );
}

export function GroupPage() {
  const app = useApp();
  const [home, setHome] = useState<GroupHome | null>(null);
  const [desk, setDesk] = useState<GroupDesk | null>(null);
  const [board, setBoard] = useState<BallotBoard | null>(null);
  const [votesOff, setVotesOff] = useState(false);
  const [chat, setChat] = useState<Conversation | null>(null);
  const [thread, setThread] = useState<GroupTopic | "list">("list");
  const [log, setLog] = useState<ChatMessage[]>([]);
  const [draft, setDraft] = useState("");
  const [replyTo, setReplyTo] = useState<string | null>(null);
  const [editing, setEditing] = useState<ChatMessage | null>(null);
  const [menu, setMenu] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [focusChat, setFocusChat] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const pickKind = useRef<"image" | "video" | "file">("image");
  const holdTimer = useRef(0);
  const heldOpen = useRef(false);
  useEffect(() => {
    let stop = false;
    const drop = () => {
      setHome(null);
      setChat(null);
      setDesk(null);
      setBoard(null);
      setLog([]);
    };
    void openGroupFace(
      { authenticated: !!app.session?.authenticated, groupId: app.groupId },
      groupId => api.communities(groupId),
      face => {
        if (stop) return;
        setError(face.error);
        if (!face.communityId) { drop(); return; }
        void api.groupHome(face.communityId).then(loaded => {
          if (stop) return;
          setHome(loaded);
          setChat(loaded.groupChat);
          void api.groupDesk(face.communityId).then(office => { if (!stop) setDesk(office); }).catch(() => { if (!stop) setDesk(null); });
        }).catch(() => { if (!stop) { drop(); setError("Не удалось загрузить группу"); } });
      },
    );
    return () => { stop = true; };
  }, [app.session, app.groupId]);
  useEffect(() => { setThread("list"); setDraft(""); setReplyTo(null); setEditing(null); setMenu(null); }, [chat?.conversationId]);
  useEffect(() => {
    if (!menu) return;
    const node = document.querySelector(".log .actions");
    if (node instanceof HTMLElement) node.scrollIntoView({ block: "nearest" });
  }, [menu]);
  useEffect(() => {
    if (!chat) return;
    if (chat.kind === "group" && thread === "list") return;
    const topic = chat.kind === "group" && thread !== "list" ? (thread.topicId ?? "general") : undefined;
    let stop = false;
    const pull = () => api.messages(chat.conversationId, topic).then(page => { if (!stop) setLog(page.messages); }).catch(() => { if (!stop) setError("Чат не обновился"); });
    void pull();
    if (chat.kind !== "group") void api.markRead(chat.conversationId).catch(() => undefined);
    const timer = window.setInterval(pull, 4000);
    return () => { stop = true; window.clearInterval(timer); };
  }, [chat, thread]);
  const communityId = home?.communityId ?? "";
  useEffect(() => {
    if (!app.session?.authenticated || !communityId) return;
    let stop = false;
    const pull = () => api.ballots(communityId).then(value => {
      if (stop) return;
      setBoard(value);
      setVotesOff(false);
    }).catch(() => { if (!stop) setVotesOff(true); });
    void pull();
    const timer = window.setInterval(pull, 4000);
    return () => { stop = true; window.clearInterval(timer); };
  }, [app.session?.authenticated, communityId]);
  function choose(kind: "image" | "video" | "file") {
    pickKind.current = kind;
    const input = fileRef.current;
    if (!input) return;
    input.accept = kind === "image" ? "image/*" : kind === "video" ? "video/*" : "*/*";
    input.click();
  }
  async function onPicked(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file || !chat) return;
    if (chat.kind === "group" && (thread === "list" || thread.topicId != null)) return;
    try {
      const message = await api.sendGroupMedia(chat.conversationId, pickKind.current, file.name, file, replyTo ?? undefined);
      setReplyTo(null);
      setLog(current => current.some(item => item.messageId === message.messageId) ? current.map(item => item.messageId === message.messageId ? message : item) : [...current, message]);
    } catch (reason) {
      setError(reason instanceof GroupMediaError && reason.code === "size" ? "Файл слишком большой." : "Сообщение не отправилось");
    }
  }
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!chat || !draft.trim()) return;
    if (chat.kind === "group" && thread === "list") return;
    const body = draft;
    setDraft("");
    try {
      if (editing) {
        const message = await api.editGroupMessage(chat.conversationId, editing.messageId, body);
        setEditing(null);
        setLog(current => current.map(item => item.messageId === message.messageId ? message : item));
        return;
      }
      const message = chat.kind === "group" && thread !== "list"
        ? await api.sendTopicMessage(chat.conversationId, body, thread.topicId, replyTo ?? undefined)
        : await api.sendMessage(chat.conversationId, body, replyTo ?? undefined);
      setReplyTo(null);
      setLog(current => [...current, message]);
    }
    catch { setDraft(body); setError("Сообщение не отправилось"); }
  }
  if (!app.session?.authenticated) return <section className="page"><div className="card empty"><h1>Группа</h1><p>Войдите в аккаунт, чтобы открыть группу, разделы чата и голосования.</p><Link className="btn primary" to="/settings">Открыть настройки</Link></div></section>;
  return (
    <section className="page">
      <Head title="Группа" text={home ? `${home.name}${home.groupName ? " · " + home.groupName : ""}` : error || "Одногруппники и чат"} />
      {home && desk && desk.mine.length > 0 && <GroupAdmin communityId={home.communityId} classmates={home.classmates} desk={desk} onChange={setDesk} onReload={async () => { const [loaded, office] = await Promise.all([api.groupHome(home.communityId), api.groupDesk(home.communityId)]); setHome(loaded); setDesk(office); }} onError={setError} />}
      {home && board && <BallotBoardView communityId={home.communityId} board={board} classmates={home.classmates} roles={desk?.roles ?? []} onChange={setBoard} onError={setError} />}
      {home && !board && votesOff && <p className="muted">Голосования сейчас не открылись. Чат группы на месте.</p>}
      {home && (
        <div className={"grid-2 split" + (focusChat ? " focus" : "")}>
          <div className="people split-list">
            <button className="person" type="button" onClick={() => { setChat(home.groupChat); setThread("list"); setFocusChat(true); }}><span><b>Чат группы</b><div className="muted">Разделы и общий поток</div></span>{home.groupChat.unread > 0 && <span className="chip">{home.groupChat.unread}</span>}</button>
            {home.classmates.map(person => (
              <button className="person" key={person.userId} type="button" disabled={person.self} onClick={() => { if (!home || person.self) return; setFocusChat(true); void api.openDirect(home.communityId, person.userId).then(setChat).catch(() => setError("Личный чат не открылся")); }}>
                <span><b>{person.displayName || person.username}</b><div className="muted">@{person.username}</div></span>
                <span className="row">{[person.role === "headman" ? "Староста" : person.role === "curator" ? "Куратор" : "Участник", ...titlesOf(desk, person.userId)].map(title => <span className="chip" key={title}>{title}</span>)}</span>
              </button>
            ))}
            {home.directs.map(item => <button className="person" key={item.conversationId} type="button" onClick={() => { setChat(item); setFocusChat(true); }}><span><b>{item.title}</b><div className="muted">{item.lastBody}</div></span></button>)}
          </div>
          <section className="card chat split-detail">
            <button className="btn back-only" type="button" onClick={() => setFocusChat(false)}>К списку</button>
            {chat?.kind === "group" && thread === "list" && <GroupTopics communityId={home.communityId} onOpen={setThread} onError={setError} />}
            {(chat?.kind !== "group" || thread !== "list") && <>
            <div className="row">
              {chat?.kind === "group" && <button className="btn" type="button" onClick={() => setThread("list")}>Все разделы</button>}
              <h2>{chat?.kind === "group" && thread !== "list" ? `${thread.icon} ${thread.title}` : (chat?.title || "Чат")}</h2>
            </div>
            <div className="log">
              {log.map(message => {
                const mine = message.senderId === app.session?.user?.userId;
                const kind = message.kind || "text";
                const actions = holdActions(kind, mine, !!message.deleted, menu === message.messageId);
                return (
                  <article key={message.messageId} data-hold={kind} className={"bubble" + (mine ? " mine" : "")}
                    onPointerDown={() => { heldOpen.current = false; if (holdTimer.current) window.clearTimeout(holdTimer.current); holdTimer.current = window.setTimeout(() => { holdTimer.current = 0; heldOpen.current = true; setMenu(message.messageId); }, 450); }}
                    onPointerUp={event => { if (holdTimer.current) window.clearTimeout(holdTimer.current); if (heldOpen.current && !(event.target instanceof Element && event.target.closest(".actions"))) event.preventDefault(); }}
                    onPointerLeave={() => { if (holdTimer.current) window.clearTimeout(holdTimer.current); }}
                    onClickCapture={event => { if (event.target instanceof Element && event.target.closest(".actions")) return; if (heldOpen.current || menu === message.messageId) { event.preventDefault(); event.stopPropagation(); } }}>
                    {message.senderId !== app.session?.user?.userId && <b>{message.senderName}</b>}
                    {message.replyTo && <div className="muted">Ответ</div>}
                    <div>{groupBubbleText(message)}</div>
                    <div className="muted">{message.createdAt.slice(0, 16).replace("T", " ")}</div>
                    {actions.length > 0 && (
                      <div className="actions">
                        {actions.map(action => (
                          <button key={action} type="button" onClick={() => runHold(action, {
                            reply() { setMenu(null); setEditing(null); setReplyTo(message.messageId); },
                            reaction() {
                              setMenu(null);
                              if (!chat) return;
                              void api.reactGroupMessage(chat.conversationId, message.messageId).then(next => setLog(current => current.map(item => item.messageId === next.messageId ? next : item))).catch(() => setError("Реакция не сохранилась"));
                            },
                            edit() { setMenu(null); setReplyTo(null); setEditing(message); setDraft(message.body); },
                            delete() {
                              setMenu(null);
                              if (!chat) return;
                              void api.deleteGroupMessage(chat.conversationId, message.messageId).then(next => setLog(current => current.map(item => item.messageId === next.messageId ? next : item))).catch(() => setError("Сообщение не удалилось"));
                            },
                          })}>{action === "reply" ? "Ответить" : action === "reaction" ? "Реакция" : action === "edit" ? "Изменить" : "Удалить"}</button>
                        ))}
                      </div>
                    )}
                  </article>
                );
              })}
            </div>
            {chat && !editing && (chat.kind !== "group" || (thread !== "list" && thread.topicId == null)) && (
              <div className="row">
                <button className="btn" type="button" onClick={() => choose("image")}>Фото</button>
                <button className="btn" type="button" onClick={() => choose("video")}>Видео</button>
                <button className="btn" type="button" onClick={() => choose("file")}>Документ</button>
                <input ref={fileRef} type="file" hidden aria-label="Файл" onChange={event => void onPicked(event)} />
              </div>
            )}
            <form className="compose" onSubmit={event => void submit(event)}>
              {(editing || replyTo) && <p className="muted">{editing ? "Редактирование" : "Ответ"}</p>}
              <input value={draft} onChange={event => setDraft(event.target.value)} placeholder="Сообщение" aria-label="Сообщение" maxLength={2000} />
              <button className="btn primary" type="submit">Отправить</button>
            </form>
            </>}
          </section>
        </div>
      )}
    </section>
  );
}

function LegalLinks() {
  return (
    <nav className="stack legal-links" aria-label="Документы">
      <Link className="btn" to="/legal/agreement"><Icon name="file" size={18} />Пользовательское соглашение</Link>
      <Link className="btn" to="/legal/policy"><Icon name="shield" size={18} />Политика обработки персональных данных</Link>
    </nav>
  );
}

export function LegalPage({ id }: { id: LegalId }) {
  const doc = legalDocument(id);
  return (
    <section className="page legal">
      <h1>{doc.title}</h1>
      {doc.body.split(/\n\n+/).map(paragraph => <p key={paragraph}>{paragraph}</p>)}
      <p><Link to="/settings">К настройкам</Link></p>
    </section>
  );
}

export function SettingsPage() {
  const app = useApp();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [display, setDisplay] = useState("");
  const [mode, setMode] = useState<"login" | "register">("login");
  const [accepted, setAccepted] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const yandex = app.session?.capabilities.yandex === true;
  const vk = app.session?.capabilities.vk === true;
  async function external(provider: "vk" | "yandex") {
    if (busy) return;
    if (mode === "register" && !accepted) {
      setError("Примите пользовательское соглашение и политику обработки персональных данных.");
      return;
    }
    setError("");
    setBusy(true);
    try {
      await api.startExternal(provider);
    } catch {
      setError(provider === "yandex" ? "Не удалось начать вход через Яндекс ID" : "Не удалось начать вход через VK ID");
      setBusy(false);
    }
  }
  async function submit(event: FormEvent) {
    event.preventDefault();
    setError("");
    if (mode === "register" && !accepted) {
      setError("Примите пользовательское соглашение и политику обработки персональных данных.");
      return;
    }
    try {
      if (mode === "register") await api.register(username, password, display || username);
      await api.login(username, password);
      await app.refreshSession();
      setPassword("");
    } catch {
      setError(mode === "login" ? "Не удалось войти" : "Не удалось зарегистрироваться");
    }
  }
  return (
    <section className="page">
      <Head title="Настройки" text="Неофициальное приложение для студентов БГТУ «Военмех»." />
      <div className="stack">
        <article className="card">
          <h2>Группа</h2>
          <label className="field">Моя группа
            <select value={app.groupId} onChange={event => app.setGroupId(event.target.value)} aria-label="Моя группа">
              <option value="">Не выбрана</option>
              {(app.catalog?.groups || []).map(group => <option key={group.id} value={group.id}>{group.name}</option>)}
            </select>
          </label>
          {(app.catalog?.groups || []).length === 0 && <p className="muted">Список групп появится, когда расписание откроется. Пока можно пользоваться сохранённой копией.</p>}
          <div className="row" style={{ marginTop: 12 }}>
            <span>Инвертировать чётность</span>
            <button className={"switch" + (app.invert ? " on" : "")} type="button" aria-label="Инвертировать чётность" onClick={() => app.setInvert(!app.invert)}><i /></button>
          </div>
        </article>
        <article className="card">
          <h2>Оформление</h2>
          <div className="seg">
            <button type="button" className={app.theme === "light" ? "active" : ""} onClick={() => app.setTheme("light")}><Icon name="sun" size={16} />Светлая</button>
            <button type="button" className={app.theme === "dark" ? "active" : ""} onClick={() => app.setTheme("dark")}><Icon name="moon" size={16} />Тёмная</button>
          </div>
        </article>
        <article className="card">
          <h2>Аккаунт</h2>
          {app.session?.authenticated ? (
            <div className="stack">
              <div className="row">
                <span>{app.session.user?.displayName || app.session.user?.username}</span>
                <button className="btn" type="button" onClick={() => void api.logout().then(() => app.refreshSession())}>Выйти</button>
              </div>
              <LegalLinks />
            </div>
          ) : (
            <form className="stack" onSubmit={event => void submit(event)}>
              <p className="muted">Гостевой профиль: расписание доступно без аккаунта и сети, если копия уже сохранена.</p>
              {(yandex || vk) && (
                <div className="providers">
                  <p className="muted">Войти с помощью</p>
                  <div className="id-row">
                    {yandex && <button className="id-btn" type="button" disabled={busy} aria-label="Войти с Яндекс ID" onClick={() => void external("yandex")}><YandexMark />Яндекс ID</button>}
                    {vk && <button className="id-btn" type="button" disabled={busy} aria-label="Войти с VK ID" onClick={() => void external("vk")}><VkMark />VK ID</button>}
                  </div>
                </div>
              )}
              <div className="seg">
                <button type="button" className={mode === "login" ? "active" : ""} onClick={() => { setMode("login"); setAccepted(false); }}>Вход</button>
                <button type="button" className={mode === "register" ? "active" : ""} onClick={() => setMode("register")} disabled={!app.session?.capabilities.registration}>Регистрация</button>
              </div>
              <label className="field">Логин<input value={username} onChange={event => setUsername(event.target.value)} autoComplete="username" /></label>
              <label className="field">Пароль<input type="password" value={password} onChange={event => setPassword(event.target.value)} autoComplete="current-password" /></label>
              {mode === "register" && <label className="field">Имя<input value={display} onChange={event => setDisplay(event.target.value)} /></label>}
              {mode === "register" && (
                <label className="check">
                  <input type="checkbox" checked={accepted} onChange={event => setAccepted(event.target.checked)} />
                  <span className="check-marks" aria-hidden="true"><Icon name="file" size={16} /><Icon name="shield" size={16} /></span>
                  <span>Я принимаю <Link to="/legal/agreement">пользовательское соглашение</Link> и <Link to="/legal/policy">политику обработки персональных данных</Link>.</span>
                </label>
              )}
              {error && <div className="banner">{error}</div>}
              <LegalLinks />
              <button className="btn primary" type="submit" disabled={mode === "register" && !accepted}>{mode === "login" ? "Войти" : "Создать аккаунт"}</button>
            </form>
          )}
        </article>
        <SupportCard />
      </div>
    </section>
  );
}

function FollowUp({ onSend }: { onSend: (text: string, photos: File[], logs: File[]) => void }) {
  const [text, setText] = useState("");
  const [photos, setPhotos] = useState<File[]>([]);
  const [logs, setLogs] = useState<File[]>([]);
  const [note, setNote] = useState("");
  return (
    <form className="stack" onSubmit={event => {
      event.preventDefault();
      const value = text.trim();
      if (!value) return;
      const files = supportFiles(photos, logs);
      if (files.error) { setNote(files.error); return; }
      setText("");
      setPhotos([]);
      setLogs([]);
      setNote("");
      onSend(value, photos, logs);
    }}>
      <label className="field">Уточнение<textarea value={text} onChange={event => setText(event.target.value)} maxLength={4000} rows={3} /></label>
      <Attach photos={photos} logs={logs} onPhotos={setPhotos} onLogs={setLogs} onNote={setNote} />
      {note && <p className="banner">{note}</p>}
      <button className="btn" type="submit">Ответить</button>
    </form>
  );
}

function Attach({ photos, logs, onPhotos, onLogs, onNote }: { photos: File[]; logs: File[]; onPhotos: (files: File[]) => void; onLogs: (files: File[]) => void; onNote: (note: string) => void }) {
  function add(kind: "photo" | "log", list: FileList | null, input: HTMLInputElement) {
    const next = [...(kind === "photo" ? photos : logs), ...Array.from(list ?? [])].slice(0, 3);
    const check = supportFiles(kind === "photo" ? next : photos, kind === "log" ? next : logs);
    input.value = "";
    if (check.error) { onNote(check.error); return; }
    onNote("");
    if (kind === "photo") onPhotos(next);
    else onLogs(next);
  }
  return (
    <div className="stack">
      <div className="attach">
        <label className="btn file">Фото<input type="file" accept="image/jpeg,image/png,image/webp,.jpg,.jpeg,.png,.webp" multiple aria-label="Фото" onChange={event => add("photo", event.target.files, event.target)} /></label>
        <label className="btn file">Лог<input type="file" accept=".txt,.log,text/plain" multiple aria-label="Лог" onChange={event => add("log", event.target.files, event.target)} /></label>
      </div>
      <p className="muted">До трёх фотографий JPEG, PNG или WebP, до 4 МиБ. До трёх логов .txt или .log, до 512 КиБ.</p>
      <div className="attach">
        {photos.map((file, index) => <button className="btn" type="button" key={"p" + file.name + index} onClick={() => onPhotos(photos.filter((_, item) => item !== index))}>{file.name} · убрать</button>)}
        {logs.map((file, index) => <button className="btn" type="button" key={"l" + file.name + index} onClick={() => onLogs(logs.filter((_, item) => item !== index))}>{file.name} · убрать</button>)}
      </div>
    </div>
  );
}

function SupportLineView({ line }: { line: api.SupportLine }) {
  return (
    <div>
      <p><b>{line.author === "operator" ? "Поддержка" : "Вы"}.</b> {line.body}</p>
      {(line.attachments ?? []).map(file => file.kind === "photo"
        ? <img key={file.id} className="support-photo" src={"/web-api/support/attachments/" + file.id} alt={file.name} />
        : <p key={file.id} className="support-log"><a href={"/web-api/support/attachments/" + file.id}>{file.name}</a></p>)}
    </div>
  );
}

function SupportCard() {
  const app = useApp();
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [photos, setPhotos] = useState<File[]>([]);
  const [logs, setLogs] = useState<File[]>([]);
  const [threads, setThreads] = useState<api.SupportThread[]>([]);
  const [note, setNote] = useState("");
  useEffect(() => {
    if (!app.session?.authenticated) { setThreads([]); return; }
    void api.supportList().then(setThreads).catch(() => setNote("Переписка не открылась."));
  }, [app.session?.authenticated]);
  async function send(event: FormEvent) {
    event.preventDefault();
    setNote("");
    const draft = supportDraft(!!app.session?.authenticated, subject, body);
    if (draft.error || !draft.subject || !draft.body) { setNote(draft.error || "Опишите тему и что случилось."); return; }
    const files = supportFiles(photos, logs);
    if (files.error) { setNote(files.error); return; }
    try {
      const opened = await api.supportOpen(draft.subject, draft.body, photos, logs);
      setThreads(list => [opened, ...list.filter(item => item.id !== opened.id)]);
      setSubject("");
      setBody("");
      setPhotos([]);
      setLogs([]);
    } catch (error) {
      setNote(error instanceof Error && error.message ? error.message : "Не удалось отправить сообщение.");
    }
  }
  async function follow(id: string, text: string, nextPhotos: File[], nextLogs: File[]) {
    const draft = supportDraft(!!app.session?.authenticated, "уточнение", text);
    if (draft.error || !draft.body) { setNote(draft.error || "Опишите, что случилось."); return; }
    const next = supportAppend([], "user", draft.body);
    if (next.length !== 1) return;
    try {
      const updated = await api.supportReply(id, next[0].body, nextPhotos, nextLogs);
      setThreads(list => list.map(item => item.id === updated.id ? updated : item));
    } catch (error) {
      setNote(error instanceof Error && error.message ? error.message : "Не удалось отправить сообщение.");
    }
  }
  return (
    <article className="card stack">
      <h2>Сообщить о баге</h2>
      <form className="stack" onSubmit={event => void send(event)}>
        {!app.session?.authenticated && <p>Войдите в аккаунт, чтобы отправить сообщение об ошибке и увидеть ответ. Расписание и карты остаются доступны без входа.</p>}
        <label className="field">Тема<input value={subject} onChange={event => setSubject(event.target.value)} maxLength={120} required={!!app.session?.authenticated} /></label>
        <label className="field">Что случилось<textarea value={body} onChange={event => setBody(event.target.value)} maxLength={4000} required={!!app.session?.authenticated} rows={4} /></label>
        <Attach photos={photos} logs={logs} onPhotos={setPhotos} onLogs={setLogs} onNote={setNote} />
        <button className="btn primary" type="submit">Отправить</button>
      </form>
      {note && <p className="banner">{note}</p>}
      {threads.map(thread => (
        <div key={thread.id} className="stack">
          <h3>{thread.subject}</h3>
          {thread.messages.map((line, index) => <SupportLineView key={thread.id + index} line={line} />)}
          <FollowUp onSend={(text, nextPhotos, nextLogs) => void follow(thread.id, text, nextPhotos, nextLogs)} />
        </div>
      ))}
    </article>
  );
}
