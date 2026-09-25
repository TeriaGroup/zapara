import { ChangeEvent, FormEvent, Fragment, ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useSwipe } from "./swipe";
import * as api from "./api";
import { followGroupCommunity, openGroupFace } from "./groupChoice";
import { clearSentGroupDraft, createGroupPoller, groupMediaSelectionIsCurrent, mergeGroupMessages, newestUnseenIncoming } from "./groupChat";
import { completeGroupCopy, saveEditorHomework } from "./groupHomework";
import { addDays, dayTitle, isoDay, lessonsOn, longDate, sameSubject, weekday } from "./parity";
import { forecastIntersections, intersectionPlace, markDescription, marksForLesson, resolveFriendSchedules, type FriendMark } from "./intersections";
import { composeSummary } from "./summary";
import { lessonsOfGroupTeacher, teacherCode, teacherRows, teacherWeek, type TeacherRow } from "./teachers";
import { subgroupIndex, subgroupMark, visibleLessons } from "./subgroups";
import { HOMEWORK_FILE_LIMIT, checkHomeworkFile, compressHomeworkPhoto, deleteHomeworkBlob, putHomeworkBlob, readHomeworkBlob } from "./homework-files";
import { supportAppend, supportDraft, supportFiles } from "./support";
import { holdActions, runHold } from "./hold";
import { groupBubbleText, groupMediaDownload, GroupMediaError, type GroupMediaDownload } from "./group-media";
import { canComposeChannel, canCreateBallot, isChatChannel, nextUnreadTopic, orderedTopics, topicPreview } from "./channels";
import { isNearLatest, matchesBrowseQuery, unreadBadgeDescription, unreadBadgeText } from "./groupBrowse";
import { canCopyMessageText, filterMessages, messageDayKey, sameMessageCluster, type MessageBrowseFilter } from "./messageBrowse";
import { buildGroupChatContext } from "./groupChatContext";
import { GroupComposer } from "./group-composer";
import { GroupInlineMedia } from "./group-inline-media";
import { legalDocument, type LegalId } from "./legal";
import { useApp } from "./store";
import { homeworkCard, lessonFrom, placeCard, scheduleCard } from "./cards";
import { BallotBoardView } from "./ballots";
import { GroupTopics } from "./topics";
import { GroupAdmin, titlesOf } from "./group-admin";
import { ShareMenu } from "./share";
import { Icon } from "./icons";
import { VkMark, YandexMark } from "./brands";
import type { BallotBoard, ChatMessage, Community, Conversation, FriendItem, GroupDesk, GroupHome, GroupHomeworkCopy, GroupTopic, GroupTopicPage, HomeworkFile, Lesson, MapPlan, Teacher, TeacherLesson } from "./types";

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

function LessonCard({ lesson, marks = [], share, subgroup, onPick }: { lesson: Lesson; marks?: FriendMark[]; share?: string | null; subgroup?: ReturnType<typeof subgroupMark>; onPick?: (streamId: string, optionId: string) => void }) {
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
          {marks.map(mark => <span key={mark.groupName} className={"chip friend-mark" + (mark.present ? "" : " absent")}
            style={{ borderLeftColor: mark.color || "var(--line-strong)" }}
            aria-label={`${mark.groupName}${mark.members ? " (" + mark.members + ")" : ""}: ${markDescription(mark)}`}>
            {mark.groupName}{mark.members ? ` · ${mark.members}` : ""} · {markDescription(mark)}
          </span>)}
          <ShareMenu card={share ?? null} />
        </div>
      </div>
    </article>
  );
}

export function SchedulePage() {
  const app = useApp();
  const cache = api.readCache();
  const ownTimetable = cache.lessons[app.groupId];
  const period = ownTimetable?.period || app.catalog?.period;
  const choices = app.subgroups[app.groupId] || {};
  const index = useMemo(() => subgroupIndex(app.lessons), [app.lessons]);
  const shown = useMemo(() => visibleLessons(app.lessons, choices), [app.lessons, app.subgroups, app.groupId]);
  const lessons = period ? lessonsOn(shown, app.date, period.start, period.weekCount, app.invert) : [];
  const friendSchedules = resolveFriendSchedules(app.friends, app.catalog?.groups || [], cache.lessons, ownTimetable);
  const groupName = app.catalog?.groups.find(group => group.id === app.groupId)?.name || "";
  const dayCard = scheduleCard(groupName, app.date, period ? longDate(app.date, period.start, period.weekCount, app.invert) : dayTitle(app.date), lessons);
  const today = new Date();
  const strip = Array.from({ length: 7 }, (_, index) => addDays(addDays(app.date, -((weekday(app.date) + 6) % 7)), index));
  const swipe = useSwipe(() => app.setDate(addDays(app.date, 1)), () => app.setDate(addDays(app.date, -1)));
  const groupsUnavailable = !app.groupId && !app.catalog && !app.loading;
  const timetableUnavailable = !!app.groupId && !app.timetableAvailable && !app.timetableLoading;
  const emptyMessage = !app.groupId
    ? app.loading ? "Загружаем список групп" : groupsUnavailable ? "Список групп не загрузился. Проверьте сеть и попробуйте ещё раз." : app.catalog?.groups.length === 0 ? "В списке пока нет групп" : "Группа ещё не выбрана. Расписание, карты и домашка останутся на этом устройстве."
    : !app.timetableAvailable
      ? app.timetableLoading ? "Загружаем расписание группы" : "Расписание группы не загрузилось. Проверьте сеть и попробуйте ещё раз."
      : "В этот день пар нет";
  return (
    <section className="page">
      <Head title={dayTitle(app.date, today)} text={period ? longDate(app.date, period.start, period.weekCount, app.invert) : app.timetableFailed ? "Расписание недоступно" : "Загружаем расписание"}>
        <button className="icon-btn" type="button" aria-label="Предыдущий день" onClick={() => app.setDate(addDays(app.date, -1))}><Icon name="left" /></button>
        <button className="btn" type="button" onClick={() => app.setDate(new Date())}>Сегодня</button>
        <button className="icon-btn" type="button" aria-label="Следующий день" onClick={() => app.setDate(addDays(app.date, 1))}><Icon name="right" /></button>
        <button className="btn" type="button" onClick={app.refresh} disabled={app.loading}><Icon name="refresh" size={16} />Обновить</button>
        <ShareMenu card={app.timetableAvailable && period ? dayCard : null} label="День в чат" />
      </Head>
      <div className="dates">
        {strip.map(date => (
          <button key={isoDay(date)} className={"date" + (isoDay(date) === isoDay(app.date) ? " active" : "")} type="button" onClick={() => app.setDate(date)}>
            <span>{["вс", "пн", "вт", "ср", "чт", "пт", "сб"][date.getDay()]}</span>
            <strong>{date.getDate()}</strong>
          </button>
        ))}
      </div>
      {app.timetableAvailable && <div className="section-overview">
        <strong>{groupName || "Расписание группы"}</strong>
        <span>Пар в этот день: {lessons.length}</span>
      </div>}
      <p className="swipe-hint">Смахните влево или вправо, чтобы сменить день</p>
      <div className="stack swipe" {...swipe}>
        {(!app.groupId || !app.timetableAvailable || lessons.length === 0) && (
          <div className="card empty">
            <p>{emptyMessage}</p>
            {!app.groupId && !!app.catalog?.groups.length && <Link className="btn primary" to="/settings">Выбрать группу</Link>}
            {(groupsUnavailable || timetableUnavailable) && <button className="btn primary" type="button" onClick={app.refresh}>Повторить</button>}
          </div>
        )}
        {lessons.map(lesson => {
          const marks = period ? marksForLesson(lesson, app.date, {
            mineLessons: shown, friends: friendSchedules, period, invert: app.invert,
            strictness: app.intersectionStrictness, now: today,
          }, app.showAbsentFriends) : [];
          return <LessonCard key={lesson.index + lesson.timeStart + lesson.subjectRaw + (lesson.teacherRaw || "")} lesson={lesson} marks={marks} share={lessonFrom(groupName, app.date, lesson)} subgroup={subgroupMark(lesson, lessons, index, choices)} onPick={app.pickSubgroup} />;
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
  const weekDays = days.map(date => ({ date, lessons: period ? lessonsOn(shown, date, period.start, period.weekCount, app.invert) : [] }));
  const weekTotal = weekDays.reduce((total, day) => total + day.lessons.length, 0);
  const swipe = useSwipe(() => app.setDate(addDays(app.date, 7)), () => app.setDate(addDays(app.date, -7)));
  return (
    <section className="page">
      <Head title="Неделя" text={period?.title}>
        <button className="icon-btn" type="button" aria-label="Предыдущая неделя" onClick={() => app.setDate(addDays(app.date, -7))}><Icon name="left" /></button>
        <button className="btn" type="button" onClick={() => app.setDate(new Date())}>Сегодня</button>
        <button className="icon-btn" type="button" aria-label="Следующая неделя" onClick={() => app.setDate(addDays(app.date, 7))}><Icon name="right" /></button>
      </Head>
      {app.timetableAvailable && period && <div className="section-overview">
        <strong>Пар за неделю: {weekTotal}</strong>
        <span>{monday.toLocaleDateString("ru-RU", { day: "numeric", month: "short" })} — {days[5].toLocaleDateString("ru-RU", { day: "numeric", month: "short" })}</span>
      </div>}
      <p className="swipe-hint">Смахните, чтобы сменить неделю</p>
      <div className="week swipe" {...swipe}>
        {weekDays.map(({ date, lessons: dayLessons }) => (
          <article className="card" key={isoDay(date)}>
            <h2 className="week-day-head">{dayTitle(date)} <span className="muted">{date.getDate()}</span><span className="chip">Пар: {dayLessons.length}</span></h2>
            <button className="btn" type="button" onClick={() => { app.setDate(date); navigate("/schedule"); }}>Открыть день</button>
            <div className="stack">
              {dayLessons.map(lesson => (
                <div key={lesson.timeStart + lesson.subjectRaw + (lesson.teacherRaw || "")}><b>{lesson.timeStart}</b> {lesson.subjectRaw}<div className="muted">{lesson.roomRaw}</div></div>
              ))}
              {period && dayLessons.length === 0 && <span className="muted">Нет пар</span>}
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
          <div className="section-divider" aria-hidden="true" />
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
  const teacherWeekTotal = week.reduce((total, day) => total + day.rows.length, 0);
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
          <div className="section-overview"><strong>Пар в выбранной неделе: {teacherWeekTotal}</strong><span>{teacherFilters[filter]}</span></div>
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
          <div className="teacher-search-bar">
            <input className="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Фамилия или предмет" aria-label="Поиск преподавателя" />
            {!!query.trim() && <button className="btn" type="button" onClick={() => setQuery("")}>Сбросить поиск</button>}
          </div>
          <div className="section-overview">
            <strong>Найдено {found.rows.length} из {found.total}</strong>
            <div className="row"><span>Только мои</span>
              <button className={"switch" + (onlyMine ? " on" : "")} type="button" aria-pressed={onlyMine} aria-label="Только мои" onClick={() => setOnlyMine(value => !value)}><i /></button>
            </div>
          </div>
          {found.rows.length === 0 && <div className="card empty"><p>{!query.trim() && onlyMine ? "У выбранной группы преподаватели пока не найдены" : "Никого не нашлось"}</p>
            {!!query.trim() ? <button className="btn" type="button" onClick={() => setQuery("")}>Сбросить поиск</button>
              : onlyMine && <button className="btn" type="button" onClick={() => setOnlyMine(false)}>Показать всех</button>}</div>}
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
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let stop = false;
    setLoading(true);
    setError("");
    setPlans([]);
    setPlan(null);
    api.loadMaps().then(data => {
      if (stop) return;
      setPlans(data.maps);
      const building = sessionStorage.getItem("zapara.map.building");
      const floor = sessionStorage.getItem("zapara.map.floor");
      setPlan(data.maps.find(item => item.building === building && String(item.floor) === floor) || data.maps.find(item => item.building === building) || data.maps[0] || null);
    }).catch(() => { if (!stop) setError("Карты не загрузились. Проверьте сеть и попробуйте ещё раз."); })
      .finally(() => { if (!stop) setLoading(false); });
    return () => { stop = true; };
  }, [retry]);
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
      {plan && <div className="section-overview">
        <strong>{plan.building} · {plan.floor} этаж</strong>
        <span>Выбранный план</span>
      </div>}
      <div className="map-tools map-selectors" role="group" aria-label="Выбор корпуса и этажа">
        {buildings.map(building => <button key={building} className={"btn" + (plan?.building === building ? " primary" : "")} type="button" onClick={() => setPlan(plans.find(item => item.building === building) || null)}>{building}</button>)}
        {floors.map(item => (
          <button key={item.id} className={"btn" + (item.id === plan?.id ? " primary" : "")} type="button" onClick={() => setPlan(item)}>{item.floor} этаж</button>
        ))}
      </div>
      <div className="map-tools map-navigation" role="group" aria-label="Переход между этажами">
        <button className="btn" type="button" disabled={floorAt <= 0} onClick={() => floorAt > 0 && setPlan(floors[floorAt - 1])}><Icon name="down" size={16} />Ниже</button>
        <button className="btn" type="button" disabled={floorAt < 0 || floorAt >= floors.length - 1} onClick={() => floorAt >= 0 && floorAt < floors.length - 1 && setPlan(floors[floorAt + 1])}><Icon name="up" size={16} />Выше</button>
      </div>
      <p className="swipe-hint">Смахните по плану, чтобы сменить этаж</p>
      <div className="map-frame swipe" {...swipe}>{plan ? <img src={plan.url} alt={`${plan.building}, ${plan.floor} этаж`} /> : <span className="muted">{loading ? "Загружаем карты" : error ? "Карты недоступны" : "Планов пока нет"}</span>}</div>
      {error && <button className="btn primary" type="button" onClick={() => setRetry(value => value + 1)}>Повторить</button>}
    </section>
  );
}

export function FriendsPage() {
  const app = useApp();
  const [editorOpen, setEditorOpen] = useState(false);
  const [name, setName] = useState("");
  const [members, setMembers] = useState("");
  const [error, setError] = useState("");
  const [refreshing, setRefreshing] = useState(false);
  const [, setCacheRevision] = useState(0);
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 60_000);
    return () => window.clearInterval(timer);
  }, []);
  const groups = app.catalog?.groups || [];
  const cache = api.readCache();
  const ownTimetable = cache.lessons[app.groupId];
  const schedules = resolveFriendSchedules(app.friends, groups, cache.lessons, ownTimetable);
  const period = ownTimetable?.period || app.catalog?.period;
  const forecast = period && app.groupId && app.timetableAvailable
    ? forecastIntersections({
      mineLessons: visibleLessons(app.lessons, app.subgroups[app.groupId] || {}),
      friends: schedules, period, invert: app.invert,
      strictness: app.intersectionStrictness, now,
    }) : null;
  const activeCount = app.friends.filter(friend => friend.enabled).length;
  const missingCount = forecast?.missingGroups.length || 0;
  const palette = ["#F2A33C", "#4CC38A", "#5AA9FF", "#C77DFF", "#FF7A9C"];
  function knownGroup(value: string) {
    return groups.find(group => group.id === value.trim() || group.name.toLocaleLowerCase("ru-RU") === value.trim().toLocaleLowerCase("ru-RU"));
  }
  function duplicateGroup(value: string, exceptId = "") {
    const selected = knownGroup(value);
    return app.friends.some(friend => friend.id !== exceptId &&
      (friend.groupName.toLocaleLowerCase("ru-RU") === value.trim().toLocaleLowerCase("ru-RU") ||
        (selected && knownGroup(friend.groupName)?.id === selected.id)));
  }
  async function updateFriendTimetable(groupName: string) {
    const known = knownGroup(groupName);
    if (!known) return false;
    try {
      const payload = await api.loadTimetable(known.id);
      const cache = api.readCache();
      cache.lessons[known.id] = payload;
      cache.lessons[known.name] = payload;
      api.writeCache(cache);
      setCacheRevision(value => value + 1);
      return true;
    } catch { return false; }
  }
  async function refreshFriends() {
    if (refreshing) return;
    setRefreshing(true);
    setError("");
    const names = app.friends.filter(friend => friend.enabled).map(friend => friend.groupName);
    const outcomes = await Promise.all(names.map(updateFriendTimetable));
    if (outcomes.some(ok => !ok)) setError("Не все расписания удалось обновить. Сохранённые копии остаются доступны.");
    setRefreshing(false);
  }
  function add(event: FormEvent) {
    event.preventDefault();
    const group = knownGroup(name);
    if (!name.trim() || app.friends.length >= 5) return;
    if (duplicateGroup(name)) { setError("Эта группа уже добавлена."); return; }
    if (group?.id === app.groupId) { setError("Ваша группа уже есть в расписании."); return; }
    const used = new Set(app.friends.map(friend => friend.color?.toUpperCase()));
    const color = palette.find(value => !used.has(value.toUpperCase())) || palette[0];
    app.saveFriends([...app.friends, { id: crypto.randomUUID(), groupName: group?.name || name.trim(), members: members.trim(), enabled: true, color }]);
    setName(""); setMembers("");
    setError("");
    setEditorOpen(false);
    if (group) void updateFriendTimetable(group.name).then(ok => { if (!ok) setError("Расписание группы не загрузилось. Попробуйте обновить позже."); });
  }
  return (
    <section className="page">
      <Head title="Пересечения" text="Узнайте, когда друзья из других групп учатся рядом с вами.">
        <button className="btn primary" type="button" aria-expanded={editorOpen} aria-controls="friend-editor"
          disabled={app.friends.length >= 5 && !editorOpen} onClick={() => setEditorOpen(value => !value)}>
          {editorOpen ? "Скрыть редактор" : "Добавить группу"}
        </button>
      </Head>
      <div className="section-overview">
        <strong>Групп: {app.friends.length} из 5</strong>
        <span>Активных: {activeCount}</span>
      </div>
      <div className="card stack intersection-settings">
        <div><h2>Насколько близко</h2><p className="sub">Показываем совпадение времени и аудитории по расписанию, а не фактическое местоположение.</p></div>
        <div className="seg intersection-presets" role="group" aria-label="Точность пересечений">
          {([[25, "В вузе"], [50, "В корпусе"], [75, "На этаже"], [100, "В аудитории"]] as const).map(([value, label]) =>
            <button key={value} type="button" className={app.intersectionStrictness === value ? "active" : ""}
              aria-pressed={app.intersectionStrictness === value} onClick={() => app.setIntersectionStrictness(value)}>{label}</button>)}
        </div>
        <label className="check"><input type="checkbox" checked={app.showAbsentFriends} onChange={event => app.setShowAbsentFriends(event.target.checked)} />
          <span>Показывать и группы без совпадения<span className="muted"> — в карточках пар будет виден уровень встречи или её отсутствие</span></span></label>
      </div>
      <div className="intersection-forecast-head row">
        <div><h2>Ближайшие пересечения</h2><p className="sub">До трёх встреч на ближайшие 14 дней</p></div>
        {activeCount > 0 && <button className="btn" type="button" disabled={refreshing} onClick={() => void refreshFriends()}>
          <Icon name="refresh" size={16} />{refreshing ? "Обновляем…" : "Обновить расписания"}</button>}
      </div>
      {error && <div className="banner" role="status">{error}</div>}
      {!app.groupId && <div className="card empty">Выберите свою группу в настройках, чтобы увидеть встречи с друзьями.<Link className="btn" to="/settings">Открыть настройки</Link></div>}
      {!!app.groupId && !app.timetableAvailable && <div className="card empty">{app.timetableLoading ? "Загружаем ваше расписание…" : "Ваше расписание недоступно. Обновите его в разделе «Расписание»."}</div>}
      {!!forecast && activeCount === 0 && <div className="card empty">Включите группу друзей или добавьте новую, чтобы увидеть пересечения.</div>}
      {!!forecast && activeCount > 0 && forecast.encounters.length === 0 &&
        <div className="card empty">{forecast.checkedGroups === 0 ? "Совместимые расписания групп друзей ещё не загружены." : "По выбранной точности встреч в ближайшие две недели не найдено."}</div>}
      {!!forecast && forecast.encounters.length > 0 && <div className="intersection-forecast stack" aria-label="Ближайшие пересечения">
        {forecast.encounters.map((encounter, index) => <article className="card intersection-encounter" key={`${encounter.date.toDateString()}-${encounter.time}-${encounter.friendGroupName}-${index}`}
          style={{ borderLeftColor: encounter.color || "var(--line-strong)" }}>
          <strong>{encounter.date.toLocaleDateString("ru-RU", { weekday: "short", day: "numeric", month: "long" })} · {encounter.time} · {encounter.subject}</strong>
          <span>{encounter.friendGroupName}{encounter.members ? ` · ${encounter.members}` : ""}</span>
          <span className="muted">{intersectionPlace(encounter.score)}{encounter.friendRoom ? ` · ${encounter.friendRoom}` : ""}</span>
        </article>)}
      </div>}
      {!!forecast && missingCount > 0 && <p className="muted" role="status">Нет подходящего загруженного расписания: {forecast.missingGroups.join(", ")}. Для этих групп прогноз пока неполный.</p>}
      <h2 className="section-list-title">Группы в расписании <span className="chip">{app.friends.length}</span></h2>
      <p className="sub">До пяти групп. Их пары отмечаются в вашем расписании.</p>
      <form id="friend-editor" className="card stack friend-compose" hidden={!editorOpen} onSubmit={add} style={{ marginTop: 12 }}>
        <label className="field">Группа<input list="friend-groups" value={name} onChange={event => setName(event.target.value)} placeholder="А863С" /></label>
        <datalist id="friend-groups">{(app.catalog?.groups || []).map(group => <option key={group.id} value={group.name} />)}</datalist>
        <label className="field">Имена<input value={members} onChange={event => setMembers(event.target.value)} placeholder="Необязательно" /></label>
        <button className="btn primary" type="submit" disabled={app.friends.length >= 5}>Добавить</button>
      </form>
      {app.friends.length === 0 && !editorOpen && <div className="card empty">Группы друзей ещё не добавлены.
        <button className="btn" type="button" onClick={() => setEditorOpen(true)}>Добавить первую группу</button>
      </div>}
      <div className="stack" style={{ marginTop: 12 }}>
        {app.friends.map(friend => <FriendRow key={friend.id} friend={friend}
          hasSchedule={schedules.find(item => item.groupName === friend.groupName)?.lessons != null}
          groups={groups} palette={palette} duplicateGroup={duplicateGroup}
          onGroupChanged={groupName => void updateFriendTimetable(groupName)} />)}
      </div>
    </section>
  );
}

function FriendRow({ friend, hasSchedule, groups, palette, duplicateGroup, onGroupChanged }: {
  friend: FriendItem; hasSchedule: boolean;
  groups: { id: string; name: string }[]; palette: string[];
  duplicateGroup: (value: string, exceptId: string) => boolean;
  onGroupChanged: (groupName: string) => void;
}) {
  const app = useApp();
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(friend.groupName);
  const [members, setMembers] = useState(friend.members);
  const [error, setError] = useState("");
  function save(event: FormEvent) {
    event.preventDefault();
    const next = name.trim();
    if (!next) { setError("Укажите группу."); return; }
    if (duplicateGroup(next, friend.id)) { setError("Эта группа уже добавлена."); return; }
    const known = groups.find(group => group.id === next || group.name.toLocaleLowerCase("ru-RU") === next.toLocaleLowerCase("ru-RU"));
    if (known?.id === app.groupId) { setError("Ваша группа уже есть в расписании."); return; }
    const groupName = known?.name || next;
    app.saveFriends(app.friends.map(item => item.id === friend.id ? { ...item, groupName, members: members.trim() } : item));
    if (groupName !== friend.groupName) onGroupChanged(groupName);
    setError(""); setEditing(false);
  }
  return (
    <article className="card friend-row">
      <div className="row friend-row-main">
        <span className="friend-swatch" style={{ backgroundColor: friend.color || palette[0] }} aria-hidden="true" />
        <div className="friend-row-name"><b>{friend.groupName}</b><div className="muted">{friend.members || "Имена не указаны"} · {hasSchedule ? "расписание готово" : "расписание недоступно"}</div></div>
        <button className={"switch" + (friend.enabled ? " on" : "")} type="button" role="switch" aria-checked={friend.enabled}
          aria-label={`Показывать группу ${friend.groupName}`}
          onClick={() => app.saveFriends(app.friends.map(item => item.id === friend.id ? { ...item, enabled: !item.enabled } : item))}><i /></button>
        <button className="btn" type="button" aria-expanded={editing} onClick={() => { setName(friend.groupName); setMembers(friend.members); setError(""); setEditing(value => !value); }}>{editing ? "Закрыть" : "Изменить"}</button>
        <button className="btn" type="button" onClick={() => {
          if (window.confirm(`Удалить группу ${friend.groupName} из пересечений?`))
            app.saveFriends(app.friends.filter(item => item.id !== friend.id));
        }}>Удалить</button>
      </div>
      {editing && <form className="stack friend-row-edit" onSubmit={save}>
        <label className="field">Группа<input list="friend-groups" value={name} onChange={event => setName(event.target.value)} /></label>
        <label className="field">Имена друзей<input value={members} onChange={event => setMembers(event.target.value)} placeholder="Необязательно" /></label>
        <div className="row friend-palette" role="group" aria-label={`Цвет группы ${friend.groupName}`}>
          {palette.map(color => <button key={color} type="button" className={"friend-color-choice" + (friend.color?.toUpperCase() === color.toUpperCase() ? " selected" : "")}
            style={{ backgroundColor: color }} aria-label={`Цвет ${color}`} aria-pressed={friend.color?.toUpperCase() === color.toUpperCase()}
            disabled={app.friends.some(item => item.id !== friend.id && item.color?.toUpperCase() === color.toUpperCase())}
            onClick={() => app.saveFriends(app.friends.map(item => item.id === friend.id ? { ...item, color } : item))} />)}
        </div>
        {error && <p className="muted" role="alert">{error}</p>}
        <button className="btn primary" type="submit">Сохранить</button>
      </form>}
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
  const [editorOpen, setEditorOpen] = useState(false);
  const [subject, setSubject] = useState("");
  const [text, setText] = useState("");
  const [share, setShare] = useState(false);
  const [pending, setPending] = useState<{ file: File; kind: "photo" | "document" }[]>([]);
  const [communityId, setCommunityId] = useState("");
  const [copies, setCopies] = useState<GroupHomeworkCopy[]>([]);
  const [copiesLoading, setCopiesLoading] = useState(false);
  const [note, setNote] = useState("");
  const [copiesFailed, setCopiesFailed] = useState(false);
  const [copiesRetry, setCopiesRetry] = useState(0);
  const subjects = [...new Set(app.lessons.map(lesson => lesson.subjectRaw))];
  useEffect(() => { if (!app.session?.authenticated) setShare(false); }, [app.session?.authenticated]);
  useEffect(() => {
    let stop = false;
    setCopiesLoading(!!app.session?.authenticated && !!app.groupId);
    setCommunityId("");
    setCopies([]);
    setCopiesFailed(false);
    void followGroupCommunity(
      { authenticated: !!app.session?.authenticated, groupId: app.groupId },
      groupId => api.communities(groupId),
      state => {
        if (stop) return;
        setCommunityId(state.communityId);
        if (!state.communityId) { setCopies([]); setCopiesLoading(false); }
        if (state.failed) { setCopiesFailed(true); setCopiesLoading(false); }
        else if (state.communityId) void api.groupHomework(state.communityId)
          .then(loaded => { if (!stop) setCopies(loaded); })
          .catch(() => { if (!stop) setCopiesFailed(true); })
          .finally(() => { if (!stop) setCopiesLoading(false); });
      },
    );
    return () => { stop = true; };
  }, [app.session, app.groupId, copiesRetry]);
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
    setEditorOpen(false);
    if (outcome.sent) {
      try { setCopies(await api.groupHomework(communityId)); }
      catch { setCopies([]); setCopiesFailed(true); }
    }
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
      <Head title="Домашка" text={note || "Хранится на этом устройстве. Галочка отправляет ту же домашку всей группе."}>
        <button className="btn primary" type="button" aria-expanded={editorOpen} aria-controls="homework-editor"
          onClick={() => setEditorOpen(value => !value)}>{editorOpen ? "Скрыть редактор" : "Добавить задание"}</button>
      </Head>
      <div className="section-overview">
        <strong>Мои задания: {app.homework.length} · Общие: {copiesLoading ? "загрузка…" : copies.length}</strong>
        <span>Выполнено: {app.homework.filter(item => item.done).length + copies.filter(item => item.completed).length}</span>
      </div>
      <form id="homework-editor" className="card stack homework-compose" hidden={!editorOpen} onSubmit={event => void add(event)}>
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
          <input type="checkbox" checked={share} disabled={!app.session?.authenticated} onChange={event => setShare(event.target.checked)} />
          <span>Дублировать всей группе<span className="muted">{app.session?.authenticated ? " — одна и та же домашка появится у всех участников" : " — войдите в аккаунт, чтобы отправить группе"}</span></span>
        </label>
        <button className="btn primary" type="submit">Сохранить</button>
      </form>
      {!copiesLoading && !copiesFailed && app.homework.length === 0 && copies.length === 0 && !editorOpen &&
        <div className="card empty">Заданий пока нет. <button className="btn" type="button" onClick={() => setEditorOpen(true)}>Добавить первое</button></div>}
      {copiesFailed && <div className="card empty"><p>Общая домашка не загрузилась. Проверьте сеть и попробуйте ещё раз.</p><button className="btn primary" type="button" onClick={() => setCopiesRetry(value => value + 1)}>Повторить</button></div>}
      {copies.length > 0 && <h2 className="section-list-title">Всей группе <span className="chip">{copies.length}</span></h2>}
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
      {app.homework.length > 0 && <h2 className="section-list-title">Мои задания <span className="chip">{app.homework.length}</span></h2>}
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

function communityRoleLabel(role: string | null): string {
  return role === "headman" ? "Староста" : role === "curator" ? "Куратор"
    : role === "member" ? "Участник" : "Не в группе";
}

export function CommunityPage() {
  const app = useApp();
  const [list, setList] = useState<Community[]>([]);
  const [error, setError] = useState("");
  const [search, setSearch] = useState("");
  const [reloadEpoch, setReloadEpoch] = useState(0);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    if (!app.session?.authenticated) return;
    let stopped = false;
    setError("");
    setLoading(true);
    api.communities(app.groupId).then(rows => { if (!stopped) { setList(rows); setLoading(false); } })
      .catch(() => { if (!stopped) { setError("Сообщества не открылись"); setLoading(false); } });
    return () => { stopped = true; };
  }, [app.session, app.groupId, reloadEpoch]);
  const visible = list.filter(item => matchesBrowseQuery(search, item.name, item.description));
  if (!app.session?.authenticated) return <section className="page"><div className="card empty"><h1>Сообщество</h1><p>Войдите в аккаунт, чтобы видеть сообщества своей группы.</p><Link className="btn primary" to="/settings">Открыть настройки</Link></div></section>;
  return (
    <section className="page">
      <Head title="Сообщество" text={error || "Роли старосты и куратора действуют только внутри «Расписание военмех» и не подтверждены университетом."} />
      <div className="stack">
        <div className="community-filter">
          <label className="field">Поиск группы
            <input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Название или описание" />
          </label>
          <div className="row"><span className="muted">Показано {visible.length} из {list.length}</span>
            <span className="muted">Моих сообществ: {list.filter(item => item.role != null).length}</span>
            {!!search.trim() && <button className="btn" type="button" onClick={() => setSearch("")}>Сбросить поиск</button>}
            {error && <button className="btn" type="button" onClick={() => setReloadEpoch(value => value + 1)}>Повторить</button>}
          </div>
        </div>
        {loading && <p className="muted" role="status">Загрузка сообществ…</p>}
        {visible.map(item => (
          <article className="card" key={item.communityId}>
            <h2>{item.name}</h2>
            <p className="muted">{item.description}</p>
            <div className="row">
              <span className="chip">{communityRoleLabel(item.role)}</span>
              {!item.role && <button className="btn" type="button" onClick={() => void api.joinCommunity(item.communityId).then(() => api.communities(app.groupId).then(setList))}>Подать заявку</button>}
            </div>
          </article>
        ))}
        {!loading && !error && visible.length === 0 && <div className="empty">{list.length === 0 ? "Сообществ пока нет" : "Группы по запросу не найдены"}
          {list.length > 0 && <button className="btn" type="button" onClick={() => setSearch("")}>Показать все</button>}</div>}
      </div>
    </section>
  );
}

const groupReactions = [["like", "👍"], ["heart", "❤️"], ["laugh", "😂"], ["wow", "😮"], ["sad", "😢"]] as const;

function groupMessageDay(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso.slice(0, 10)
    : date.toLocaleDateString("ru-RU", { day: "numeric", month: "long", year: "numeric" });
}

function groupMessageTime(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? "" : date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
}

function groupTopicWhen(iso: string | null): string {
  if (!iso) return "";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return date.toDateString() === new Date().toDateString()
    ? date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })
    : date.toLocaleDateString("ru-RU", { day: "numeric", month: "short" });
}

const defaultMessageFilter: MessageBrowseFilter = { query: "", author: "all", kind: "all" };

export function GroupPage() {
  const app = useApp();
  const location = useLocation();
  const query = new URLSearchParams(location.search);
  const selectedCommunityId = query.get("communityId")?.match(/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i)?.[0] ?? null;
  const selectedConversationId = query.get("conversationId")?.match(/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i)?.[0] ?? null;
  const groupViewKey = `${app.groupId}:${selectedCommunityId ?? ""}:${selectedConversationId ?? ""}:${app.session?.user?.userId ?? ""}:${app.session?.authenticated ? "in" : "out"}`;
  const groupViewKeyRef = useRef(groupViewKey);
  groupViewKeyRef.current = groupViewKey;
  const [homeState, setHomeState] = useState<{ key: string; value: GroupHome | null }>({ key: groupViewKey, value: null });
  const home = homeState.key === groupViewKey ? homeState.value : null;
  const [desk, setDesk] = useState<GroupDesk | null>(null);
  const [canManageChannels, setCanManageChannels] = useState(false);
  const [board, setBoard] = useState<BallotBoard | null>(null);
  const [channelBoardState, setChannelBoardState] = useState<{ topicId: string; value: BallotBoard | null; failed: boolean }>({ topicId: "", value: null, failed: false });
  const [votesOff, setVotesOff] = useState(false);
  const [votesRetry, setVotesRetry] = useState(0);
  const [channelVotesRetry, setChannelVotesRetry] = useState(0);
  const [selectedChat, setChat] = useState<Conversation | null>(null);
  const chat = home ? selectedChat : null;
  const [thread, setThread] = useState<GroupTopic | "list">("list");
  const [topicPageState, setTopicPageState] = useState<{ key: string; value: GroupTopicPage | null }>({ key: "", value: null });
  const [nextUnreadBusy, setNextUnreadBusy] = useState(false);
  const viewKey = chat && (chat.kind !== "group" || (thread !== "list" && isChatChannel(thread)))
    ? `${chat.conversationId}:${chat.kind === "group" && thread !== "list" ? thread.topicId ?? "general" : "direct"}`
    : "";
  const selectedTopicId = chat?.kind === "group" && thread !== "list" ? thread.topicId : undefined;
  const activeBallotTopicId = chat?.kind === "group" && thread !== "list" && thread.kind === "ballots" ? thread.topicId : null;
  const channelBoard = activeBallotTopicId && channelBoardState.topicId === activeBallotTopicId ? channelBoardState.value : null;
  const channelBoardFailed = !!activeBallotTopicId && channelBoardState.topicId === activeBallotTopicId && channelBoardState.failed;
  const viewKeyRef = useRef(viewKey);
  viewKeyRef.current = viewKey;
  const [logState, setLogState] = useState<{ key: string; messages: ChatMessage[]; hasOlder: boolean }>({ key: "", messages: [], hasOlder: false });
  const logRef = useRef(logState);
  const log = logState.key === viewKey ? logState.messages : [];
  const hasOlder = logState.key === viewKey && logState.hasOlder;
  const [messageFilter, setMessageFilter] = useState<MessageBrowseFilter>(defaultMessageFilter);
  const [messageBrowseOpen, setMessageBrowseOpen] = useState(false);
  const [contextExpandedByGroup, setContextExpandedByGroup] = useState<Record<string, boolean>>({});
  const visibleLog = filterMessages(log, messageFilter, app.session?.user?.userId || "");
  const messageFiltered = !!messageFilter.query.trim() || messageFilter.author !== "all" || messageFilter.kind !== "all";
  const [copyNotice, setCopyNotice] = useState<{ key: string; text: string } | null>(null);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const draft = drafts[viewKey] ?? "";
  const draftEpoch = useRef<Record<string, number>>({});
  const [replyTo, setReplyTo] = useState<string | null>(null);
  const [editing, setEditing] = useState<ChatMessage | null>(null);
  const editReturnDraft = useRef<{ key: string; messageId: string; value: string } | null>(null);
  const repliedMessage = replyTo ? log.find(item => item.messageId === replyTo) : null;
  const [menu, setMenu] = useState<string | null>(null);
  const [reactionFor, setReactionFor] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [focusChat, setFocusChat] = useState(false);
  const [memberSearch, setMemberSearch] = useState("");
  const [reloadEpoch, setReloadEpoch] = useState(0);
  const [atLatest, setAtLatest] = useState(true);
  const [mediaBusy, setMediaBusy] = useState<string[]>([]);
  const mediaPending = useRef(new Set<string>());
  const [loadingOlder, setLoadingOlder] = useState<string[]>([]);
  const olderPending = useRef(new Set<string>());
  const logBoxRef = useRef<HTMLDivElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const pendingPick = useRef<{ kind: "image" | "video" | "file"; key: string; conversationId: string; epoch: number; replyTo: string | null } | null>(null);
  const holdTimer = useRef(0);
  const heldOpen = useRef(false);
  const sendPending = useRef(new Set<string>());
  const selectionEpoch = useRef(0);
  const pollerRef = useRef<{ key: string; poller: ReturnType<typeof createGroupPoller> } | null>(null);
  const deskEpoch = useRef(0);

  function updateDesk(value: GroupDesk | null) {
    deskEpoch.current += 1;
    setDesk(value);
  }

  function setHome(value: GroupHome | null) {
    if (groupViewKeyRef.current === groupViewKey) setHomeState({ key: groupViewKey, value });
  }

  function setDraft(value: string) {
    if (!viewKey) return;
    draftEpoch.current[viewKey] = (draftEpoch.current[viewKey] ?? 0) + 1;
    setDrafts(current => ({ ...current, [viewKey]: value }));
  }

  function cancelComposerContext() {
    if (editing && editReturnDraft.current?.key === viewKey) {
      setDraft(editReturnDraft.current.value);
      editReturnDraft.current = null;
    }
    setEditing(null);
    setReplyTo(null);
  }

  function clearLog() {
    logRef.current = { key: "", messages: [], hasOlder: false };
    setLogState(logRef.current);
  }

  function updateLog(key: string, incoming: ChatMessage[], initialHasOlder?: boolean) {
    if (viewKeyRef.current !== key) return;
    const current = logRef.current.key === key ? logRef.current : { messages: [], hasOlder: false };
    logRef.current = {
      key,
      messages: mergeGroupMessages(current.messages, incoming),
      hasOlder: initialHasOlder ?? current.hasOlder,
    };
    setLogState(logRef.current);
  }

  function prependOlder(key: string, incoming: ChatMessage[], more: boolean) {
    if (viewKeyRef.current !== key) return;
    const current = logRef.current.key === key ? logRef.current.messages : [];
    logRef.current = { key, messages: mergeGroupMessages(incoming, current), hasOlder: more };
    setLogState(logRef.current);
  }

  function markLogChanged(key: string) {
    if (pollerRef.current?.key === key) pollerRef.current.poller.changed();
  }
  useEffect(() => {
    let stop = false;
    const drop = () => {
      selectionEpoch.current += 1;
      setHome(null);
      setChat(null);
      setThread("list");
      setFocusChat(false);
      updateDesk(null);
      setCanManageChannels(false);
      setTopicPageState({ key: "", value: null });
      setBoard(null);
      setVotesOff(false);
      setChannelBoardState({ topicId: "", value: null, failed: false });
      clearLog();
    };
    drop();
    const openHome = (id: string) => void api.groupHome(id).then(loaded => {
      if (stop) return;
      setHome(loaded);
      const requested = selectedConversationId === loaded.groupChat.conversationId
        ? loaded.groupChat : loaded.directs.find(item => item.conversationId === selectedConversationId);
      setThread(requested?.kind === "group"
        ? { topicId: null, title: "Общий поток", icon: "💬", lastBody: requested.lastBody,
          lastAuthor: null, lastAt: requested.lastAt, unread: requested.unread, canDelete: false, kind: "chat", activeBallots: 0,
          description: "", accent: "default", pinned: false, writePolicy: "all", canPost: true }
        : "list");
      setChat(requested ?? loaded.groupChat);
      setFocusChat(!!requested);
    }).catch(() => { if (!stop) { drop(); setError("Не удалось загрузить группу"); } });
    if (app.session?.authenticated && selectedCommunityId) {
      openHome(selectedCommunityId);
      return () => { stop = true; };
    }
    void openGroupFace(
      { authenticated: !!app.session?.authenticated, groupId: app.groupId },
      groupId => api.communities(groupId),
      face => {
        if (stop) return;
        setError(face.error);
        if (!face.communityId) return;
        openHome(face.communityId);
      },
    );
    return () => { stop = true; };
  }, [app.session, app.groupId, selectedCommunityId, selectedConversationId, reloadEpoch]);
  useEffect(() => {
    const previousEdit = editReturnDraft.current;
    if (previousEdit) {
      setDrafts(current => ({ ...current, [previousEdit.key]: previousEdit.value }));
      editReturnDraft.current = null;
    }
    setReplyTo(null); setEditing(null); setMenu(null); setReactionFor(null);
  }, [viewKey]);
  useEffect(() => { setMessageFilter(defaultMessageFilter); setMessageBrowseOpen(false); setCopyNotice(null); }, [viewKey]);
  useEffect(() => { setAtLatest(true); }, [viewKey]);
  useEffect(() => {
    if (atLatest && logBoxRef.current) logBoxRef.current.scrollTop = logBoxRef.current.scrollHeight;
  }, [atLatest, log.length, viewKey]);
  useEffect(() => {
    if (!menu) return;
    const node = document.querySelector(".log .actions");
    if (node instanceof HTMLElement) node.scrollIntoView({ block: "nearest" });
  }, [menu]);
  useEffect(() => {
    clearLog();
    if (!chat || !viewKey) return;
    const topic = chat.kind === "group" && thread !== "list" ? (thread.topicId ?? "general") : undefined;
    let wantedReadId: string | null = null;
    let markedReadId: string | null = null;
    let markingRead = false;
    const markDirectRead = () => {
      if (!wantedReadId || wantedReadId === markedReadId || markingRead) return;
      const target = wantedReadId;
      markingRead = true;
      void api.markRead(chat.conversationId)
        .then(() => { markedReadId = target; })
        .catch(() => undefined)
        .finally(() => { markingRead = false; });
    };
    const poller = createGroupPoller(
      after => api.messages(chat.conversationId, topic, after ? { after } : undefined),
      () => logRef.current.key === viewKey ? logRef.current.messages : [],
      (updates, firstLoad) => {
        const known = logRef.current.key === viewKey ? logRef.current.messages : [];
        const incoming = chat.kind !== "group" ? newestUnseenIncoming(known, updates.messages, app.session?.user?.userId || "") : null;
        updateLog(viewKey, updates.messages, firstLoad ? updates.hasOlder : undefined);
        if (chat.kind !== "group") {
          if (firstLoad) wantedReadId = updates.messages.at(-1)?.messageId ?? "open";
          if (incoming) wantedReadId = incoming.messageId;
          markDirectRead();
        }
      },
      () => { if (viewKeyRef.current === viewKey) setError("Чат не обновился"); },
    );
    pollerRef.current = { key: viewKey, poller };
    void poller.poll();
    const timer = window.setInterval(() => void poller.poll(), 4000);
    return () => {
      poller.dispose();
      if (pollerRef.current?.poller === poller) pollerRef.current = null;
      window.clearInterval(timer);
    };
  }, [viewKey]);
  const communityId = home?.communityId ?? "";
  const topicPage = topicPageState.key === communityId ? topicPageState.value : null;
  const selectedAcademicGroup = app.catalog?.groups.find(group => group.id === app.groupId)?.name ?? null;
  const groupContext = buildGroupChatContext({
    communityGroupName: home?.groupName ?? null,
    selectedGroupName: selectedAcademicGroup,
    timetableAvailable: app.timetableAvailable,
    lessons: app.lessons,
    subgroupChoices: app.subgroups[app.groupId] ?? {},
    period: app.catalog?.period ?? null,
    invert: app.invert,
    topics: topicPage?.topics ?? [],
    now: new Date(),
  });
  const contextExpanded = contextExpandedByGroup[communityId] !== false;
  const activeBallotTopic = topicPage?.topics.find(topic => topic.topicId !== null && topic.kind === "ballots" && topic.activeBallots > 0);
  const nextUnread = chat?.kind === "group" && thread !== "list" && topicPage
    ? nextUnreadTopic(topicPage.topics, thread.topicId) : null;
  useEffect(() => {
    if (!app.session?.authenticated || !communityId) return;
    let stop = false;
    const refresh = () => {
      const ticket = ++deskEpoch.current;
      void api.groupDesk(communityId).then(office => {
        if (!stop && ticket === deskEpoch.current) setDesk(office);
      }).catch(() => { /* Keep the last good desk on a transient outage. */ });
    };
    refresh();
    const timer = window.setInterval(refresh, 4000);
    return () => { stop = true; deskEpoch.current += 1; window.clearInterval(timer); };
  }, [app.session?.authenticated, communityId]);
  useEffect(() => {
    if (!communityId || selectedTopicId === undefined) return;
    let stop = false;
    const refresh = () => {
      void api.topics(communityId).then(page => {
        if (stop) return;
        setTopicPageState({ key: communityId, value: page });
        setCanManageChannels(page.canManageChannels);
        const selected = page.topics.find(item => item.topicId === selectedTopicId);
        if (selected) setThread(selected);
        else {
          selectionEpoch.current += 1;
          clearLog();
          setReplyTo(null);
          setEditing(null);
          setThread("list");
        }
      }).catch(() => { /* Current messages remain readable; the server still checks writes. */ });
    };
    refresh();
    const timer = window.setInterval(refresh, 4000);
    return () => { stop = true; window.clearInterval(timer); };
  }, [communityId, selectedTopicId]);
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
  }, [app.session?.authenticated, communityId, votesRetry]);
  useEffect(() => {
    if (!communityId || !activeBallotTopicId) return;
    const topicId = activeBallotTopicId;
    let stop = false;
    const pull = () => api.ballots(communityId, topicId).then(value => {
      if (!stop) setChannelBoardState({ topicId, value, failed: false });
    }).catch(() => { if (!stop) setChannelBoardState(current => current.topicId === topicId
      ? { ...current, failed: true } : { topicId, value: null, failed: true }); });
    void pull();
    const timer = window.setInterval(pull, 4000);
    return () => { stop = true; window.clearInterval(timer); };
  }, [communityId, activeBallotTopicId, channelVotesRetry]);
  function choose(kind: "image" | "video" | "file") {
    if (!chat || !viewKey) return;
    const input = fileRef.current;
    if (!input) return;
    if (chat.kind === "group" && (thread === "list" || !canComposeChannel(thread))) return;
    pendingPick.current = { kind, key: viewKey, conversationId: chat.conversationId, epoch: selectionEpoch.current, replyTo };
    input.accept = kind === "image" ? "image/*" : kind === "video" ? "video/*" : "*/*";
    input.click();
  }
  async function onPicked(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    const pick = pendingPick.current;
    pendingPick.current = null;
    if (!file || !pick || !chat || !groupMediaSelectionIsCurrent(pick,
      { key: viewKey, conversationId: chat.conversationId, epoch: selectionEpoch.current })) return;
    if (chat.kind === "group" && (thread === "list" || !canComposeChannel(thread))) return;
    await sendGroupAttachment(pick.key, pick.conversationId, pick.kind, file.name, file, pick.replyTo, pick.epoch,
      undefined, chat.kind === "group" && thread !== "list" ? thread.topicId ?? undefined : undefined);
  }
  async function sendGroupAttachment(key: string, conversationId: string, kind: "image" | "video" | "file" | "voice" | "circle",
    name: string, blob: Blob, sentReply: string | null, epoch: number, durationMs?: number, topicId?: string) {
    markLogChanged(key);
    try {
      const message = await api.sendGroupMedia(conversationId, kind, name, blob, sentReply ?? undefined, durationMs, topicId);
      markLogChanged(key);
      updateLog(key, [message]);
      if (viewKeyRef.current === key && selectionEpoch.current === epoch) setReplyTo(current => current === sentReply ? null : current);
    } catch (reason) {
      if (viewKeyRef.current === key && selectionEpoch.current === epoch) setError(reason instanceof GroupMediaError && reason.code === "size" || reason instanceof Error && reason.message === "413"
        ? "Файл слишком большой." : reason instanceof GroupMediaError && reason.code === "format" ? "Формат записи не поддерживается" : "Сообщение не отправилось");
    }
  }
  async function downloadMedia(download: GroupMediaDownload) {
    if (mediaPending.current.has(download.href)) return;
    const key = viewKey;
    const epoch = selectionEpoch.current;
    mediaPending.current.add(download.href);
    setMediaBusy([...mediaPending.current]);
    try {
      const blob = await api.groupMedia(download);
      if (blob.size === 0) throw new Error("empty media");
      const objectUrl = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = objectUrl;
      link.download = download.filename;
      link.hidden = true;
      document.body.appendChild(link);
      try { link.click(); }
      finally {
        link.remove();
        window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
      }
    } catch { if (viewKeyRef.current === key && selectionEpoch.current === epoch) setError("Файл не загрузился"); }
    finally {
      mediaPending.current.delete(download.href);
      setMediaBusy([...mediaPending.current]);
    }
  }
  async function earlier() {
    if (!chat || !viewKey || !hasOlder || olderPending.current.has(viewKey)) return;
    const first = logRef.current.key === viewKey ? logRef.current.messages[0] : null;
    if (!first) return;
    const key = viewKey;
    const topic = chat.kind === "group" && thread !== "list" ? (thread.topicId ?? "general") : undefined;
    olderPending.current.add(key);
    setLoadingOlder([...olderPending.current]);
    try {
      const page = await api.messages(chat.conversationId, topic, { before: first.messageId });
      if (viewKeyRef.current !== key) return;
      const node = logBoxRef.current;
      const top = node?.scrollTop ?? 0;
      const height = node?.scrollHeight ?? 0;
      const anchor = node?.querySelector<HTMLElement>("article.bubble");
      const anchorTop = anchor?.getBoundingClientRect().top;
      prependOlder(key, page.messages, page.hasMore);
      window.requestAnimationFrame(() => {
        if (!node?.isConnected || viewKeyRef.current !== key) return;
        const shift = anchor?.isConnected && anchorTop !== undefined
          ? anchor.getBoundingClientRect().top - anchorTop
          : node.scrollHeight - height;
        node.scrollTop = top + shift;
      });
    } catch { if (viewKeyRef.current === key) setError("Старые сообщения не загрузились"); }
    finally {
      olderPending.current.delete(key);
      setLoadingOlder([...olderPending.current]);
    }
  }
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!chat || !viewKey || !draft.trim() || sendPending.current.has(viewKey)) return;
    if (chat.kind === "group" && (thread === "list" || !canComposeChannel(thread))) return;
    const key = viewKey;
    const body = draft;
    const sentDraftEpoch = draftEpoch.current[key] ?? 0;
    const target = editing;
    const sentReply = replyTo;
    const previousDraft = target && editReturnDraft.current?.key === key && editReturnDraft.current.messageId === target.messageId
      ? editReturnDraft.current.value : null;
    sendPending.current.add(key);
    markLogChanged(key);
    try {
      const message = target
        ? await api.editGroupMessage(chat.conversationId, target.messageId, body)
        : chat.kind === "group" && thread !== "list"
          ? await api.sendTopicMessage(chat.conversationId, body, thread.topicId, sentReply ?? undefined)
          : await api.sendMessage(chat.conversationId, body, sentReply ?? undefined);
      markLogChanged(key);
      updateLog(key, [message]);
      setDrafts(current => {
        const unchanged = draftEpoch.current[key] === sentDraftEpoch;
        const cleared = clearSentGroupDraft(current, key, body, unchanged);
        return target && previousDraft !== null && unchanged ? { ...cleared, [key]: previousDraft } : cleared;
      });
      if (target && editReturnDraft.current?.key === key && editReturnDraft.current.messageId === target.messageId)
        editReturnDraft.current = null;
      if (viewKeyRef.current === key) {
        if (target) setEditing(current => current?.messageId === target.messageId ? null : current);
        setReplyTo(current => current === sentReply ? null : current);
      }
    } catch { if (viewKeyRef.current === key) setError(target ? "Изменение не сохранилось" : "Сообщение не отправилось"); }
    finally { sendPending.current.delete(key); }
  }
  function reactTo(message: ChatMessage, emoji: string) {
    if (!chat) return;
    const key = viewKey;
    markLogChanged(key);
    void api.reactGroupMessage(chat.conversationId, message.messageId, emoji)
      .then(next => { markLogChanged(key); updateLog(key, [next]); if (viewKeyRef.current === key) setReactionFor(null); })
      .catch(() => { if (viewKeyRef.current === key) setError("Реакция не сохранилась"); });
  }
  async function openNextUnread() {
    if (!communityId || chat?.kind !== "group" || thread === "list" || nextUnreadBusy) return;
    const ticket = selectionEpoch.current;
    setNextUnreadBusy(true);
    try {
      const page = await api.topics(communityId);
      if (selectionEpoch.current !== ticket || groupViewKeyRef.current !== groupViewKey) return;
      setTopicPageState({ key: communityId, value: page });
      const next = nextUnreadTopic(page.topics, thread.topicId);
      if (!next) { setError("Непрочитанных каналов нет"); return; }
      selectionEpoch.current += 1;
      clearLog();
      setCanManageChannels(page.canManageChannels);
      setThread(next);
    } catch { if (groupViewKeyRef.current === groupViewKey) setError("Не удалось проверить непрочитанные каналы"); }
    finally { setNextUnreadBusy(false); }
  }
  async function copyGroupMessage(message: ChatMessage) {
    if (!canCopyMessageText(message)) return;
    const key = viewKey;
    setMenu(null);
    try {
      await navigator.clipboard.writeText(message.body);
      if (viewKeyRef.current === key) setCopyNotice({ key, text: "Текст скопирован" });
    } catch {
      if (viewKeyRef.current === key) setCopyNotice({ key, text: "Не удалось скопировать текст" });
    }
  }
  if (!app.session?.authenticated) return <section className="page"><div className="card empty"><h1>Группа</h1><p>Войдите в аккаунт, чтобы открыть группу, разделы чата и голосования.</p><Link className="btn primary" to="/settings">Открыть настройки</Link></div></section>;
  return (
    <section className="page">
      <Head title="Группа" text={home ? `${home.name}${home.groupName ? " · " + home.groupName : ""}` : error || "Одногруппники и чат"} />
      {!home && error && <button className="btn" type="button" onClick={() => setReloadEpoch(value => value + 1)}>Повторить загрузку группы</button>}
      {home && desk && desk.mine.length > 0 && <GroupAdmin communityId={home.communityId} classmates={home.classmates} desk={desk} onChange={updateDesk} onReload={async () => { const [loaded, office] = await Promise.all([api.groupHome(home.communityId), api.groupDesk(home.communityId)]); setHome(loaded); updateDesk(office); }} onError={setError} />}
      {home && !board && !votesOff && <p className="muted" role="status">Загрузка голосований…</p>}
      {home && votesOff && <div className="banner row" role="status"><span>{board ? "Голосования не обновились. Показана предыдущая доска." : "Голосования сейчас не открылись. Чат группы на месте."}</span>
        <button className="btn" type="button" onClick={() => setVotesRetry(value => value + 1)}>Повторить</button></div>}
      {home && board && <BallotBoardView communityId={home.communityId} board={board} classmates={home.classmates} roles={desk?.roles ?? []} onChange={setBoard} onError={setError} />}
      {home && (
        <div className={"grid-2 split" + (focusChat ? " focus" : "")}>
          <div className="people split-list">
            <label className="field">Поиск участника
              <input type="search" value={memberSearch} onChange={event => setMemberSearch(event.target.value)}
                placeholder="Имя или логин" />
            </label>
            <button className="person" type="button" onClick={() => { selectionEpoch.current += 1; clearLog(); setChat(home.groupChat); setThread("list"); setFocusChat(true); }}><span><b>Чат группы</b><div className="muted">Разделы и общий поток</div></span>{home.groupChat.unread > 0 && <span className="chip" aria-label={unreadBadgeDescription(home.groupChat.unread)}>{unreadBadgeText(home.groupChat.unread)}</span>}</button>
            {chat?.kind === "group" && thread !== "list" && topicPage && <div className="group-quick-topics">
              <h2>Каналы группы</h2>
              {orderedTopics(topicPage.topics).filter(topic => topic.topicId !== null || topic.kind === "chat").map(topic => {
                const active = topic.topicId === thread.topicId && topic.kind === thread.kind;
                return <button key={`${topic.kind}:${topic.topicId ?? "general"}`} className={active ? "group-quick-topic active" : "group-quick-topic"}
                  type="button" aria-current={active ? "page" : undefined} onClick={() => {
                    if (active) return;
                    selectionEpoch.current += 1;
                    clearLog();
                    setThread(topic);
                  }}>
                  <span className="group-quick-topic-top"><b>{topic.icon} {topic.title}</b><span className="muted">{groupTopicWhen(topic.lastAt)}</span></span>
                  <span className="group-quick-topic-bottom"><span className="muted">{topicPreview(topic)}</span>
                    {topic.unread > 0 && <span className="chip" aria-label={unreadBadgeDescription(topic.unread)}>{unreadBadgeText(topic.unread)}</span>}</span>
                </button>;
              })}
            </div>}
            {home.classmates.filter(person => matchesBrowseQuery(memberSearch, person.displayName, person.username)).map(person => (
              <button className="person" key={person.userId} type="button" disabled={person.self} onClick={() => {
                if (!home || person.self) return;
                const epoch = ++selectionEpoch.current;
                clearLog();
                setChat(null);
                setThread("list");
                setFocusChat(true);
                void api.openDirect(home.communityId, person.userId)
                  .then(next => { if (selectionEpoch.current === epoch) setChat(next); })
                  .catch(() => { if (selectionEpoch.current === epoch) setError("Личный чат не открылся"); });
              }}>
                <span><b>{person.displayName || person.username}</b><div className="muted">@{person.username}</div></span>
                <span className="row">{[person.role === "headman" ? "Староста" : person.role === "curator" ? "Куратор" : "Участник", ...titlesOf(desk, person.userId)].map(title => <span className="chip" key={title}>{title}</span>)}</span>
              </button>
            ))}
            {!!memberSearch.trim() && !home.classmates.some(person => matchesBrowseQuery(memberSearch, person.displayName, person.username)) &&
              <div className="empty">Участник не найден <button className="btn" type="button" onClick={() => setMemberSearch("")}>Сбросить поиск</button></div>}
            {home.directs.map(item => <button className="person" key={item.conversationId} type="button" onClick={() => { selectionEpoch.current += 1; clearLog(); setChat(item); setThread("list"); setFocusChat(true); }}><span><b>{item.title}</b><div className="muted">{item.lastBody}</div></span></button>)}
          </div>
          <section className="card chat split-detail">
            <button className="btn back-only" type="button" onClick={() => setFocusChat(false)}>К списку</button>
            {!chat && <p className="muted">{error || "Чат открывается…"}</p>}
            {chat?.kind === "group" && thread === "list" && <GroupTopics key={home.communityId} communityId={home.communityId} onOpen={(next, allowed) => { selectionEpoch.current += 1; clearLog(); setCanManageChannels(allowed); setThread(next); }} onError={setError} />}
            {chat?.kind === "group" && thread !== "list" && !isChatChannel(thread) && <>
              <div className="row">
                <button className="btn" type="button" onClick={() => { selectionEpoch.current += 1; setThread("list"); }}>Все разделы</button>
                <h2>{thread.icon} {thread.title}</h2>
                <button className="btn" type="button" disabled={!nextUnread || nextUnreadBusy} onClick={() => void openNextUnread()}
                  title={nextUnread ? `Открыть: ${nextUnread.title}` : "Непрочитанных каналов нет"}>{nextUnreadBusy ? "Проверяем…" : "Следующий непрочитанный"}</button>
              </div>
              {thread.description && <p className="muted">{thread.description}</p>}
              {activeBallotTopicId && channelBoard && <BallotBoardView key={activeBallotTopicId} communityId={home.communityId} board={channelBoard}
                classmates={home.classmates} roles={desk?.roles ?? []} topicId={activeBallotTopicId} title={thread.title}
                canCreate={canCreateBallot(thread, canManageChannels)}
                onChange={value => setChannelBoardState({ topicId: activeBallotTopicId, value, failed: false })} onError={setError} />}
              {channelBoardFailed && <div className="banner row" role="status"><span>{channelBoard ? "Голосования не обновились. Показана предыдущая доска." : "Голосования не открылись."}</span>
                <button className="btn" type="button" onClick={() => setChannelVotesRetry(value => value + 1)}>Повторить</button></div>}
              {!channelBoard && !channelBoardFailed && <p className="muted" role="status">Загрузка голосований…</p>}
            </>}
            {chat && (chat.kind !== "group" || (thread !== "list" && isChatChannel(thread))) && <>
            <div className="row">
              {chat?.kind === "group" && <button className="btn" type="button" onClick={() => { selectionEpoch.current += 1; clearLog(); setThread("list"); }}>Все разделы</button>}
              <h2>{chat?.kind === "group" && thread !== "list" ? `${thread.icon} ${thread.title}` : (chat?.title || "Чат")}</h2>
              {chat.kind === "group" && <button className="btn" type="button" disabled={!nextUnread || nextUnreadBusy} onClick={() => void openNextUnread()}
                title={nextUnread ? `Открыть: ${nextUnread.title}` : "Непрочитанных каналов нет"}>{nextUnreadBusy ? "Проверяем…" : "Следующий непрочитанный"}</button>}
            </div>
            {chat.kind === "group" && thread !== "list" && topicPage && <nav className="group-quick-rail" aria-label="Быстрые каналы группы">
              <div className="group-quick-rail-items">
                {orderedTopics(topicPage.topics).filter(topic => topic.topicId !== null || topic.kind === "chat").map(topic => {
                  const active = topic.topicId === thread.topicId && topic.kind === thread.kind;
                  return <button key={`${topic.kind}:${topic.topicId ?? "general"}`} className={active ? "group-quick-rail-item active" : "group-quick-rail-item"}
                    type="button" aria-current={active ? "page" : undefined} onClick={() => {
                      if (active) return;
                      selectionEpoch.current += 1; clearLog(); setThread(topic);
                    }}>
                    <span>{topic.title}</span>
                    {topic.unread > 0 && <span className="chip" aria-label={unreadBadgeDescription(topic.unread)}>{unreadBadgeText(topic.unread)}</span>}
                  </button>;
                })}
              </div>
              <button className="btn" type="button" onClick={() => { selectionEpoch.current += 1; clearLog(); setThread("list"); }}>Все каналы</button>
            </nav>}
            {chat.kind === "group" && thread !== "list" && groupContext.hasContent && <section className="group-context" aria-label="Контекст группы">
              <div className="row group-context-head">
                <b>В группе сейчас</b>
                <button className="btn" type="button" aria-expanded={contextExpanded}
                  onClick={() => setContextExpandedByGroup(current => ({ ...current, [communityId]: !contextExpanded }))}>
                  {contextExpanded ? "Свернуть" : "Развернуть"}
                </button>
              </div>
              {contextExpanded ? <div className="group-context-items">
                {groupContext.nextLesson && <p><b>Ближайшая пара по расписанию</b> · {groupContext.nextLesson.date.toLocaleDateString("ru-RU", { weekday: "short", day: "numeric", month: "short" })}, {groupContext.nextLesson.time} · {groupContext.nextLesson.subject}{groupContext.nextLesson.room && ` · ${groupContext.nextLesson.room}`}</p>}
                {groupContext.activeBallots > 0 && <div className="row"><span>Активных голосований: {groupContext.activeBallots}</span>
                  {activeBallotTopic && <button className="btn" type="button" onClick={() => { selectionEpoch.current += 1; clearLog(); setThread(activeBallotTopic); }}>Открыть</button>}</div>}
                {groupContext.unread > 0 && <p>Непрочитанных сообщений в каналах: {groupContext.unread}</p>}
              </div> : <p className="muted group-context-collapsed">
                {[groupContext.nextLesson ? `Следующая пара ${groupContext.nextLesson.time}` : null,
                  groupContext.activeBallots > 0 ? `Голосований: ${groupContext.activeBallots}` : null,
                  groupContext.unread > 0 ? `Непрочитано: ${groupContext.unread}` : null].filter(Boolean).join(" · ")}
              </p>}
            </section>}
            {chat.kind === "group" && thread !== "list" && thread.description && <p className="muted">{thread.description}</p>}
            <button className="btn message-browse-toggle" type="button" aria-expanded={messageBrowseOpen}
              onClick={() => setMessageBrowseOpen(value => !value)}>
              {messageBrowseOpen ? "Скрыть поиск" : messageFiltered ? `Поиск и фильтры · ${visibleLog.length} найдено` : "Поиск и фильтры"}
            </button>
            {messageBrowseOpen && <div className="message-browse" role="group" aria-label="Поиск и фильтры сообщений">
              <label className="field">Поиск сообщения
                <input type="search" value={messageFilter.query} onChange={event => setMessageFilter(current => ({ ...current, query: event.target.value }))}
                  placeholder="Текст загруженного сообщения" />
              </label>
              <label className="field">Автор
                <select value={messageFilter.author} onChange={event => setMessageFilter(current => ({ ...current, author: event.target.value as MessageBrowseFilter["author"] }))}>
                  <option value="all">Все</option><option value="mine">Мои</option><option value="others">Других</option>
                </select>
              </label>
              <label className="field">Вид
                <select value={messageFilter.kind} onChange={event => setMessageFilter(current => ({ ...current, kind: event.target.value as MessageBrowseFilter["kind"] }))}>
                  <option value="all">Все</option><option value="text">Текст</option><option value="visual">Фото и видео</option>
                  <option value="file">Документы</option><option value="audio">Голос и кружки</option>
                </select>
              </label>
              <div className="row message-browse-summary"><span className="muted">В загруженных сообщениях: {visibleLog.length} из {log.length}</span>
                {messageFiltered && <button className="btn" type="button" onClick={() => setMessageFilter(defaultMessageFilter)}>Сбросить фильтры</button>}
              </div>
            </div>}
            {copyNotice?.key === viewKey && <p className="muted copy-notice" role="status">{copyNotice.text}</p>}
            <div className="log" ref={logBoxRef} onScroll={event => {
              const node = event.currentTarget;
              setAtLatest(isNearLatest(node.scrollTop, node.clientHeight, node.scrollHeight));
            }}>
              {hasOlder && (visibleLog.length > 0 || !messageFiltered) && <button className="btn" type="button" disabled={loadingOlder.includes(viewKey)} onClick={() => void earlier()}>{loadingOlder.includes(viewKey) ? "Загрузка…" : "Раньше"}</button>}
              {visibleLog.length === 0 && <div className="empty message-empty">{messageFiltered ? "В загруженных сообщениях совпадений нет." : "Сообщений пока нет."}
                {messageFiltered && <button className="btn" type="button" onClick={() => setMessageFilter(defaultMessageFilter)}>Сбросить фильтры</button>}
                {hasOlder && <button className="btn" type="button" disabled={loadingOlder.includes(viewKey)} onClick={() => void earlier()}>{loadingOlder.includes(viewKey) ? "Загрузка…" : "Загрузить раньше"}</button>}
              </div>}
              {visibleLog.map((message, index) => {
                const mine = message.senderId === app.session?.user?.userId;
                const kind = message.kind || "text";
                const canWrite = chat.kind !== "group" || (thread !== "list" && canComposeChannel(thread));
                const actions = [...holdActions(kind, mine, !!message.deleted, menu === message.messageId),
                  ...(menu === message.messageId && canCopyMessageText(message) ? ["copy"] : [])]
                  .filter(action => canWrite || action !== "reply" && action !== "edit");
                const download = chat && groupMediaDownload(chat.conversationId, message);
                const previous = visibleLog[index - 1];
                const newDay = !previous || messageDayKey(previous.createdAt) !== messageDayKey(message.createdAt);
                const grouped = !messageFiltered && !!previous && sameMessageCluster(previous, message);
                return (
                  <Fragment key={message.messageId}>
                  {newDay && <div className="message-day" role="separator">{groupMessageDay(message.createdAt)}</div>}
                  <article data-hold={kind} className={"bubble" + (mine ? " mine" : "") + (grouped ? " grouped" : "") + (kind === "circle" && !message.deleted ? " round" : "")}
                    aria-label={`Сообщение: ${message.senderName}`}
                    onPointerDown={event => { heldOpen.current = false; if (holdTimer.current) window.clearTimeout(holdTimer.current); if (event.target instanceof Element && event.target.closest(".actions, .react, .react-chips, .group-inline-media, .group-media-download, .message-action-toggle")) return; holdTimer.current = window.setTimeout(() => { holdTimer.current = 0; heldOpen.current = true; setMenu(message.messageId); }, 450); }}
                    onPointerUp={event => { if (holdTimer.current) window.clearTimeout(holdTimer.current); if (heldOpen.current && !(event.target instanceof Element && event.target.closest(".actions, .group-inline-media, .group-media-download, .message-action-toggle"))) event.preventDefault(); }}
                    onPointerLeave={() => { if (holdTimer.current) window.clearTimeout(holdTimer.current); }}
                    onClickCapture={event => { if (event.target instanceof Element && event.target.closest(".actions, .group-inline-media, .message-action-toggle")) return; if (event.target instanceof Element && event.target.closest(".group-media-download") && !heldOpen.current) return; if (heldOpen.current || menu === message.messageId) { event.preventDefault(); event.stopPropagation(); } }}>
                    {!mine && !grouped && <b>{message.senderName}</b>}
                    {message.replyTo && <div className="muted">↳ {log.find(item => item.messageId === message.replyTo)?.body || "Сообщение"}</div>}
                    <div>{download
                      ? download.kind === "file"
                        ? <button className="group-media-download" type="button" disabled={mediaBusy.includes(download.href)} onClick={() => { setMenu(null); void downloadMedia(download); }}>{mediaBusy.includes(download.href) ? "Загрузка…" : download.label}</button>
                        : <GroupInlineMedia download={download} busy={mediaBusy.includes(download.href)} onDownload={() => { setMenu(null); void downloadMedia(download); }} />
                      : groupBubbleText(message)}</div>
                    <div className="message-meta"><span className="muted" title={new Date(message.createdAt).toLocaleString("ru-RU")}>{groupMessageTime(message.createdAt)}</span>
                      {!message.deleted && <button className="tool message-action-toggle" type="button" aria-label={`Действия с сообщением: ${message.senderName}`}
                        aria-expanded={menu === message.messageId} onClick={() => setMenu(current => current === message.messageId ? null : message.messageId)}><Icon name="more" size={16} /></button>}</div>
                    {!message.deleted && !!message.reactions?.length && <div className="react-chips">
                      {message.reactions.map(reaction => <button key={reaction.emoji} type="button" className={reaction.mine ? "on" : ""}
                        onClick={() => reactTo(message, reaction.emoji)}>{groupReactions.find(item => item[0] === reaction.emoji)?.[1] || reaction.emoji} {reaction.count}</button>)}
                    </div>}
                    {actions.length > 0 && (
                      <div className="actions">
                        {actions.map(action => (
                          <button key={action} type="button" onClick={() => action === "copy" ? void copyGroupMessage(message) : runHold(action, {
                            reply() { setMenu(null); cancelComposerContext(); setReplyTo(message.messageId); },
                            reaction() { setMenu(null); setReactionFor(message.messageId); },
                            edit() {
                              setMenu(null);
                              const savedDraft = editReturnDraft.current?.key === viewKey ? editReturnDraft.current.value : draft;
                              editReturnDraft.current = { key: viewKey, messageId: message.messageId, value: savedDraft };
                              setReplyTo(null); setEditing(message); setDraft(message.body);
                            },
                            delete() {
                              setMenu(null);
                              if (!chat) return;
                              if (!window.confirm("Удалить сообщение для участников? Восстановить его нельзя.")) return;
                              const key = viewKey;
                              markLogChanged(key);
                              void api.deleteGroupMessage(chat.conversationId, message.messageId)
                                .then(next => { markLogChanged(key); updateLog(key, [next]); })
                                .catch(() => { if (viewKeyRef.current === key) setError("Сообщение не удалилось"); });
                            },
                          })}>{action === "reply" ? "Ответить" : action === "reaction" ? "Реакция" : action === "edit" ? "Изменить" : action === "copy" ? "Копировать текст" : "Удалить"}</button>
                        ))}
                      </div>
                    )}
                    {reactionFor === message.messageId && <div className="react">
                      {groupReactions.map(([code, mark]) => <button key={code} type="button" aria-label={`Реакция ${mark}`}
                        className={message.reactions?.some(item => item.emoji === code && item.mine) ? "on" : ""}
                        onClick={() => reactTo(message, code)}>{mark}</button>)}
                    </div>}
                  </article>
                  </Fragment>
                );
              })}
            </div>
            {!atLatest && log.length > 0 && <button className="btn latest-jump" type="button" onClick={() => {
              const node = logBoxRef.current;
              node?.scrollTo({ top: node.scrollHeight, behavior: "smooth" });
              setAtLatest(true);
            }}>К новым сообщениям</button>}
            <input ref={fileRef} type="file" hidden aria-label="Файл" onChange={event => void onPicked(event)} />
            {chat.kind === "group" && thread !== "list" && !canComposeChannel(thread)
              ? <p className="muted channel-readonly">Писать здесь могут только управляющие разделами.</p>
              : <GroupComposer key={viewKey} draft={draft} editing={!!editing} replyTo={!!replyTo}
              contextText={editing?.body ?? (replyTo ? (repliedMessage ? `${repliedMessage.senderName}: ${repliedMessage.body || "Сообщение"}` : "Сообщение") : "")}
              allowMedia={true}
              onDraft={setDraft} onSubmit={event => void submit(event)} onCancelContext={cancelComposerContext} onChoose={choose}
              onRecorded={(kind, name, blob, durationMs) => sendGroupAttachment(viewKey, chat.conversationId, kind, name, blob, replyTo, selectionEpoch.current, durationMs,
                chat.kind === "group" && thread !== "list" ? thread.topicId ?? undefined : undefined)}
              onError={setError} />}
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
  const chosenGroupName = app.catalog?.groups.find(group => group.id === app.groupId)?.name;
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
      <div className="section-overview">
        <strong>{app.groupId ? chosenGroupName ? `Группа: ${chosenGroupName}` : "Группа выбрана" : "Группа не выбрана"}</strong>
        <span>Оформление: {app.theme === "dark" ? "тёмное" : "светлое"}</span>
      </div>
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
