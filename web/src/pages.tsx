import { TimetableExportTools } from "./timetable-export-view";
import { HomeworkExportTools } from "./homework-export-view";
import { PersonalHomeworkBatch } from "./personal-homework-batch-view";
import { HomeworkPostpone } from "./homework-postpone-view";
import { HomeworkPublication } from "./homework-publication-view";
import { duplicatePersonalHomework } from "./personal-homework-batch";
import { CommonFreeTime } from "./common-free-view";
import { StudyTransferCheck } from "./study-transfer-view";
import { WeekComparison, WeekHomework, HomeworkSubjectOverview } from "./study-overviews-view";
import {AssessmentPlanner,RecentWeekChanges,SubgroupPreview,RoomActivity,SupportDiagnostics,studyOwner} from './study-discovery-view';
import {exactLessonIndex} from './study-discovery';
import {GroupObligations} from './group-obligations-view';
import {GroupObjectFocus} from './group-object-focus';
import {GLOBAL_BALLOTS,type Obligation} from './group-obligations';
import { lessonMapContext, routeClassroom } from "./route-context";
import type { SupportPendingState } from "./update-settings-view";
import { dayLoad, dueBucket, errorHint, noteSearch, summaryRows, chooseBuildingPlan, safeAppReturn } from "./ux300";
import { SearchField, FilterEmpty, useClock, useConnectivity, useBrowseValue, focusElement, SecretInput } from "./ux300-controls";
import { revealQuote } from "./quote-navigation";
import { groupWireText, scalarInput } from "./scalar-input";
import { ChangeEvent, FormEvent, Fragment, ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useDateReveal, useSwipe } from "./swipe";
import * as api from "./api";
import { followGroupCommunity, openGroupFace } from "./groupChoice";
import { clearSentGroupDraft, createGroupPoller, groupMediaSelectionIsCurrent, mergeGroupMessages } from "./groupChat";
import { completeGroupCopy } from "./groupHomework";
import { browseHomework, homeworkEmptyKind, type HomeworkBrowseStatus, type HomeworkTarget } from "./homework-browse";
import { canUndoPersonalHomework, personalHomeworkUndo, type PersonalHomeworkUndo } from "./homework-undo";
import { useHomeworkDraft } from "./homework-draft-context";
import { HomeworkRequestScope, loadScopedHomework, scopedValue } from "./homework-request-scope";
import { HomeworkRecipients, allHomeworkAudience, audienceLabel, useHomeworkAudienceData } from "./homework-audience";
import { canonicalUtc, localDateTimeInput } from "./utc";
import { homeworkSaveError, validateHomeworkDraft } from "./homework-draft";
import { checkHomeworkUpload, runHomeworkSave } from "./homework-save";
import { addDays, dayTitle, isoDay, lessonsOn, sameSubject } from "./parity";
import { forecastIntersections, intersectionPlace, lessonPresenceCaption, markDescription, marksForLesson, presenceForLesson, resolveFriendSchedules, type FriendMark, type FriendPresence } from "./intersections";
import { composeSummary, onWeek, summaryCode, stripType, roomLabel } from "./summary";
import { lessonsOfGroupTeacher, teacherCode, teacherRows, teacherWeek, type TeacherRow } from "./teachers";
import { subgroupIndex, subgroupMark, visibleLessons } from "./subgroups";
import { HOMEWORK_FILE_LIMIT, checkHomeworkFile, compressHomeworkPhoto, deleteHomeworkBlob, putHomeworkBlob, readHomeworkBlob } from "./homework-files";
import { supportAppend, supportDraft, supportFiles, sendSupportDraft } from "./support";
import { holdActions, runHold } from "./hold";
import { groupBubbleText, groupMediaDownload, GroupMediaError, type GroupMediaDownload } from "./group-media";
import { canComposeChannel, canCreateBallot, isChatChannel, nextUnreadTopic, orderedTopics, topicPreview } from "./channels";
import { isNearLatest, matchesBrowseQuery, unreadBadgeDescription, unreadBadgeText } from "./groupBrowse";
import { canCopyMessageText, filterMessages, messageDayKey, sameMessageCluster, type MessageBrowseFilter } from "./messageBrowse";
import { buildGroupChatContext } from "./groupChatContext";
import { advanceDraftEpoch, currentDraftEpoch, attachDiscussionContext, clearSentDiscussionContext, draftEpochMatches, reconcileGroupDrafts, revokeGroupDrafts, useStoredDraft } from "./draft-store";
import { GroupComposer } from "./group-composer";
import { GroupInlineMedia } from "./group-inline-media";
import { legalDocument, type LegalId } from "./legal";
import { registerGroupChatDraft, registerGroupConversation, scopeLease, scopeLeaseValid } from "./draft-revocation";
import { selectedTopicAuthority } from "./topic-authority";
import { topicAction } from "./topic-policy";
import { useCommunityTimetable } from "./use-community-timetable";
import { useApp } from "./store";
import { absoluteDate, freeGaps, gapsBeforeLessons, hasLessonOverlap, heroLesson, isUpcomingLesson, localDay, minuteClock, nearbyHomework, personalHomeworkDue } from "./planner";
import { homeworkCard, lessonFrom, placeCard } from "./cards";
import { BallotBoardView } from "./ballots";
import { GroupTopics, TopicMark } from "./topics";
import { AccountDetails, PasswordRecovery, DataSettings, NotificationSettings, readReminders, StudyExtras, UpdateSettings } from "./settings-panels";
import { SpecializedChannel, SubjectChannelContext } from "./group-panels";
import { GroupAdmin, titlesOf } from "./group-admin";
import { reconcileGroupHomeChat } from "./group-home-refresh";
import { Avatar, AvatarEditor } from "./avatar-view";
import { ShareMenu } from "./share";
import { Icon } from "./icons";
import { MapViewer } from "./map-viewer";
import { CampusRouteView } from "./campus-route-view";
import type { CampusNode, CampusRoute } from "./campus-routing";
import type { PublicMapAsset } from "./types";
import { pairCount } from "./map-viewport";
import { calendarWeek, currentSummarySegment, summaryDayDate, roomPlan, RequestEpoch } from "./ux-navigation";
import { friendGroupChoice } from "./friend-group-choice";
import { overlapPairs, nextTeacherDay, matchesWords, settingsAliases } from "./next-workflows";
import { PersonalHomeworkEditor, type PersonalEditDraft } from "./personal-homework-editor";
import { accountDraft,rememberAccountDraft,legalReturn } from "./account-form-draft";
import { VkMark, YandexMark } from "./brands";
import type { BallotBoard, ChatMessage, Community, Conversation, FriendItem, GroupDesk, GroupHome, GroupHomeworkCopy, GroupTopic, GroupTopicPage, HomeworkAudience, HomeworkFile, HomeworkItem, Lesson, MapPlan, Teacher, TeacherLesson } from "./types";

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

function LessonCard({ lesson, marks = [], presence = [], share, subgroup, upcoming = false, onPick }: { lesson: Lesson; marks?: FriendMark[]; presence?: FriendPresence[]; share?: string | null; subgroup?: ReturnType<typeof subgroupMark>; upcoming?: boolean; onPick?: (streamId: string, optionId: string) => void }) {
  return (
    <article className="lesson">
      <div className="lesson-top">
        <span className="time" aria-label={upcoming ? `Предстоит: ${lesson.timeStart} – ${lesson.timeEnd}` : undefined}>{lesson.timeStart} – {lesson.timeEnd}</span>
        {lesson.typeRaw?.trim() && <TypeChip type={lesson.typeRaw.trim()} />}
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
          {marks.map(mark => <span key={mark.groupName} className={"chip friend-mark" + (mark.present ? "" : " absent") + (upcoming ? " upcoming" : "")}
            style={{ borderLeftColor: mark.color || "var(--line-strong)" }}
            aria-label={`${mark.groupName}${mark.members ? " (" + mark.members + ")" : ""}: ${upcoming ? lessonPresenceCaption(mark.hasLesson) + " · " : ""}${markDescription(mark)}`}>
            {mark.groupName}{mark.members ? ` · ${mark.members}` : ""}{upcoming && <span className="friend-presence"><Icon name="calendar" size={13} />{lessonPresenceCaption(mark.hasLesson)}</span>} · {markDescription(mark)}
          </span>)}
          {upcoming && presence.filter(item => !marks.some(mark => mark.groupName === item.groupName)).map(item => <span key={item.groupName} className="chip friend-mark friend-presence-mark upcoming"
            style={{ borderLeftColor: item.color || "var(--line-strong)" }}
            aria-label={`${item.groupName}${item.members ? " (" + item.members + ")" : ""}: ${lessonPresenceCaption(item.hasLesson)}`}>
            {item.groupName}{item.members ? ` · ${item.members}` : ""}<span className="friend-presence"><Icon name="calendar" size={13} />{lessonPresenceCaption(item.hasLesson)}</span>
          </span>)}
          <ShareMenu card={share ?? null} />
        </div>
      </div>
    </article>
  );
}

export function SchedulePage() {
  const app = useApp();
  const navigate = useNavigate();
  const location = useLocation();
  const online = useConnectivity();
  useEffect(() => { const day = localDay(new URLSearchParams(location.search).get("date") || ""); if (day) app.setDate(day); }, [location.search]);
  const cache = api.readCache();
  const ownTimetable = cache.lessons[app.groupId];
  const period = ownTimetable?.period || app.catalog?.period;
  const choices = app.subgroups[app.groupId] || {};
  const index = useMemo(() => subgroupIndex(app.lessons), [app.lessons]);
  const shown = useMemo(() => visibleLessons(app.lessons, choices), [app.lessons, app.subgroups, app.groupId]);
  const outsidePeriod = !!period && isoDay(app.date) < period.start.slice(0,10);
  const lessons = period && !outsidePeriod ? lessonsOn(shown, app.date, period.start, period.weekCount, app.invert) : [];
  const [now, setNow] = useState(() => new Date());
  const [undo, setUndo] = useState<{ id: string; done: boolean } | null>(null);
  const [calendar, setCalendar] = useState(false);
  const calendarRef = useRef<HTMLInputElement>(null);
  useEffect(() => { const timer = window.setInterval(() => setNow(new Date()), 30_000); return () => window.clearInterval(timer); }, []);
  useEffect(() => { setUndo(null); }, [isoDay(app.date)]);
  useEffect(() => { if (calendar) calendarRef.current?.focus(); }, [calendar]);
  const gaps = gapsBeforeLessons(lessons);
  const overlaps = overlapPairs(lessons);
  const [exactTargetNote,setExactTargetNote]=useState('');
  useEffect(()=>{const query=new URLSearchParams(location.search),key=query.get('rawTarget');setExactTargetNote('');if(!key||app.timetableLoading||query.get('date')!==isoDay(app.date))return;const at=query.get('group')===app.groupId&&app.timetableAvailable?exactLessonIndex(lessons,key):-1;if(at<0){setExactTargetNote('Пара изменилась или недоступна в текущей группе и подгруппе. Похожая пара не выбрана.');return;}const node=document.getElementById(`schedule-lesson-${at}`);node?.focus();node?.scrollIntoView({block:'center'});},[location.search,isoDay(app.date),app.groupId,app.subgroups,app.lessons,app.timetableLoading,app.timetableAvailable]);
  useEffect(()=>{const query=new URLSearchParams(location.search),time=query.get("time"),subject=query.get("subject");if(!time||!subject||!app.timetableAvailable)return;const at=lessons.findIndex(row=>row.timeStart===time&&sameSubject(row.subjectRaw,subject));if(at<0)return;const node=document.getElementById(`schedule-lesson-${at}`);node?.focus();node?.scrollIntoView({block:"center"});},[location.search,isoDay(app.date),app.timetableAvailable,app.lessons]);
  const hero = heroLesson(lessons, app.date, now);
  const friendSchedules = resolveFriendSchedules(app.friends, app.catalog?.groups || [], cache.lessons, ownTimetable);
  const groupName = app.catalog?.groups.find(group => group.id === app.groupId)?.name || "";
  const [sharedHomework, setSharedHomework] = useState<GroupHomeworkCopy[]>([]);
  const [sharedHomeworkOwner, setSharedHomeworkOwner] = useState("");
  const [homeworkCommunity, setHomeworkCommunity] = useState("");
  const [sharedUndo,setSharedUndo]=useState<{id:string;done:boolean}|null>(null);
  const [sharedBusy,setSharedBusy]=useState(false);
  const [deadlineError,setDeadlineError]=useState("");
  const [deadlineRetry, setDeadlineRetry] = useState(0);
  const sharedScopeKey = JSON.stringify([!!app.session?.authenticated, app.session?.user?.userId, app.groupId, homeworkCommunity]);
  const sharedRequests = useRef(new HomeworkRequestScope()).current;
  sharedRequests.scope(sharedScopeKey);
  const sharedOwnerKey = JSON.stringify([!!app.session?.authenticated, app.session?.user?.userId, app.groupId]);
  const sharedOwnerRef = useRef(sharedOwnerKey);
  sharedOwnerRef.current = sharedOwnerKey;
  useEffect(() => { setSharedBusy(false); setSharedUndo(null); setDeadlineError(""); }, [sharedScopeKey]);
  useEffect(()=>{setSharedUndo(null);},[isoDay(app.date)]);
  useEffect(() => { let stop = false; const owner = sharedOwnerKey; const current = () => !stop && sharedOwnerRef.current === owner; if(sharedHomeworkOwner!==owner){setSharedHomework([]);setSharedHomeworkOwner(owner);setHomeworkCommunity("");}
    if (app.session?.authenticated && app.groupId) void api.communities(app.groupId).then(rows => { const group=rows.find(row=>row.role); if(!current())return;if(!group){setSharedHomework([]);setHomeworkCommunity("");return;} setHomeworkCommunity(group.communityId); return api.groupHomework(group.communityId).then(items=>{if(current()){setSharedHomeworkOwner(owner);setSharedHomework(items);setDeadlineError("");}}); }).catch(()=>{if(current())setDeadlineError("Общая домашка не обновилась. Личные сроки остаются доступны.");});
    return()=>{stop=true;};
  }, [app.groupId,app.session?.user?.userId,deadlineRetry]);
  const personal = app.homework.map(item=>({...item,deadlineAt: period ? personalHomeworkDue(item,shown,period,app.invert)?.toISOString() ?? null : null}));
  const deadlines = nearbyHomework(personal, app.date, lessons.map(lesson => lesson.subjectRaw));
  const visibleSharedHomework = scopedValue(sharedHomework, sharedHomeworkOwner, sharedOwnerKey, []);
  const sharedDeadlines = nearbyHomework(visibleSharedHomework.map(item=>({id:item.homeworkId,subject:item.title,text:item.body,done:item.completed,created:"",deadlineAt:item.deadlineAt})),app.date,lessons.map(lesson=>lesson.subjectRaw));
  async function changeSharedCompletion(copy: GroupHomeworkCopy, completed: boolean, undoAction = false) {
    if (!homeworkCommunity || sharedBusy || copy.canComplete === false) return;
    const action = sharedRequests.begin();
    const community = homeworkCommunity;
    setSharedBusy(true);
    setDeadlineError("");
    try {
      const value = await api.completeHomework(community, copy.homeworkId, completed, copy.completionRevision);
      if (!sharedRequests.active(action)) return;
      if (undoAction) setSharedUndo(null);
      else { setUndo(null); setSharedUndo({ id: copy.homeworkId, done: copy.completed }); }
      setSharedHomework(rows => rows.map(row => row.homeworkId === copy.homeworkId ? { ...row, completed: value.completed, completionRevision: value.revision } : row));
    } catch (error) {
      if (!sharedRequests.active(action)) return;
      setDeadlineError(undoAction ? "Отмена не сохранилась" : "Личная готовность не сохранилась. Повторите после обновления домашки.");
      if (error instanceof Error && error.message === "409")
        await loadScopedHomework(sharedRequests, action, () => api.groupHomework(community), rows => { if (sharedRequests.active(action)) setSharedHomework(rows); });
    } finally { if (sharedRequests.active(action)) setSharedBusy(false); }
  }
  const strip = Array.from({ length: 7 }, (_, i) => addDays(app.date, i - 2));
  const swipe = useSwipe(() => app.setDate(addDays(app.date, 1)), () => app.setDate(addDays(app.date, -1)), isoDay(app.date));
  const dateReveal = useDateReveal(isoDay(app.date));
  const nextDate = app.timetableAvailable && period ? Array.from({ length: 21 }, (_, i) => addDays(app.date, i + 1)).find(date => lessonsOn(shown, date, period.start, period.weekCount, app.invert).length > 0) : null;
  function actions(lesson: Lesson) {
    const context = new URLSearchParams({ subject: lesson.subjectRaw, date: isoDay(app.date), time: lesson.timeStart });
    return <div className="row lesson-actions">
      <button className="btn primary" type="button" disabled={!lesson.roomRaw && !lesson.classroomRaw || /дистанц|онлайн/i.test(`${lesson.roomRaw||""} ${lesson.classroomRaw||""}`)} title={!lesson.roomRaw && !lesson.classroomRaw ? "Аудитория не указана" : undefined}
        onClick={() => navigate(`/maps?${context}&${lessonMapContext(lesson)}`)}>Открыть карту</button>
      <Link className="btn" to={`/homework?${context}`}>Домашка</Link>
      <Link className="btn quiet" to={`/group?${context}`}>Обсудить</Link>
    </div>;
  }
  function renderLesson(lesson: Lesson, lessonIndex: number) {
    const gap = gaps.get(lessonIndex);
    const upcoming = isUpcomingLesson(lesson, app.date, now);
    const intersectionInput = period ? { mineLessons: shown, friends: friendSchedules, period, invert: app.invert, strictness: app.intersectionStrictness, now } : null;
    const marks = intersectionInput ? marksForLesson(lesson, app.date, intersectionInput, app.showAbsentFriends) : [];
    const presence = upcoming && intersectionInput ? presenceForLesson(lesson, app.date, intersectionInput) : [];
    return <Fragment key={`${lesson.index}:${lesson.timeStart}:${lesson.subjectRaw}:${lesson.teacherRaw}:${lessonIndex}`}>
      {gap && <p className="free-gap"><span>Перерыв {minuteClock(gap.start)}–{minuteClock(gap.end)}</span><span className="free-gap-duration">{Math.floor(gap.duration / 60) ? `${Math.floor(gap.duration / 60)} ч ` : ""}{gap.duration % 60 ? `${gap.duration % 60} мин` : ""}</span></p>}
      <div className={hero === lesson ? "day-hero" : "day-row"} id={`schedule-lesson-${lessonIndex}`} tabIndex={-1}>
        {hero === lesson && <p className="muted">{isoDay(app.date) > isoDay(now) ? "Первая пара" : lesson.timeStart <= minuteClock(now.getHours() * 60 + now.getMinutes()) ? "Сейчас" : "Следующая пара"}</p>}
        <LessonCard lesson={lesson} marks={marks} presence={presence} upcoming={upcoming} share={lessonFrom(groupName, app.date, lesson)} subgroup={subgroupMark(lesson, lessons, index, choices)} onPick={app.pickSubgroup} />
        {actions(lesson)}
      </div>
    </Fragment>;
  }
  return <section className="page day-page">
    {exactTargetNote&&<p className="banner" role="status">{exactTargetNote}</p>}
    {app.canUndoSubgroup&&<div className="banner row" role="status"><span>Выбор подгруппы изменён</span><button className="btn" type="button" onClick={app.undoSubgroup}>Отменить выбор подгруппы</button></div>}
    {overlaps.length>0&&<details className="banner"><summary>Совпадает время занятий · {overlaps.length}</summary>{overlaps.map(({left,right})=><div className="row" key={`${left}:${right}`}><span>{lessons[left].subjectRaw} {lessons[left].timeStart}–{lessons[left].timeEnd} · {lessons[right].subjectRaw} {lessons[right].timeStart}–{lessons[right].timeEnd}</span>{[left,right].map(index=><button key={index} className="btn quiet" type="button" onClick={()=>{const node=document.getElementById(`schedule-lesson-${index}`);node?.focus();node?.scrollIntoView({block:"center"});}}>К {lessons[index].timeStart}</button>)}</div>)}</details>}
    <Head title={isoDay(app.date) === isoDay(addDays(now, 2)) ? "Послезавтра" : dayTitle(app.date, now)} text={absoluteDate(app.date)}>
      <button className="icon-btn quiet" type="button" title="Предыдущий день" aria-label="Предыдущий день" onClick={() => app.setDate(addDays(app.date, -1))}><Icon name="left" /></button>
      <button className="btn quiet" type="button" aria-expanded={calendar} onClick={() => setCalendar(value => !value)}><Icon name="calendar" />Календарь</button>
      <button className="icon-btn quiet" type="button" title="Следующий день" aria-label="Следующий день" onClick={() => app.setDate(addDays(app.date, 1))}><Icon name="right" /></button>
      <Link className="btn" to="/week">Неделя</Link>
      {hero && <button className="btn quiet" onClick={() => focusElement(`schedule-lesson-${lessons.indexOf(hero)}`)}>К ближайшей паре</button>}
      <button className="btn quiet" onClick={() => focusElement("schedule-deadlines")}>К срокам домашки</button>
    </Head>
    {app.timetableAvailable&&<TimetableExportTools days={[{date:app.date,lessons,known:!outsidePeriod}]} groupId={app.groupId} groupName={groupName}/>}
    <div className="seg quick-days" aria-label="Быстрый выбор дня">{["Сегодня", "Завтра", "Послезавтра"].map((title, i) => <button type="button" key={title} aria-pressed={isoDay(app.date) === isoDay(addDays(now, i))} className={isoDay(app.date) === isoDay(addDays(now, i)) ? "active" : ""} onClick={() => app.setDate(addDays(now, i))}>{title}</button>)}</div>
    {calendar && <label className="field day-calendar">Выберите дату<input ref={calendarRef} type="date" value={isoDay(app.date)} onChange={event => { const value = localDay(event.target.value); if (value) app.setDate(value); }} /></label>}
    <div className="dates day-strip">{strip.map(date => {
      const count = app.timetableAvailable && period && isoDay(date) >= period.start.slice(0,10) ? lessonsOn(shown, date, period.start, period.weekCount, app.invert).length : null;
      return <button className={"date" + (isoDay(date) === isoDay(app.date) ? " active" : "")} key={isoDay(date)} type="button" aria-pressed={isoDay(date) === isoDay(app.date)} aria-label={`${absoluteDate(date)}, ${count === null ? "нет данных" : pairCount(count)}`} onClick={() => app.setDate(date)}><span>{date.toLocaleDateString("ru-RU", { weekday: "short" })}</span><strong>{date.getDate()}</strong><small>{count === null ? "Нет данных" : count ? pairCount(count) : "Без пар"}</small></button>;
    })}</div>
    <div className="day-space date-reveal" ref={dateReveal}>
      <main className="stack swipe" {...swipe}>
        <div className="section-overview"><strong>{groupName || "Группа не выбрана"}</strong><span>{app.timetableAvailable && !outsidePeriod ? `${pairCount(lessons.length)}${lessons.length ? ` · ${lessons[0].timeStart}–${lessons.at(-1)?.timeEnd}` : ""}` : "Нет данных"}</span>
          <button className="btn quiet" type="button" disabled={app.loading || app.timetableLoading} onClick={app.refresh}>{app.loading || app.timetableLoading ? "Обновляем…" : "Обновить"}</button></div>
        {app.timetableAvailable && !outsidePeriod && <p className="muted">Учебное время: {dayLoad(lessons).minutes} мин · Окна между парами: {dayLoad(lessons).gaps} мин</p>}
        {app.timetableAvailable&&!outsidePeriod&&<StudyTransferCheck lessons={lessons}/>}
        {ownTimetable && <p className="schedule-cache muted">{!online ? "Нет сети · сохранённая копия" : app.timetableFailed ? "Не удалось обновить · сохранённая копия" : "Обновлено"} {new Date(ownTimetable.meta.fetchedAt).toLocaleString("ru-RU", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}</p>}
        {hasLessonOverlap(lessons) && <p className="banner">Записи пар пересекаются по времени. Проверьте выбранные подгруппы.</p>}
        {!app.timetableAvailable ? <div className="card empty"><p>{app.timetableLoading ? "Загружаем расписание" : app.groupId ? "Расписание не загружено. Нет сохранённой копии." : "Выберите учебную группу"}</p>{app.groupId ? <button className="btn" type="button" disabled={app.timetableLoading || app.loading} onClick={app.refresh}>{app.timetableLoading || app.loading ? "Загружаем…" : "Повторить загрузку"}</button> : <Link className="btn" to="/settings?section=study">Выбрать группу</Link>}</div>
          : outsidePeriod ? <div className="card empty day-empty"><Icon name="calendar" size={32} /><h2>Дата вне учебного периода</h2><p>Начало сохранённого учебного периода: {period && localDay(period.start.slice(0,10)) ? absoluteDate(localDay(period.start.slice(0,10))!) : "неизвестно"}</p></div> : lessons.length === 0 ? <div className="card empty day-empty"><Icon name="calendar" size={32} /><h2>В этот день пар нет</h2><p>{nextDate ? `Ближайшие занятия — ${absoluteDate(nextDate)}.` : "В ближайшие три недели в сохранённом расписании занятий нет."}</p>{nextDate && <button className="btn" type="button" onClick={() => app.setDate(nextDate)}>Открыть {nextDate.toLocaleDateString("ru-RU", { day: "numeric", month: "long" })}<Icon name="right" /></button>}</div>
          : <>{isoDay(app.date) === isoDay(now) && !hero && <p className="banner">Пары закончились</p>}{lessons.map(renderLesson)}</>}
      </main>
      <aside id="schedule-deadlines" tabIndex={-1} className="stack day-context"><h2>Ближайшие сроки · {deadlines.length + sharedDeadlines.length}</h2><p className="muted">{app.date.toLocaleDateString("ru-RU")}–{addDays(app.date, 2).toLocaleDateString("ru-RU")}</p>
        {deadlines.length + sharedDeadlines.length === 0 && <p className="muted">На выбранные дни заданий нет</p>}
        {deadlines.map(item => <article className="deadline" key={item.id}><label className="check"><input type="checkbox" checked={item.done} aria-label={`Готово у меня: ${item.text}`} onChange={() => { const saved=app.homework.find(row=>row.id===item.id);if(!saved)return;setSharedUndo(null); setUndo({ id: saved.id, done: saved.done }); app.saveHomework({ ...saved, done: !saved.done }); }} /><Link to={`/homework?id=${item.id}&subject=${encodeURIComponent(item.subject)}`}><span className={item.done ? "done-title" : ""}>{item.text}</span><small>{item.subject}</small></Link></label><p className="muted">{item.done ? "Готово у меня · " : ""}{item.deadlineAt ? new Date(item.deadlineAt).toLocaleString("ru-RU") : "Без срока"}{!item.done && item.deadlineAt && dueBucket(item.deadlineAt,now,"date")==="overdue" ? " · Просрочено" : ""}</p></article>)}
        {sharedDeadlines.map(item => { const copy = visibleSharedHomework.find(row => row.homeworkId === item.id); return <article className="deadline" key={item.id}><Link to={`/homework?sharedId=${encodeURIComponent(item.id)}&subject=${encodeURIComponent(item.subject)}`}><b>{item.text}</b></Link><p className="muted">{audienceLabel(copy?.audience)} · {item.subject} · {item.deadlineAt ? new Date(item.deadlineAt).toLocaleString("ru-RU") : "Без срока"}</p>{copy?.canComplete !== false && <label className="check"><input type="checkbox" aria-label={`Готово у меня: ${item.text}`} checked={item.done} disabled={sharedBusy} onChange={() => { if (copy) void changeSharedCompletion(copy, !copy.completed); }} />Готово у меня</label>}</article>; })}
        {deadlineError && <div className="banner" role="alert">{deadlineError}<button className="btn" onClick={() => setDeadlineRetry(value=>value+1)}>Обновить общую домашку</button></div>}
        {sharedUndo && <div className="banner row" role="status"><span>{sharedUndo.done ? "Отметка снята" : "Отмечено готово"}</span><button className="btn quiet" type="button" disabled={sharedBusy} onClick={()=>{ const item=visibleSharedHomework.find(row=>row.homeworkId===sharedUndo.id); if (item) void changeSharedCompletion(item, sharedUndo.done, true); }}>Отменить</button></div>}
        {undo && <div className="banner row" role="status"><span>{!undo.done ? "Отмечено готово" : "Отметка снята"}</span><button className="btn quiet" type="button" onClick={() => { const item = app.homework.find(row => row.id === undo.id); if (item) app.saveHomework({ ...item, done: undo.done }); setUndo(null); }}>Отменить</button></div>}
        <Link className="btn" to="/homework">Вся домашка</Link>
        <Link className="btn quiet" to={`/group?date=${isoDay(app.date)}`}>Обсуждение группы</Link>
      </aside>
    </div>
  </section>;
}

export function WeekPage() {
  const app = useApp();
  const navigate = useNavigate();
  const [query, setQuery] = useBrowseValue("week-query","");
  const [hideEmpty, setHideEmpty] = useBrowseValue("week-hide-empty",false);
  const period = api.readCache().lessons[app.groupId]?.period || app.catalog?.period;
  const shown = useMemo(() => visibleLessons(app.lessons, app.subgroups[app.groupId] || {}), [app.lessons, app.subgroups, app.groupId]);
  const days = calendarWeek(app.date);
  const monday = days[0];
  const weekDays = days.map(date => ({ date, known: !!period && isoDay(date)>=period.start.slice(0,10), lessons: period && isoDay(date)>=period.start.slice(0,10) ? lessonsOn(shown, date, period.start, period.weekCount, app.invert) : [] }));
  const weekTotal = weekDays.reduce((total, day) => total + day.lessons.length, 0);
  const visibleDays = weekDays.map(day=>({...day, matches:day.lessons.filter(lesson=>noteSearch(query,lesson.subjectRaw,lesson.teacherRaw||"",lesson.roomRaw||"",lesson.classroomRaw||"",lesson.typeRaw||""))})).filter(day=>(!hideEmpty || !day.known || day.lessons.length>0) && (!query.trim() || day.matches.length>0));
  const swipe = useSwipe(() => app.setDate(addDays(app.date, 7)), () => app.setDate(addDays(app.date, -7)), isoDay(app.date));
  const dateReveal = useDateReveal(isoDay(monday));
  return (
    <section className="page">
      <Head title="Неделя" text={period?.title}>
        <button className="icon-btn" type="button" aria-label="Предыдущая неделя" onClick={() => app.setDate(addDays(app.date, -7))}><Icon name="left" /></button>
        <button className="btn" type="button" onClick={() => app.setDate(new Date())}>Сегодня</button>
        <button className="icon-btn" type="button" aria-label="Следующая неделя" onClick={() => app.setDate(addDays(app.date, 7))}><Icon name="right" /></button>
      </Head>
      <TimetableState />
      <SearchField label="Найти пару на этой неделе" value={query} onChange={setQuery}/><label className="check"><input type="checkbox" checked={hideEmpty} onChange={event=>setHideEmpty(event.target.checked)}/>Скрыть дни без пар</label><div className="row"><label className="field">Неделя по дате<input type="date" value={isoDay(app.date)} onChange={event=>{const date=localDay(event.target.value);if(date)app.setDate(date);}}/></label>{weekDays.some(day=>day.lessons.length>0)&&<button className="btn" onClick={()=>{const first=weekDays.find(day=>day.lessons.length>0)!;app.setDate(first.date);navigate(`/schedule?date=${isoDay(first.date)}`);}}>Первый учебный день</button>}</div>
      {app.timetableAvailable && visibleDays.length===0 && <FilterEmpty onReset={()=>{setQuery("");setHideEmpty(false);}}/>}
      {app.timetableAvailable && period && <div className="section-overview">
        <strong>{weekDays.some(day=>!day.known)?"Известных пар за неделю":"Пар за неделю"}: {weekTotal}</strong>
        <span>{monday.toLocaleDateString("ru-RU", { day: "numeric", month: "short" })} — {days[6].toLocaleDateString("ru-RU", { day: "numeric", month: "short" })}</span>
      </div>}
      {app.timetableAvailable&&<TimetableExportTools days={weekDays} groupId={app.groupId} groupName={app.catalog?.groups.find(group=>group.id===app.groupId)?.name||app.groupId}/>}
      <details className="card week-planning-tools"><summary>Планирование недели</summary><div className="stack">
      <WeekComparison lessons={shown} date={app.date} period={period} invert={app.invert} available={app.timetableAvailable}/>
      <SubgroupPreview key={'week-subgroups'+studyOwner(app)}/>
      <AssessmentPlanner key={'assessments'+studyOwner(app)}/>
      <RecentWeekChanges key={'changes'+studyOwner(app)+isoDay(monday)}/>
      {!app.privateHomework.readFailed?<WeekHomework items={app.homework} days={days} dateOf={item=>{const due=app.timetableAvailable&&period?personalHomeworkDue(item,shown,period,app.invert):null;return due?isoDay(due):null;}}/>:<p role="status">Личные сроки недоступны: сохранённые задания не удалось прочитать.</p>}
      </div></details>
      <p className="swipe-hint">Смахните, чтобы сменить неделю</p>
      <div className="week swipe date-reveal" {...swipe} ref={node => { swipe.ref(node); dateReveal.current = node; }}>
        {visibleDays.map(({ date, matches: dayLessons, lessons: originalLessons, known }) => (
          <article className="card" key={isoDay(date)} aria-current={isoDay(date) === isoDay(new Date()) ? "date" : undefined}>
            <h2 className="week-day-head">{dayTitle(date)} <span className="muted">{date.getDate()}</span>{app.timetableAvailable && <span className="chip">{known?`Пар: ${dayLessons.length}`:"Нет данных"}</span>}</h2>
            {isoDay(date) === isoDay(new Date()) && <span className="chip">Сегодня</span>}
            <button className="btn" type="button" onClick={() => { app.setDate(date); navigate(`/schedule?date=${isoDay(date)}`); }}>Открыть день</button>
            {known&&<p className="muted">Учебное время: {dayLoad(originalLessons).minutes} мин</p>}{freeGaps(originalLessons).map(gap=><p className="free-gap" key={gap.start}>Окно {minuteClock(gap.start)}–{minuteClock(gap.end)} · {gap.duration} мин</p>)}<div className="stack">
              {dayLessons.map(lesson => (
                <div key={lesson.timeStart + lesson.subjectRaw + (lesson.teacherRaw || "")}><b>{lesson.timeStart}–{lesson.timeEnd}</b> {lesson.subjectRaw}<div className="muted">{roomLabel(lesson)} · {lesson.teacherRaw}</div><div className="row"><Link className="btn quiet" to={`/schedule?date=${isoDay(date)}&time=${lesson.timeStart}&subject=${encodeURIComponent(lesson.subjectRaw)}`} onClick={()=>app.setDate(date)}>Открыть пару</Link><Link className="btn quiet" to={`/homework?date=${isoDay(date)}&subject=${encodeURIComponent(lesson.subjectRaw)}`}>Домашка</Link>{lesson.classroomRaw && <Link className="btn quiet" to={`/maps?date=${isoDay(date)}&time=${lesson.timeStart}&subject=${encodeURIComponent(lesson.subjectRaw)}&${lessonMapContext(lesson)}`}>Карта</Link>}</div></div>
              ))}
              {app.timetableAvailable && period && dayLessons.length === 0 && <span className="muted">{known ? "Нет пар" : "Дата вне известного периода"}</span>}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

const summarySegments = ["Нечётная", "Чётная", "Обе"];

function TimetableState() {
  const app = useApp();
  if (!app.groupId) return <div className="card empty"><p>Выберите учебную группу.</p><Link className="btn primary" to="/settings?section=study">Выбрать группу</Link></div>;
  if (app.timetableLoading) return <p className="muted" role="status">{app.timetableAvailable ? "Обновляем расписание. Сохранённая копия остаётся доступна." : "Загружаем расписание…"}</p>;
  if (!app.timetableAvailable || app.timetableFailed) return <div className="banner row" role="status"><span>{app.timetableAvailable ? "Расписание не обновилось. Показана сохранённая копия." : "Расписание этой группы ещё не загружено. Это не означает, что занятий нет."}</span><button className="btn" type="button" onClick={app.refresh}>Повторить загрузку</button></div>;
  return null;
}

export function SummaryPage() {
  const app = useApp();
  const navigate = useNavigate();
  const period = api.readCache().lessons[app.groupId]?.period || app.catalog?.period;
  const [chosenSegment, setSegment] = useState<number | null>(null);
  const [detail, setDetail] = useState<{title:string;rows:Lesson[]} | null>(null);
  const segment = chosenSegment ?? (period ? currentSummarySegment(new Date(), period) : 0);
  const summary = useMemo(
    () => composeSummary(app.lessons, app.subgroups[app.groupId] || {}, segment, app.invert),
    [app.lessons, app.subgroups, app.groupId, segment, app.invert],
  );
  const source = visibleLessons(app.lessons,app.subgroups[app.groupId]||{}).filter(lesson=>onWeek(lesson.parity,summaryCode(segment,app.invert)));
  function inspect(title:string, matches:(lesson:Lesson)=>boolean) { setDetail({title,rows:source.filter(matches)}); }
  function openLesson(lesson:Lesson) {
    if(!period)return;
    const date=Array.from({length:56},(_,i)=>addDays(new Date(),i)).find(date=>lessonsOn([lesson],date,period.start,period.weekCount,app.invert).length>0);
    if(date){app.setDate(date);navigate(`/schedule?date=${isoDay(date)}&time=${lesson.timeStart}&subject=${encodeURIComponent(lesson.subjectRaw)}`);}
  }
  useEffect(()=>setDetail(null),[app.groupId,segment]);
  return (
    <section className="page summary-page">
      <Head title="Сводка" text={app.groupId ? "Сколько пар в выбранной группе" : "Группа не выбрана"} />
      {app.groupId && <TimetableState />}
      {!app.groupId ? (
        <div className="card empty">
          <p>Выберите группу, чтобы открыть расписание</p>
          <Link className="btn primary" to="/settings">Выбрать группу</Link>
        </div>
      ) : app.timetableAvailable ? (
        <div className="stack">
          <div className="seg" role="tablist" aria-label="Неделя сводки">
            {summarySegments.map((label, index) => (
              <button key={label} className={segment === index ? "active" : ""} type="button" aria-pressed={segment === index} onClick={() => setSegment(index)}>{label}</button>
            ))}
          </div>
          <article className="card summary-hero">
            <div className="muted">Всего занятий</div>
            <b className="summary-total">{summary.total}</b><span className="muted">{summarySegments[segment]} · выбранная группа</span>
          </article>
          <CountCard title="По дням" rows={summary.byDay} onPick={index => { if (!period) return; const date = summaryDayDate(new Date(), period, segment, app.invert, index + 1); app.setDate(date); navigate(`/schedule?date=${isoDay(date)}`); }} />
          <CountCard title="По типам" rows={summary.byType} />
          <CountCard title="По предметам" rows={summary.bySubject} onSelect={name=>inspect(name,lesson=>stripType(lesson.subjectRaw,lesson.typeRaw).toLocaleLowerCase("ru")===name.toLocaleLowerCase("ru"))}/>
          <CountCard title="По преподавателям" rows={summary.byTeacher} onSelect={name=>inspect(name,lesson=>(lesson.teacherRaw||"").split(";").some(part=>part.trim().toLocaleLowerCase("ru")===name.toLocaleLowerCase("ru")))}/>
          <CountCard title="По аудиториям" rows={summary.byRoom} empty="Аудитории не указаны" onSelect={name=>inspect(name,lesson=>roomLabel(lesson).toLocaleLowerCase("ru")===name.toLocaleLowerCase("ru"))}/>
          {detail && <div className="card stack"><h2>{detail.title} · занятий: {detail.rows.length}</h2><button className="btn quiet" onClick={()=>setDetail(null)}>Закрыть подробности</button>{detail.rows.map((lesson,index)=><div className="row" key={index}><span>{["","Пн","Вт","Ср","Чт","Пт","Сб","Вс"][lesson.dayOfWeek]} {lesson.timeStart}–{lesson.timeEnd} · {lesson.subjectRaw} · {roomLabel(lesson)}</span><button className="btn" onClick={()=>openLesson(lesson)}>Открыть ближайшую пару</button></div>)}</div>}
        </div>
      ) : null}
    </section>
  );
}

function CountCard({ title, rows, empty, onPick, onSelect }: { title: string; rows: { name: string; count: number }[]; empty?: string; onPick?: (index: number) => void; onSelect?: (name:string)=>void }) {
  const largest = Math.max(1, ...rows.map(row => row.count));
  const [query,setQuery]=useState("");
  const [order,setOrder]=useState<"original"|"name"|"count">("original");
  const [expanded,setExpanded]=useState(false);
  const days=title==="По дням";
  const filtered=summaryRows(rows,query,order);
  const visible=expanded||days||query.trim()?filtered:filtered.slice(0,5);
  return (
    <article className="card stack summary-count-card">
      <h2>{title}</h2>
      {!days && rows.length>1 && <><SearchField label={`Поиск: ${title.toLocaleLowerCase("ru")}`} value={query} onChange={setQuery}/><label className="field">Порядок<select value={order} onChange={event=>setOrder(event.target.value as typeof order)}><option value="original">Как в сводке</option><option value="name">По названию</option><option value="count">По количеству</option></select></label></>}
      {rows.length === 0 && <p className="muted">{empty ?? "В выбранном расписании занятий нет."}</p>}
      {visible.map(row => (
        <div className="summary-count-row" key={title + row.name}>
          <div className="count"><span>{row.name}</span><b>{row.count}</b></div>
          {onPick && <button className="btn quiet" type="button" onClick={() => onPick(rows.indexOf(row))}>Открыть {row.name.toLocaleLowerCase("ru")}</button>}
          {onSelect && <button className="btn quiet" onClick={()=>onSelect(row.name)}>Показать занятия</button>}
          <div className="summary-count-track" aria-hidden="true"><span style={{ width: `${row.count / largest * 100}%` }} /></div>
        </div>
      ))}
      {query && filtered.length===0 && <FilterEmpty onReset={()=>setQuery("")}/>}
      {!days && !query && rows.length>5 && <button className="btn quiet" aria-expanded={expanded} onClick={()=>setExpanded(value=>!value)}>{expanded?"Свернуть":`Показать все · ${rows.length}`}</button>}
    </article>
  );
}

const teacherFilters = ["Обе", "Нечётная", "Чётная"];

export function TeachersPage() {
  const app = useApp();
  return <TeachersContent key={JSON.stringify([app.session?.user?.userId, app.session?.familyId, app.groupId])} />;
}

function TeachersContent() {
  const app = useApp();
  const location = useLocation();
  const teacherAlive=useRef(true);
  useEffect(()=>{teacherAlive.current=true;return()=>{teacherAlive.current=false;};},[]);
  const navigate=useNavigate();
  const teacherPeriod=api.readCache().lessons[app.groupId]?.period||app.catalog?.period;
  const [query, setQuery] = useBrowseValue("teacher-query","");
  const [onlyMine, setOnlyMine] = useBrowseValue("teacher-mine",true);
  const [catalog, setCatalog] = useState<Teacher[]>([]);
  const [selected, setSelected] = useState<TeacherRow | null>(null);
  const [lessons, setLessons] = useState<TeacherLesson[]>([]);
  const [filter, setFilter] = useBrowseValue("teacher-week",0);
  const [myLessonsOnly, setMyLessonsOnly] = useState(false);
  const [department, setDepartment] = useBrowseValue("teacher-department","");
  const [collapsedDays,setCollapsedDays]=useState<number[]>([]);
  const [error, setError] = useState("");
  const [catalogLoading, setCatalogLoading] = useState(true);
  const [catalogRetry, setCatalogRetry] = useState(0);
  const [detailError, setDetailError] = useState("");
  const [detailLoading, setDetailLoading] = useState(false);
  const requests = useRef(new RequestEpoch()).current;
  const requestedTeacher = useRef<string|null>(null);
  const groupName = app.catalog?.groups.find(group => group.id === app.groupId)?.name || "";
  useEffect(() => { let stopped = false; setCatalogLoading(true); setError("");
    api.loadTeachers().then(data => { if (!stopped) setCatalog(data.lecturers || []); })
      .catch(() => { if (!stopped) setError("Каталог не обновился. Доступны ранее загруженные преподаватели и пары вашей группы."); })
      .finally(() => { if (!stopped) setCatalogLoading(false); }); return () => { stopped = true; };
  }, [catalogRetry]);
  useEffect(() => () => requests.invalidate(), [requests]);
  const attended = useMemo(
    () => visibleLessons(app.lessons, app.subgroups[app.groupId] || {}),
    [app.lessons, app.subgroups, app.groupId],
  );
  const found = useMemo(() => { const result=teacherRows(catalog, attended, query, onlyMine); return {...result,rows:department?result.rows.filter(row=>catalog.find(teacher=>teacher.id===row.id)?.kafedra===department):result.rows}; }, [catalog, attended, query, onlyMine, department]);
  const week = useMemo(
    () => teacherWeek(lessons, teacherCode(filter, app.invert), app.groupId, groupName, app.invert).map(day=>({...day,rows:day.rows.filter(row=>!myLessonsOnly || row.mine)})).filter(day=>day.rows.length>0),
    [lessons, filter, app.invert, app.groupId, groupName, myLessonsOnly],
  );
  const teacherWeekTotal = week.reduce((total, day) => total + day.rows.length, 0);
  useEffect(()=>{
    const id=new URLSearchParams(location.search).get("teacher");
    if(!id){requests.invalidate();requestedTeacher.current=null;setSelected(null);setLessons([]);setDetailLoading(false);setDetailError("");return;}
    if(requestedTeacher.current===id)return;
    const row=teacherRows(catalog,attended,"",false).rows.find(row=>row.id===id);
    if(row){void open(row,false);return;}
    requests.invalidate();requestedTeacher.current=null;setSelected(null);setLessons([]);setDetailLoading(false);
    setDetailError(catalogLoading?"":"Преподаватель по ссылке не найден. Выберите его в списке или повторите загрузку каталога.");
  },[location.search,catalog,attended,catalogLoading]);
  async function open(row: TeacherRow, route = true) {
    if(route && new URLSearchParams(location.search).get("teacher")!==row.id){const params=new URLSearchParams(location.search);params.set("teacher",row.id);navigate({pathname:location.pathname,search:params.toString()});return;}
    requestedTeacher.current=row.id;
    const current = requests.begin(row.id);
    const sameTeacher = selected?.id === row.id;
    setSelected(row);
    setDetailError("");
    const own = lessonsOfGroupTeacher(attended, row.name, app.groupId, groupName);
    if (!sameTeacher) setLessons(own);
    if (row.id.startsWith("group:")) { setDetailLoading(false); setDetailError("Преподаватель найден в расписании вашей группы. Полный каталог пока недоступен."); return; }
    setDetailLoading(true);
    try { const result = await api.loadTeacher(row.id); if (current()) setLessons(result.lessons || own); }
    catch { if (current()) setDetailError("Полное расписание не обновилось. Показана локальная копия; она может содержать только пары вашей группы."); }
    finally { if (current()) setDetailLoading(false); }
  }
  return (
    <section className="page teachers-page">
      <Head title="Преподаватели" text={selected ? selected.name : error || "Поиск по фамилии или предмету"} />
      {!selected&&detailError&&<p className="banner" role="status">{detailError}</p>}
      {selected ? (
        <div className="stack">
          <button className="btn teacher-back" type="button" onClick={() => { requests.invalidate(); setSelected(null); setDetailLoading(false); const params=new URLSearchParams(location.search);params.delete("teacher");navigate({pathname:location.pathname,search:params.toString()},{replace:true}); }}>Назад</button>
          {detailLoading && <p className="muted" role="status">Загружаем расписание преподавателя…</p>}
          {detailError && <div className="banner row" role="status"><span>{detailError}</span>{!selected.id.startsWith("group:") && <button className="btn" type="button" disabled={detailLoading} onClick={() => void open(selected)}>Повторить</button>}</div>}
          <div className="card teacher-profile"><span className="muted">Преподаватель</span><h2>{selected.name}</h2><p className="muted">{selected.detail}</p></div>
          <div className="seg" role="tablist" aria-label="Неделя преподавателя">
            {teacherFilters.map((label, index) => (
              <button key={label} className={filter === index ? "active" : ""} type="button" aria-pressed={filter === index} onClick={() => setFilter(index)}>{label}</button>
            ))}
          </div>
          <div className="section-overview"><strong>Пар в выбранной неделе: {teacherWeekTotal}</strong><span>{teacherFilters[filter]}</span></div>
          <label className="check"><input type="checkbox" checked={myLessonsOnly} onChange={event=>setMyLessonsOnly(event.target.checked)}/>Только пары моей группы</label>{myLessonsOnly && teacherWeekTotal===0 && <button className="btn" onClick={()=>setMyLessonsOnly(false)}>Показать все группы</button>}
          {!detailLoading && teacherWeekTotal === 0 && <div className="card empty"><p>{detailError ? "В доступной копии занятий для этой недели нет." : "В выбранной неделе занятий нет."}</p>{filter !== 0 && <button className="btn" type="button" onClick={() => setFilter(0)}>Показать обе недели</button>}</div>}
          {week.map(day => (
            <article className="card stack" key={day.day}>
              <h2>{day.title}{teacherPeriod&&` · ${nextTeacherDay(new Date(),day.day,teacherCode(filter,app.invert),teacherPeriod,app.invert)?.toLocaleDateString("ru-RU")||""}`}</h2>
              <button className="btn quiet" aria-expanded={!collapsedDays.includes(day.day)} onClick={()=>setCollapsedDays(values=>values.includes(day.day)?values.filter(value=>value!==day.day):[...values,day.day])}>{collapsedDays.includes(day.day)?`Показать пары · ${day.rows.length}`:"Свернуть день"}</button>
              {!collapsedDays.includes(day.day) && day.rows.map((row, index) => (
                <div className="teacher-lesson-row" key={day.day + row.time + row.subject + index}>
                  <div className="time">{row.time}</div>
                  <strong className="subject">{row.subject}</strong>
                  {row.groups && <div className="muted">{row.groups}</div>}
                  <div className="muted">{row.room}</div>
                  {row.room && !/дистанционно/i.test(row.room) && <Link className="btn quiet" to={`/maps?${lessonMapContext(row)}&subject=${encodeURIComponent(row.subject)}`}>Открыть аудиторию</Link>}
                  <div>{row.parityLabel}</div>
                  {row.mine && <div>Моя группа {teacherPeriod&&<button className="btn quiet" type="button" onClick={()=>{if(!teacherAlive.current)return;const date=nextTeacherDay(new Date(),day.day,row.parity,teacherPeriod,app.invert);if(date){app.setDate(date);navigate(`/schedule?date=${isoDay(date)}`);}}}>Открыть день своей группы</button>}</div>}
                </div>
              ))}
            </article>
          ))}
        </div>
      ) : (
        <div className="stack">
          {catalogLoading && <p className="muted" role="status">Загружаем каталог преподавателей…</p>}
          {error && <div className="banner row" role="status"><span>{error}</span><button className="btn" type="button" disabled={catalogLoading} onClick={() => setCatalogRetry(value => value + 1)}>Повторить</button></div>}
          <div className="card teacher-filter-card"><label className="field">Кафедра<select value={department} onChange={event=>setDepartment(event.target.value)}><option value="">Все кафедры</option>{[...new Set(catalog.map(row=>row.kafedra).filter(Boolean))].sort((a,b)=>a.localeCompare(b,"ru")).map(value=><option key={value}>{value}</option>)}</select></label><div className="teacher-search-bar">
            <input className="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Фамилия или предмет" aria-label="Поиск преподавателя" />
            {!!query.trim() && <button className="btn" type="button" onClick={() => setQuery("")}>Сбросить поиск</button>}
          </div>
          <div className="section-overview">
            <strong>Найдено {found.rows.length} из {found.total}</strong>
            <div className="row"><span>Только мои</span>
              <button className={"switch" + (onlyMine ? " on" : "")} type="button" aria-pressed={onlyMine} aria-label="Только мои" onClick={() => setOnlyMine(value => !value)}><i /></button>
            </div>
          </div>
          </div>
          {found.rows.length === 0 && <div className="card empty teacher-empty"><h2>Преподаватели не найдены</h2><p>{!query.trim() && onlyMine ? "У выбранной группы преподаватели пока не найдены" : "Никого не нашлось"}</p>
            {!!query.trim() && <button className="btn" type="button" onClick={() => setQuery("")}>Сбросить поиск</button>}
            {onlyMine && <button className="btn" type="button" onClick={() => setOnlyMine(false)}>Искать среди всех</button>}</div>}
          {found.rows.map(row => (
            <button className="person teacher-person" key={row.id} type="button" onClick={() => void open(row)}>
              {row.mine && <i className="mine-mark" />}
              <span className="teacher-person-copy"><b>{row.name}</b><span className="muted">{row.detail}</span></span><Icon name="right" size={20} />
            </button>
          ))}
        </div>
      )}
    </section>
  );
}

export function MapsPage() {
  const app = useApp();
  const mapHost=useRef<HTMLElement>(null);
  function showMap(){requestAnimationFrame(()=>{const viewport=mapHost.current?.querySelector<HTMLElement>(".map-viewport");viewport?.focus({preventScroll:true});viewport?.scrollIntoView({block:"center"});});}
  const location=useLocation();
  const mapContext=new URLSearchParams(location.search);
  const [plans, setPlans] = useState<MapPlan[]>([]);
  const [plan, setPlan] = useState<MapPlan | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  const [graphAsset,setGraphAsset]=useState<PublicMapAsset|null>(null);
  const [routeOpen,setRouteOpen]=useState(false);
  const [route,setRoute]=useState<CampusRoute|null>(null);
  const [marker,setMarker]=useState<(CampusNode&{nonce:number})|null>(null);
  const [recentPlans,setRecentPlans]=useBrowseValue<string[]>("map-recent-plans",[]);
  useEffect(() => {
    let stop = false;
    setLoading(true);
    setError("");
    api.loadMaps().then(data => {
      if (stop) return;
      setPlans(data.maps);
      setGraphAsset(data.graph??null);
      const building = sessionStorage.getItem("zapara.map.building");
      const floor = sessionStorage.getItem("zapara.map.floor");
      setPlan(current => roomPlan(data.maps, mapContext.get("room") || "", mapContext.get("building") || "") || data.maps.find(item => item.id === current?.id) || data.maps.find(item => item.building === building && String(item.floor) === floor) || data.maps.find(item => item.building === building) || data.maps[0] || null);
    }).catch(() => { if (!stop) setError("Карты не загрузились. Проверьте сеть и попробуйте ещё раз."); })
      .finally(() => { if (!stop) setLoading(false); });
    return () => { stop = true; };
  }, [retry, location.search]);
  useEffect(() => { if (plan) { sessionStorage.setItem("zapara.map.building", plan.building); sessionStorage.setItem("zapara.map.floor", String(plan.floor)); } }, [plan?.id]);
  useEffect(()=>{if(plan)setRecentPlans(values=>[plan.id,...values.filter(id=>id!==plan.id)].slice(0,6));},[plan?.id]);
  function revealPlace(node:CampusNode,reveal=false){const target=plans.find(plan=>plan.building===node.building&&plan.floor===node.floor);if(target){setPlan(target);setMarker({...node,nonce:Date.now()});if(reveal)showMap();}}
  const buildings = [...new Set(plans.map(item => item.building))];
  const floors = plans.filter(item => item.building === plan?.building).sort((a, b) => a.floor - b.floor);
  const floorAt = floors.findIndex(item => item.id === plan?.id);
  const contextDate = localDay(mapContext.get("date") || "");
  const period = api.readCache().lessons[app.groupId]?.period || app.catalog?.period;
  const today = new Date();
  const todayLessons = period && app.timetableAvailable ? lessonsOn(visibleLessons(app.lessons, app.subgroups[app.groupId] || {}), today, period.start, period.weekCount, app.invert) : [];
  const upcoming = heroLesson(todayLessons, today, today);
  const automatic = upcoming ? roomPlan(plans, upcoming.roomRaw || upcoming.classroomRaw || "", upcoming.buildingRaw || "") : null;
  return (
    <section className="page maps-page" ref={mapHost}>
      <Head title="Карты" text="Планы корпусов Военмеха">
        {contextDate && <Link className="btn quiet" to={`/schedule?date=${isoDay(contextDate)}&subject=${encodeURIComponent(mapContext.get("subject")||"")}&time=${encodeURIComponent(mapContext.get("time")||"")}`} onClick={() => app.setDate(contextDate)}>К выбранной паре</Link>}
        <ShareMenu card={plan ? placeCard(plan.building, String(plan.floor), "", `${plan.building}, ${plan.floor} этаж`) : null} label="Этаж в чат" />
      </Head>
      {(mapContext.get("subject") || mapContext.get("room")) && <div className="map-context"><Icon name="pin" /><div><strong>{mapContext.get("room") ? `Аудитория ${mapContext.get("room")}` : "Аудитория не указана"}</strong><p>{[mapContext.get("subject"), contextDate ? absoluteDate(contextDate) : null, mapContext.get("time")].filter(Boolean).join(" · ")}</p><small>{/дистанц|онлайн/i.test(mapContext.get("room")||"")?"Дистанционное занятие — карта аудитории не требуется.":roomPlan(plans,mapContext.get("room")||"",mapContext.get("building")||"")?"Найдите аудиторию на плане выбранного этажа.":"План этой аудитории не распознан. Выберите корпус и этаж вручную ниже."}</small></div></div>}
      {error && <p className="banner" role="alert">{error}</p>}
      {graphAsset&&<RoomActivity key={'room-activity'+studyOwner(app)} asset={graphAsset} onMark={node=>revealPlace(node,true)}/>}
      {graphAsset && <button className="btn" aria-expanded={routeOpen} onClick={()=>setRouteOpen(value=>!value)}>{routeOpen?"Скрыть маршрут":"Построить маршрут по кампусу"}</button>}
      {graphAsset&&routeOpen&&<CampusRouteView key={JSON.stringify([app.session?.user?.userId||"guest",app.session?.familyId||"",app.groupId])} asset={graphAsset} plan={plan} plans={plans} classroom={routeClassroom(mapContext.get("classroom"),mapContext.get("room"),mapContext.get("building"))} onPlan={setPlan} onMark={revealPlace} onRoute={setRoute}/>}
      {recentPlans.length>1&&<details><summary>Недавно просмотренные этажи</summary><div className="row">{recentPlans.map(id=>plans.find(row=>row.id===id)).filter((row):row is MapPlan=>!!row).map(row=><button className="btn quiet" key={row.id} onClick={()=>setPlan(row)}>{row.building} · {row.floor} этаж</button>)}</div></details>}
      {!mapContext.get("room") && <div className="card row"><span className="muted">{!app.groupId ? "Группа не выбрана. Корпус и этаж можно выбрать вручную." : !app.timetableAvailable ? "Расписание ещё не загружено. Доступен ручной выбор плана." : !upcoming ? "Сегодня предстоящих занятий нет. Планы доступны ниже." : automatic ? `Ближайшая пара: ${upcoming.roomRaw || upcoming.classroomRaw}` : "Для аудитории ближайшей пары подходящий план не найден. Выберите его вручную."}</span>{!app.groupId && <Link className="btn" to="/settings?section=study">Выбрать группу</Link>}{automatic && <button className="btn" type="button" onClick={() => {setPlan(automatic);showMap();}}>К ближайшей паре</button>}</div>}
      <div className="map-tools map-selectors" role="group" aria-label="Выбор корпуса и этажа">
        {buildings.length > 0 && <div className="seg" role="group" aria-label="Корпус">{buildings.map(building => <button key={building} className={plan?.building === building ? "active" : ""} aria-pressed={plan?.building === building} type="button" onClick={() => setPlan(chooseBuildingPlan(plans,building,plan?.floor))}>{building}</button>)}</div>}
        {floors.length > 0 && <div className="map-floors" role="group" aria-label="Этаж"><span className="muted">Этаж</span>{floors.map(item => (
          <button key={item.id} className={"icon-btn" + (item.id === plan?.id ? " selected" : "")} aria-label={`${item.floor} этаж`} aria-pressed={item.id === plan?.id} type="button" onClick={() => setPlan(item)}>{item.floor}</button>
        ))}</div>}
      </div>
      <div className="map-tools map-navigation" role="group" aria-label="Переход между этажами">
        <button className="btn" type="button" disabled={floorAt <= 0} onClick={() => floorAt > 0 && setPlan(floors[floorAt - 1])}><Icon name="down" size={16} />Ниже</button>
        <button className="btn" type="button" disabled={floorAt < 0 || floorAt >= floors.length - 1} onClick={() => floorAt >= 0 && floorAt < floors.length - 1 && setPlan(floors[floorAt + 1])}><Icon name="up" size={16} />Выше</button>
      </div>
      {plan ? <><h2 className="map-plan-title">{plan.building} · {plan.floor} этаж</h2><MapViewer plan={plan} marker={marker?.building===plan.building&&marker.floor===plan.floor?marker:null} lines={route?.legs.filter(leg=>leg.building===plan.building&&leg.floor===plan.floor).map(leg=>leg.points)} onRestorePlan={setPlan} onNextFloor={() => { if (floorAt >= 0 && floorAt < floors.length - 1) setPlan(floors[floorAt + 1]); }} onPreviousFloor={() => { if (floorAt > 0) setPlan(floors[floorAt - 1]); }} /></> : <div className="map-frame"><div className="map-status" role="status"><Icon name="map" size={32} /><h2>{loading ? "Загружаем карты" : error ? "Карты недоступны" : "Планов пока нет"}</h2></div></div>}
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
  const [groupQuery,setGroupQuery]=useBrowseValue("friends-groupQuery","");
  const [groupFilter,setGroupFilter]=useBrowseValue("friends-groupFilter","all");
  const [encounterGroup,setEncounterGroup]=useBrowseValue("friends-encounterGroup","");
  const [encounterDate,setEncounterDate]=useBrowseValue("friends-encounterDate","");
  const [allEncounters,setAllEncounters]=useState(false);
  const [failedGroups,setFailedGroups]=useState<string[]>([]);
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
      strictness: app.intersectionStrictness, now, limit:100,
    }) : null;
  const activeCount = app.friends.filter(friend => friend.enabled).length;
  const missingCount = forecast?.missingGroups.length || 0;
  const encounters=(forecast?.encounters||[]).filter(row=>(!encounterGroup||row.friendGroupName===encounterGroup)&&(!encounterDate||isoDay(row.date)===encounterDate));
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
    setFailedGroups(names.filter((_,index)=>!outcomes[index]));
    if (outcomes.some(ok => !ok)) setError("Не все расписания удалось обновить. Сохранённые копии остаются доступны.");
    setRefreshing(false);
  }
  function add(event: FormEvent) {
    event.preventDefault();
    const selection = friendGroupChoice(name, groups);
    if (selection.error) { setError(selection.error); return; }
    const group = knownGroup(name);
    if (!name.trim() || app.friends.length >= 5) return;
    if (duplicateGroup(name)) { setError("Эта группа уже добавлена."); return; }
    if (group?.id === app.groupId) { setError("Ваша группа уже есть в расписании."); return; }
    const used = new Set(app.friends.map(friend => friend.color?.toUpperCase()));
    const color = palette.find(value => !used.has(value.toUpperCase())) || palette[0];
    try { app.saveFriends([...app.friends, { id: crypto.randomUUID(), groupName: selection.name, members: members.trim(), enabled: true, color }]); } catch { setError("Группа не сохранена. Ввод оставлен; повторите попытку."); return; }
    setName(""); setMembers("");
    setError(selection.manual ? "Группа добавлена вручную без проверки: каталог недоступен. Расписание появится после загрузки каталога." : "");
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
          {([[50, "В корпусе"], [75, "На этаже"], [100, "В аудитории"]] as const).map(([value, label]) =>
            <button key={value} type="button" className={app.intersectionStrictness === value ? "active" : ""}
              aria-pressed={app.intersectionStrictness === value} onClick={() => app.setIntersectionStrictness(value)}>{label}</button>)}
        </div>
        <label className="check"><input type="checkbox" checked={app.showAbsentFriends} onChange={event => app.setShowAbsentFriends(event.target.checked)} />
          <span>Показывать и группы без совпадения<span className="muted"> — в карточках пар будет виден уровень встречи или её отсутствие</span></span></label>
      </div>
      <CommonFreeTime mine={visibleLessons(app.lessons,app.subgroups[app.groupId]||{})} friends={schedules} period={period} date={app.date} onDate={app.setDate} invert={app.invert} known={!!ownTimetable&&app.timetableAvailable}/>
      <div className="intersection-forecast-head row">
        <div><h2>Ближайшие пересечения</h2><p className="sub">До 100 совпадений на ближайшие 14 дней по доступным расписаниям</p></div>
        {activeCount > 0 && <button className="btn" type="button" disabled={refreshing} onClick={() => void refreshFriends()}>
          <Icon name="refresh" size={16} />{refreshing ? "Обновляем…" : "Обновить расписания"}</button>}
      </div>
      {error && <div className="banner" role="status">{error}</div>}
      {!app.groupId && <div className="card empty">Выберите свою группу в настройках, чтобы увидеть встречи с друзьями.<Link className="btn" to="/settings">Открыть настройки</Link></div>}
      {!!app.groupId && !app.timetableAvailable && <div className="card empty">{app.timetableLoading ? "Загружаем ваше расписание…" : "Ваше расписание недоступно. Обновите его в разделе «Расписание»."}</div>}
      {!!forecast && activeCount === 0 && <div className="card empty">Включите группу друзей или добавьте новую, чтобы увидеть пересечения.</div>}
      {!!forecast && activeCount > 0 && forecast.encounters.length === 0 &&
        <div className="card empty">{forecast.checkedGroups === 0 ? "Совместимые расписания групп друзей ещё не загружены." : "По выбранной точности встреч в ближайшие две недели не найдено."}</div>}
      {failedGroups.map(name=><div className="banner row" key={name}><span>Не обновилась группа {name}</span><button className="btn" disabled={refreshing} onClick={()=>{setRefreshing(true);void updateFriendTimetable(name).then(ok=>{if(ok)setFailedGroups(rows=>rows.filter(row=>row!==name));}).finally(()=>setRefreshing(false));}}>Повторить для этой группы</button></div>)}
      {!!forecast && forecast.encounters.length > 0 && <div className="intersection-forecast stack" aria-label="Ближайшие пересечения">
        <div className="row"><label className="field">Группа встречи<select value={encounterGroup} onChange={event=>setEncounterGroup(event.target.value)}><option value="">Все группы</option>{app.friends.map(friend=><option key={friend.id}>{friend.groupName}</option>)}</select></label><label className="field">Дата встречи<input type="date" value={encounterDate} onChange={event=>setEncounterDate(event.target.value)}/></label><button className="btn quiet" onClick={()=>{setEncounterDate("");setEncounterGroup("");}}>Все даты и группы</button></div>
        {encounters.length===0 && <FilterEmpty onReset={()=>{setEncounterDate("");setEncounterGroup("");}}/>}
        {(allEncounters?encounters:encounters.slice(0,3)).map((encounter, index) => <article className="card intersection-encounter" key={`${encounter.date.toDateString()}-${encounter.time}-${encounter.friendGroupName}-${index}`}
          style={{ borderLeftColor: encounter.color || "var(--line-strong)" }}>
          <strong>{encounter.date.toLocaleDateString("ru-RU", { weekday: "short", day: "numeric", month: "long" })} · {encounter.time} · {encounter.subject}</strong>
          <span>{encounter.friendGroupName}{encounter.members ? ` · ${encounter.members}` : ""}</span>
          <span className="muted">{intersectionPlace(encounter.score)}{encounter.friendRoom ? ` · ${encounter.friendRoom}` : ""}</span>
          <Link className="btn quiet" to={`/schedule?date=${isoDay(encounter.date)}&time=${encodeURIComponent(encounter.time)}&subject=${encodeURIComponent(encounter.subject)}`} onClick={()=>app.setDate(encounter.date)}>Открыть пару в моём расписании</Link>
        </article>)}
      </div>}
      {!!forecast && missingCount > 0 && <p className="muted" role="status">Нет подходящего загруженного расписания: {forecast.missingGroups.join(", ")}. Для этих групп прогноз пока неполный.</p>}
      {encounters.length>3 && <button className="btn" onClick={()=>setAllEncounters(value=>!value)}>{allEncounters?"Свернуть встречи":`Показать все найденные · ${encounters.length}`}</button>}
      <h2 className="section-list-title">Группы в расписании <span className="chip">{app.friends.length}</span></h2><SearchField label="Найти группу или человека" value={groupQuery} onChange={setGroupQuery}/><label className="field">Показать группы<select value={groupFilter} onChange={event=>setGroupFilter(event.target.value)}><option value="all">Все</option><option value="enabled">Включённые</option><option value="missing">Без расписания</option></select></label>{(groupQuery||groupFilter!=="all") && <button className="btn quiet" onClick={()=>{setGroupQuery("");setGroupFilter("all");}}>Сбросить отбор групп</button>}
      <p className="sub">До пяти групп. Их пары отмечаются в вашем расписании.</p>
      <form id="friend-editor" className="card stack friend-compose" hidden={!editorOpen} onSubmit={add} style={{ marginTop: 12 }}>
        <label className="field">Группа<input list="friend-groups" value={name} onChange={event => setName(event.target.value)} placeholder="А863С" /></label>
        <datalist id="friend-groups">{(app.catalog?.groups || []).map(group => <option key={group.id} value={group.name} />)}</datalist>
        {groups.length === 0 && <p className="muted" role="status">Каталог недоступен: группу можно добавить вручную, но название пока не будет проверено.</p>}
        <label className="field">Имена<input value={members} onChange={event => setMembers(event.target.value)} placeholder="Необязательно" /></label>
        <button className="btn primary" type="submit" disabled={app.friends.length >= 5}>Добавить</button>
      </form>
      {app.friends.length === 0 && !editorOpen && <div className="card empty">Группы друзей ещё не добавлены.
        <button className="btn" type="button" onClick={() => setEditorOpen(true)}>Добавить первую группу</button>
      </div>}
      <div className="stack" style={{ marginTop: 12 }}>
        {app.friends.filter(friend=>noteSearch(groupQuery,friend.groupName,friend.members) && (groupFilter==="all" || groupFilter==="enabled" && friend.enabled || groupFilter==="missing" && !schedules.find(row=>row.groupName===friend.groupName)?.lessons)).map(friend => <FriendRow key={friend.id} friend={friend}
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
  const [colorDraft,setColorDraft]=useState(friend.color);
  const [error, setError] = useState("");
  const dirty = name !== friend.groupName || members !== friend.members || colorDraft !== friend.color;
  function save(event: FormEvent) {
    event.preventDefault();
    const next = name.trim();
    if (!next) { setError("Укажите группу."); return; }
    if (duplicateGroup(next, friend.id)) { setError("Эта группа уже добавлена."); return; }
    const selection = friendGroupChoice(next, groups);
    if (selection.error && next !== friend.groupName) { setError(selection.error); return; }
    const known = groups.find(group => group.id === next || group.name.toLocaleLowerCase("ru-RU") === next.toLocaleLowerCase("ru-RU"));
    if (known?.id === app.groupId) { setError("Ваша группа уже есть в расписании."); return; }
    const groupName = known?.name || next;
    try { app.saveFriends(app.friends.map(item => item.id === friend.id ? { ...item, groupName, members: members.trim(),color:colorDraft } : item)); }
    catch { setError("Сохранить не удалось. Черновик остался в редакторе.");return; }
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
          onClick={() => {try{app.saveFriends(app.friends.map(item => item.id === friend.id ? { ...item, enabled: !item.enabled } : item));}catch{setError("Не удалось сохранить переключение. Группа не изменена.");}}}><i /></button>
        <button className="btn" type="button" aria-expanded={editing} onClick={() => { if(editing&&dirty&&!window.confirm("Отбросить изменения группы, имён и цвета?"))return; setName(friend.groupName); setMembers(friend.members);setColorDraft(friend.color); setError(""); setEditing(value => !value); }}>{editing ? "Отмена" : "Изменить"}</button>
        <button className="btn" type="button" onClick={() => {
          if (window.confirm(`Удалить группу ${friend.groupName} из пересечений?`))
            try{app.saveFriends(app.friends.filter(item => item.id !== friend.id));}catch{setError("Группа не удалена: сохранение недоступно. Повторите попытку.");}
        }}>Удалить</button>
      </div>
      {!editing && error && <p role="alert">{error}</p>}
      {editing && <form className="stack friend-row-edit" onSubmit={save}>
        <label className="field">Группа<input list="friend-groups" value={name} onChange={event => setName(event.target.value)} /></label>
        {groups.length === 0 && <p className="muted">Ручной ввод: каталог недоступен, название пока не проверяется.</p>}
        <label className="field">Имена друзей<input value={members} onChange={event => setMembers(event.target.value)} placeholder="Необязательно" /></label>
        <div className="row friend-palette" role="group" aria-label={`Цвет группы ${friend.groupName}`}>
          {palette.map(color => <button key={color} type="button" className={"friend-color-choice" + (colorDraft?.toUpperCase() === color.toUpperCase() ? " selected" : "")}
            style={{ backgroundColor: color }} aria-label={`Цвет ${color}`} aria-pressed={colorDraft?.toUpperCase() === color.toUpperCase()}
            disabled={app.friends.some(item => item.id !== friend.id && item.color?.toUpperCase() === color.toUpperCase())}
            onClick={() => setColorDraft(color)} />)}
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
  const now = useClock();
  const [sourceFilter,setSourceFilter]=useBrowseValue<"all"|"personal"|"shared">("homework-source","all");
  const [deadlineFilter,setDeadlineFilter]=useBrowseValue<"all"|ReturnType<typeof dueBucket>>("homework-deadline","all");
  const [fileFilter,setFileFilter]=useBrowseValue("homework-files",false);
  const [order,setOrder]=useBrowseValue<"original"|"subject"|"deadline">("homework-order","original");
  const [collapsedBuckets,setCollapsedBuckets]=useState<string[]>([]);
  const { controller, refresh, readLocal, field, draft, busy, note } = useHomeworkDraft();
  const duplicateApproval=useRef<{draft:typeof draft;operationId:string}|null>(null);
  const { subject, text, share, nth, sharedDeadline, deadlineMode, audience, topicId, pending } = draft;
  const setNote = (value: string) => { controller.note = value; refresh(); };
  const [editorOpen, setEditorOpen] = useState(false);
  const [discardOpen, setDiscardOpen] = useState(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const location = useLocation();
  const navigate = useNavigate();
  const chosenSubject = new URLSearchParams(location.search).get("subject");
  const chosenId = new URLSearchParams(location.search).get("id");
  const chosenSharedId = new URLSearchParams(location.search).get("sharedId");
  const exactSubjectKey = new URLSearchParams(location.search).get("subjectKey");
  const [communityId, setCommunityId] = useState("");
  const [copies, setCopies] = useState<GroupHomeworkCopy[]>([]);
  const [copiesScopeTag, setCopiesScopeTag] = useState("");
  const [copiesLoading, setCopiesLoading] = useState(false);
  const [copiesFailed, setCopiesFailed] = useState(false);
  const [copiesRetry, setCopiesRetry] = useState(0);
  const [copyBusy, setCopyBusy] = useState<string | null>(null);
  const [editingCopy, setEditingCopy] = useState<{ id: string; revision: number; title: string; body: string; deadline: string; audience: HomeworkAudience; topicId: string | null } | null>(null);
  const [copyEditConflict, setCopyEditConflict] = useState(false);
  const [browseState, setBrowseState] = useBrowseValue<{ owner: string; query: string; status: HomeworkBrowseStatus }>("homework-query-status",{ owner: "", query: "", status: "active" });
  const [personalUndo, setPersonalUndo] = useState<PersonalHomeworkUndo | null>(null);
  const [personalEdit,setPersonalEdit]=useState<PersonalEditDraft|null>(null);
  const copyScopeKey = JSON.stringify([!!app.session?.authenticated, app.session?.user?.userId, app.groupId, communityId]);
  const copyRequests = useRef(new HomeworkRequestScope()).current;
  copyRequests.scope(copyScopeKey);
  const copyOwnerKey = JSON.stringify([!!app.session?.authenticated, app.session?.user?.userId, app.groupId]);
  const loadedCopyOwner=useRef("");
  useEffect(() => { setPersonalUndo(null); }, [copyOwnerKey]);
  useEffect(() => {
    if (!personalUndo) return;
    const timer = window.setTimeout(() => setPersonalUndo(current => current === personalUndo ? null : current), Math.max(0, personalUndo.expiresAt - Date.now()));
    return () => window.clearTimeout(timer);
  }, [personalUndo]);
  const undoTask = personalUndo ? app.homework.find(item => item.id === personalUndo.id) : undefined;
  const visiblePersonalUndo = personalUndo && canUndoPersonalHomework(personalUndo, undoTask, copyOwnerKey, Date.now()) ? personalUndo : null;
  function togglePersonal(item: HomeworkItem) {
    const saved=app.homework.find(row=>row.id===item.id);
    if(!saved)return;
    setPersonalUndo(null);
    app.saveHomework({ ...saved, done: !saved.done });
    setPersonalUndo(personalHomeworkUndo(saved, copyOwnerKey, Date.now()));
  }
  function undoPersonal() {
    const ticket = visiblePersonalUndo;
    setPersonalUndo(null);
    if (!ticket) return;
    const current = app.homework.find(item => item.id === ticket.id);
    if (canUndoPersonalHomework(ticket, current, copyOwnerKey, Date.now()) && current)
      app.saveHomework({ ...current, done: ticket.beforeDone });
  }
  const browse = browseState.owner === copyOwnerKey ? browseState : { owner: copyOwnerKey, query: "", status: "active" as const };
  const setBrowse = (change: Partial<Pick<typeof browse, "query" | "status">>) => {
    setBrowseState({ ...browse, ...change });
    if (chosenId || chosenSharedId) {
      const query = new URLSearchParams(location.search);
      query.delete("id"); query.delete("sharedId");
      navigate({ pathname: location.pathname, search: query.toString() }, { replace: true });
    }
  };
  function resetFilters(){setBrowse({query:"",status:"all"});setSourceFilter("all");setDeadlineFilter("all");setFileFilter(false);setOrder("original");setCollapsedBuckets([]);}
  useEffect(()=>{setCollapsedBuckets([]);},[copyOwnerKey]);
  const requestedTarget: HomeworkTarget = chosenSharedId ? { kind: "shared", id: chosenSharedId } : chosenId ? { kind: "local", id: chosenId } : null;
  const homeworkReady = app.privateHomework.ready && app.sessionLoaded
    && (!app.session?.authenticated || !app.privateHomework.settings || app.groupId === (app.privateHomework.settings.selectedGroupId || ""));
  const targetOwner = useRef<{ locationKey: string; owner: string | null }>({ locationKey: location.key, owner: null });
  if (targetOwner.current.locationKey !== location.key) targetOwner.current = { locationKey: location.key, owner: null };
  if (targetOwner.current.owner === null && homeworkReady) targetOwner.current.owner = copyOwnerKey;
  const targetIsCurrentOwner = targetOwner.current.owner === copyOwnerKey;
  const target = targetIsCurrentOwner ? requestedTarget : null;
  const copyOwnerRef = useRef(copyOwnerKey);
  copyOwnerRef.current = copyOwnerKey;
  const safeCopies = scopedValue(copies, copiesScopeTag, copyScopeKey, []);
  const copiesReady = !app.session?.authenticated || !app.groupId || copiesScopeTag === copyScopeKey;
  useEffect(() => { setCopyBusy(null); setEditingCopy(null); setCopyEditConflict(false); }, [copyScopeKey]);
  const recipients = useHomeworkAudienceData(communityId);
  const subjects = [...new Set(app.lessons.map(lesson => lesson.subjectRaw))];
  const browsePeriod=api.readCache().lessons[app.groupId]?.period;
  const personalDate=(item:HomeworkItem)=>{const day=browsePeriod?personalHomeworkDue(item,visibleLessons(app.lessons,app.subgroups[app.groupId]||{}),browsePeriod,app.invert):null;return day?isoDay(day):null;};
  const withDates=app.homework.map(item=>({...item,deadlinePrecision:browsePeriod?"date" as const:"instant" as const,deadlineAt:browsePeriod?personalHomeworkDue(item,visibleLessons(app.lessons,app.subgroups[app.groupId]||{}),browsePeriod,app.invert)?.toISOString()??null:item.deadlineAt}));
  const browsing = browseHomework(withDates, safeCopies, { subject: chosenSubject, exactSubjectKey, query: browse.query, status: browse.status, target, editingSharedId: editingCopy?.id,source:sourceFilter,deadline:deadlineFilter,onlyFiles:fileFilter,order,now });
  const visibleCopies = browsing.copies;
  const visibleHomework = browsing.local;
  const homeworkBuckets=(["overdue","today","soon","later","none"] as const).map(key=>({key,label:({overdue:"Просрочено",today:"Сегодня",soon:"Скоро",later:"Позже",none:"Без даты"})[key],rows:visibleHomework.filter(item=>dueBucket(item.deadlineAt,now,item.deadlinePrecision)===key)})).filter(group=>group.rows.length>0);
  useEffect(()=>{if(requestedTarget)setCollapsedBuckets([]);},[chosenId,chosenSharedId]);
  const targetVisible = target?.kind === "shared" ? visibleCopies.some(item => item.homeworkId === target.id) : target?.kind === "local" ? visibleHomework.some(item => item.id === target.id) : false;
  const targetMissing = homeworkReady && !!requestedTarget && !targetVisible && copiesReady && !copiesLoading && !copiesFailed;
  const emptyKind = homeworkReady && copiesReady && !copiesLoading && !copiesFailed ? homeworkEmptyKind(browsing.total, browsing.shown, targetMissing) : null;
  const focusedTarget = useRef("");
  useEffect(() => {
    if (!target || !targetVisible || !targetIsCurrentOwner) return;
    const key = `${copyOwnerKey}:${location.key}:${target.kind}:${target.id}`;
    if (focusedTarget.current === key) return;
    if (document.activeElement?.matches("input, textarea, [contenteditable='true']")) { focusedTarget.current = key; return; }
    const element = document.getElementById(target.kind === "shared" ? `homework-shared-${target.id}` : target.id);
    if (!element) return;
    focusedTarget.current = key;
    element.focus({ preventScroll: true });
    element.scrollIntoView({ block: "center", behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" });
  }, [copyOwnerKey, location.key, target?.kind, target?.id, targetVisible, targetIsCurrentOwner]);
  const duePeriod = api.readCache().lessons[app.groupId]?.period || app.catalog?.period || { start: isoDay(app.date), weekCount: 2, title: "", timeZone: "" };
  const previewDue = personalHomeworkDue({ id: "preview", subject, text, done: false, created: new Date().toISOString(), targetNthOccurrence: nth }, visibleLessons(app.lessons, app.subgroups[app.groupId] || {}), duePeriod, app.invert);
  useEffect(() => { controller.preload(chosenSubject || ""); refresh(); }, [controller, chosenSubject, app.groupId, app.session?.authenticated, app.session?.user?.userId]);
  useEffect(() => { setDiscardOpen(false); setEditorOpen(false); }, [app.groupId, app.session?.authenticated, app.session?.user?.userId]);
  useEffect(() => {
    let stop = false;
    const owner = copyOwnerKey;
    const current = () => !stop && copyOwnerRef.current === owner;
    setCopiesLoading(!!app.session?.authenticated && !!app.groupId);
    if(loadedCopyOwner.current!==owner){setCommunityId("");setCopies([]);setCopiesScopeTag("");loadedCopyOwner.current=owner;}
    setCopiesFailed(false);
    void followGroupCommunity(
      { authenticated: !!app.session?.authenticated, groupId: app.groupId },
      groupId => api.communities(groupId),
      state => {
        if (!current()) return;
        setCommunityId(state.communityId);
        if (!state.communityId) { setCopies([]); setCopiesScopeTag(JSON.stringify([!!app.session?.authenticated, app.session?.user?.userId, app.groupId, ""])); setCopiesLoading(false); }
        if (state.failed) { setCopiesFailed(true); setCopiesLoading(false); }
        else if (state.communityId) void api.groupHomework(state.communityId)
          .then(loaded => { if (current()) { setCopiesScopeTag(JSON.stringify([!!app.session?.authenticated, app.session?.user?.userId, app.groupId, state.communityId])); setCopies(loaded); } })
          .catch(() => { if (current()) setCopiesFailed(true); })
          .finally(() => { if (current()) setCopiesLoading(false); });
      },
    );
    return () => { stop = true; };
  }, [app.session?.authenticated, app.session?.user?.userId, app.session?.familyId, app.groupId, copiesRetry]);
  function addPending(list: FileList | null, kind: "photo" | "document") {
    if (controller.busy) return;
    const file = list?.[0];
    if (!file) return;
    if (pending.length >= HOMEWORK_FILE_LIMIT) {
      setNote("Можно приложить не больше шести файлов");
      return;
    }
    try {
      checkHomeworkFile(kind, file.name, file.size);
      field("pending", [...pending, { file, kind }]);
      setNote("");
    } catch (error) {
      const code = error instanceof Error ? error.message : "";
      setNote(code === "big" ? "Файл слишком большой" : "Такой файл приложить нельзя");
    }
  }
  async function add(event: FormEvent) {
    event.preventDefault();
    if (controller.busy) return;
    if (controller.draft.share && communityId && recipients.loading) { setNote("Загружаем возможности группы. Повторите сохранение через несколько секунд."); return; }
    const validation = validateHomeworkDraft(controller.draft);
    if (validation) { setNote(validation); return; }
    const ticket = controller.begin();
    if (!ticket) return;
    if(!ticket.operation.localSaved&&(duplicateApproval.current?.draft!==controller.draft||duplicateApproval.current?.operationId!==ticket.operation.id)){
      const visible=visibleLessons(app.lessons,app.subgroups[app.groupId]||{});
      const due=(item:HomeworkItem)=>{const date=browsePeriod?personalHomeworkDue(item,visible,browsePeriod,app.invert):null;return date?isoDay(date):null;};
      const duplicate=duplicatePersonalHomework(app.homework,ticket.draft,due({id:ticket.operation.id,subject:ticket.draft.subject,text:ticket.draft.text,created:ticket.operation.created,done:false,targetNthOccurrence:ticket.draft.nth}),due);
      if(duplicate&&!window.confirm(`Личное задание «${duplicate.subject}» с тем же текстом и датой уже есть${duplicate.done?" и отмечено выполненным":""}. Всё равно создать ещё одно?`)){
        controller.cancelBeforeSave(ticket,"Новое задание не создано. Текст и выбранные файлы сохранены в черновике.");refresh();return;
      }
      if(duplicate)duplicateApproval.current={draft:controller.draft,operationId:ticket.operation.id};
    }
    const saveCopyScope = copyRequests.capture();
    refresh();
    setDiscardOpen(false);
    try {
      const { outcome, success } = await runHomeworkSave(ticket, {
        signedIn: !!app.session?.authenticated, communityId,
        audienceSupported: recipients.supported,
        personalDue: saved => personalHomeworkDue({ id: saved.operation.id, subject: saved.draft.subject, text: saved.draft.text, done: false, created: saved.operation.created, targetNthOccurrence: saved.draft.nth }, visibleLessons(app.lessons, app.subgroups[app.groupId] || {}), duePeriod, app.invert)?.toISOString() || null,
        isCurrent: value => controller.current(value),
        prepare: async item => {
          const name = checkHomeworkFile(item.kind, item.file.name, item.file.size);
          const blob = item.kind === "photo" ? await compressHomeworkPhoto(item.file) : item.file;
          return { blob, name: item.kind === "photo" ? name.replace(/\.[^.]+$/, ".jpg") : name,
            mime: item.kind === "photo" ? "image/jpeg" : item.file.type || "application/octet-stream" };
        },
        put: putHomeworkBlob, remove: deleteHomeworkBlob, readLocal, saveLocal: app.saveHomework,
        upload: async item => {
          const data = new FormData();
          data.append("file", item.blob, item.file.name);
          if (app.groupId) data.append("groupId", app.groupId);
          const uploaded = await api.uploadHomeworkFile(data);
          checkHomeworkUpload(uploaded);
        },
        share: async (title, body, deadline, destinationTopic, selectedAudience, operationId) => { await api.shareHomework(communityId, title, body, deadline, destinationTopic, selectedAudience, recipients.supported ? operationId : undefined); },
      });
      if (!controller.current(ticket)) return;
      controller.finish(ticket, outcome.note || "Задание сохранено на этом устройстве", success);
      refresh();
      if (mounted.current && success) setEditorOpen(false);
      if (outcome.sent && mounted.current) {
        await loadScopedHomework(copyRequests, saveCopyScope, () => api.groupHomework(communityId),
          loaded => { if (mounted.current && controller.sameScope(ticket)) { setCopiesScopeTag(copyScopeKey); setCopies(loaded); } },
          () => { if (mounted.current && controller.sameScope(ticket)) setCopiesFailed(true); });
      }
    } catch (error) {
      controller.finish(ticket, homeworkSaveError(error, ticket.operation.localSaved), false);
      refresh();
    }
  }
  async function toggleCopy(item: GroupHomeworkCopy) {
    if (!communityId || item.canComplete === false || copyBusy) return;
    const actor = app.session?.user?.userId ?? "";
    if (!actor) return;
    const action = copyRequests.begin();
    const current = () => mounted.current && copyRequests.active(action);
    const next = completeGroupCopy(
      app.homework.map(row => ({ id: row.id, done: row.done })),
      safeCopies.map(row => ({ homeworkId: row.homeworkId, memberId: actor, completed: row.completed })),
      actor,
      item.homeworkId,
      !item.completed,
    );
    if (next.local.some((row, index) => row.done !== app.homework[index]?.done)) return;
    try {
      setCopyBusy(item.homeworkId);
      const saved = await api.completeHomework(communityId, item.homeworkId, next.copies.find(row => row.homeworkId === item.homeworkId)?.completed ?? !item.completed, item.completionRevision);
      if (!current()) return;
      const completed = next.copies.find(row => row.homeworkId === item.homeworkId && row.memberId === actor)?.completed ?? saved.completed;
      setCopies(list => list.map(row => row.homeworkId === item.homeworkId ? { ...row, completed, completionRevision: saved.revision } : row));
    }
    catch (error) { if (current()) { setNote(error instanceof Error && error.message === "409" ? "Отметка изменилась. Загружаем актуальное состояние…" : "Отметку у общей домашки сохранить не получилось"); if (error instanceof Error && error.message === "409") await loadScopedHomework(copyRequests, action, () => api.groupHomework(communityId), rows => { if (current()) { setCopiesScopeTag(copyScopeKey); setCopies(rows); } }, () => { if (current()) setCopiesFailed(true); }); } }
    finally { if (current()) setCopyBusy(null); }
  }
  async function saveCopyEdit(event: FormEvent) {
    event.preventDefault();
    if (!editingCopy || copyBusy || copyEditConflict || recipients.loading) return;
    if (editingCopy.audience.kind === "selected" && !recipients.supported) { setNote("Адресная домашка недоступна на этом сервере."); return; }
    const item = editingCopy;
    const action = copyRequests.begin();
    const current = () => mounted.current && copyRequests.active(action);
    setCopyBusy(item.id);
    try {
      await api.editHomework(communityId, item.id, { title: item.title.trim(), body: item.body.trim(), deadlineAt: item.deadline ? canonicalUtc(item.deadline) : null, topicId: item.topicId, ...(recipients.supported ? { audience: item.audience } : {}) }, item.revision);
      if (!current()) return;
      setEditingCopy(null); setCopyEditConflict(false);
      await loadScopedHomework(copyRequests, action, () => api.groupHomework(communityId), rows => { if (current()) { setCopiesScopeTag(copyScopeKey); setCopies(rows); } }, () => { if (current()) setCopiesFailed(true); });
    } catch (error) {
      if (!current()) return;
      if (error instanceof Error && error.message === "409") {
        setCopyEditConflict(true);
        await loadScopedHomework(copyRequests, action, () => api.groupHomework(communityId), rows => { if (current()) { setCopiesScopeTag(copyScopeKey); setCopies(rows); } }, () => { if (current()) setCopiesFailed(true); });
        if (!current()) return;
        setNote("Публикация изменилась на сервере. Ваш черновик сохранён; сверьте его с актуальной версией.");
      } else setNote("Изменения публикации не сохранились. Черновик сохранён.");
    } finally { if (current()) setCopyBusy(null); }
  }
  return (
    <section className="page homework-page">
      <Head title="Домашка" text="Личное задание хранится на устройстве. Общая публикация содержит текст и срок; вложения остаются личными.">
        <button className="btn primary" type="button" disabled={busy} aria-expanded={editorOpen} aria-controls="homework-editor"
          onClick={() => setEditorOpen(value => !value)}>{editorOpen ? "Скрыть редактор" : controller.dirty ? "Продолжить задание" : "Добавить задание"}</button>
      </Head>
      <p id="homework-save-status" className="muted" role="status" aria-live="polite" aria-atomic="true">{note || (controller.dirty ? "Черновик сохранится при переходе в другой раздел. При перезагрузке страницы он будет потерян." : "")}</p>
      {app.privateHomework.readFailed&&<div className="banner row" role="alert"><span>Сохранённые задания не удалось прочитать. Это не пустой список.</span><button className="btn" type="button" onClick={app.privateHomework.retryRead}>Повторить чтение</button></div>}
      {!app.privateHomework.readFailed&&<HomeworkSubjectOverview items={app.homework} dateOf={personalDate} today={isoDay(now)} onPick={(subject,key)=>{resetFilters();setSourceFilter("personal");const query=new URLSearchParams(location.search);query.set("subject",subject);query.set("subjectKey",key);query.delete("id");query.delete("sharedId");navigate({pathname:location.pathname,search:query.toString()});requestAnimationFrame(()=>focusElement("homework-personal-results"));}}/>}
      {personalEdit?.owner===copyOwnerKey&&<PersonalHomeworkEditor key={`${copyOwnerKey}:${personalEdit.id}`} draft={personalEdit} onChange={setPersonalEdit} onClose={()=>setPersonalEdit(null)}/>}
      {(chosenSubject||exactSubjectKey!==null) && <div className="row homework-filter" role="region" aria-label="Фильтр домашки"><span>Предмет: <strong>{chosenSubject||"Без предмета"}</strong></span><button className="btn" type="button" onClick={() => { const query = new URLSearchParams(location.search); query.delete("subject"); query.delete("subjectKey"); query.delete("id"); query.delete("sharedId"); navigate({ pathname: location.pathname, search: query.toString() }); }}>Показать все предметы</button></div>}
      <label className="field homework-search">Поиск по предмету и заданию<input type="search" value={browse.query} onChange={event => setBrowse({ query: event.target.value })} placeholder="Предмет или текст задания" /></label>
      <details className="card stack"><summary>Дополнительные фильтры</summary><div className="row"><label className="field">Источник<select value={sourceFilter} onChange={event=>setSourceFilter(event.target.value as typeof sourceFilter)}><option value="all">Личные и общие</option><option value="personal">Личные</option><option value="shared">Общие</option></select></label><label className="field">Срок<select value={deadlineFilter} onChange={event=>setDeadlineFilter(event.target.value as typeof deadlineFilter)}><option value="all">Все сроки</option><option value="overdue">Просроченные</option><option value="today">Сегодня</option><option value="soon">В ближайшие дни</option><option value="later">Позже</option><option value="none">Без даты</option></select></label><label className="field">Порядок<select value={order} onChange={event=>setOrder(event.target.value as typeof order)}><option value="original">Исходный</option><option value="subject">По предмету</option><option value="deadline">По сроку</option></select></label></div><label className="check"><input type="checkbox" checked={fileFilter} onChange={event=>setFileFilter(event.target.checked)}/>Только личные задания с файлами</label><button className="btn quiet" type="button" onClick={resetFilters}>Сбросить все условия</button></details>
      <div className="section-overview">
        <strong>Мои задания: {app.homework.length} · Общие: {copiesLoading || !copiesReady ? "загрузка…" : safeCopies.length}</strong>
        <span>Показано: {browsing.shown}{copiesFailed ? " · общая домашка недоступна" : copiesLoading || !copiesReady ? " · общая домашка загружается" : ` из ${browsing.total}`} · Выполнено: {app.homework.filter(item => item.done).length + safeCopies.filter(item => item.completed).length}</span>
      </div>
      <div className="row homework-filters" role="group" aria-label="Показать задания"><button type="button" className="btn" aria-pressed={browse.status === "active"} onClick={() => setBrowse({ status: "active" })}>Активные</button><button type="button" className="btn" aria-pressed={browse.status === "done"} onClick={() => setBrowse({ status: "done" })}>Готово у меня</button><button type="button" className="btn" aria-pressed={browse.status === "all"} onClick={() => setBrowse({ status: "all" })}>Все</button>{(chosenSubject || exactSubjectKey!==null || browse.query.trim() || browse.status !== "all") && <button type="button" className="btn quiet" onClick={() => { resetFilters(); if (chosenSubject||exactSubjectKey!==null) { const query = new URLSearchParams(location.search); query.delete("subject"); query.delete("subjectKey"); navigate({ pathname: location.pathname, search: query.toString() }); } }}>Сбросить фильтры</button>}</div>
      <form id="homework-editor" className="card stack homework-compose" hidden={!editorOpen} aria-busy={busy} aria-describedby="homework-save-status" onInvalid={() => setNote(validateHomeworkDraft(controller.draft) || "Проверьте поля задания")} onSubmit={event => void add(event)}>
        <fieldset className="stack homework-fields" disabled={busy}>
        <legend className="sr-only">Новое задание</legend>
        <div className="homework-editor-group"><h2>Задание</h2><label className="field">Предмет (обязательно)<input required list="subjects" value={subject} onChange={event => field("subject", event.target.value)} /></label>
        <datalist id="subjects">{subjects.map(item => <option key={item} value={item} />)}</datalist>
        {subjects.length===0&&<p className="muted">Предмет можно ввести вручную. Пока расписание не содержит подходящих занятий, срок неизвестен; он появится после загрузки расписания.</p>}
        <label className="field">Задание (обязательно)<textarea required value={text} onChange={event => field("text", event.target.value)} /></label>
        </div><div className="homework-editor-group"><h2>Вложения <span className="muted">{pending.length} / {HOMEWORK_FILE_LIMIT}</span></h2><div className="row">
          <label className="btn" style={{ position: "relative" }}>Фото<input className="sr" type="file" accept="image/jpeg,image/png,image/webp,image/gif" onChange={event => { addPending(event.target.files, "photo"); event.target.value = ""; }} /></label>
          <label className="btn" style={{ position: "relative" }}>Документ<input className="sr" type="file" accept=".pdf,.txt,.csv,.rtf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.odt,.ods,.odp,.zip" onChange={event => { addPending(event.target.files, "document"); event.target.value = ""; }} /></label>
        </div>
        {pending.map((item, index) => (
          <span className="chip" style={{ alignSelf: "flex-start" }} key={`${item.file.name}-${item.file.size}-${index}`}>
            {item.file.name}
            <button type="button" aria-label={`Убрать файл ${item.file.name}`} style={{ border: 0, background: "transparent", color: "inherit", padding: 0 }} onClick={() => field("pending", pending.filter((_, at) => at !== index))}>Убрать</button>
          </span>
        ))}
        </div><div className="homework-editor-group"><h2>Срок и получатели</h2><label className="check">
          <input type="checkbox" checked={share} disabled={!app.session?.authenticated} onChange={event => field("share", event.target.checked)} />
          <span>Опубликовать общую домашку<span className="muted">{app.session?.authenticated ? " — текст и срок получат выбранные участники; вложения останутся в личном задании" : " — войдите в аккаунт, чтобы отправить группе"}</span></span>
        </label>
        <label className="field">К следующему занятию по предмету<select value={nth} onChange={event=>field("nth", Number(event.target.value))}>{Array.from({length:10},(_,i)=><option value={i+1} key={i+1}>{i+1}-е занятие</option>)}</select></label>
        {share && <><HomeworkRecipients communityId={communityId} value={audience} onChange={value => field("audience", value)} disabled={busy} data={recipients}/>
          {!!recipients.space?.topics.length && <label className="field">Канал публикации<select value={topicId} onChange={event => field("topicId", event.target.value)}><option value="">Общая домашка группы</option>{recipients.space.topics.filter(topic => topic.kind === "homework" && !topic.archived && topic.topicId && (topic.canPost || topic.permissions?.includes("homework"))).map(topic => <option key={topic.topicId!} value={topic.topicId!}>{topic.title}</option>)}</select></label>}
          <p className="muted">Группа: {recipients.home?.name || "загрузка…"} · {topicId ? recipients.space?.topics.find(topic => topic.topicId === topicId)?.title || "Канал" : "Общая домашка"} · {audienceLabel(audience)}</p>
          <label className="field">Срок публикации<select value={deadlineMode} onChange={event => field("deadlineMode", event.target.value as typeof deadlineMode)}><option value="personal">Как у личного задания</option><option value="custom">Указать вручную</option><option value="none">Без срока</option></select></label>
          {deadlineMode === "personal" && <p className="muted">{previewDue ? `Срок: ${previewDue.toLocaleString("ru-RU")}` : "Следующее занятие не найдено — публикация будет без срока."}</p>}
          {deadlineMode === "custom" && <label className="field">Срок общей домашки<input type="datetime-local" value={sharedDeadline} onChange={event=>field("sharedDeadline", event.target.value)} /></label>}</>}
        </div><div className="homework-editor-footer"><span className="muted">{pending.length ? `Вложений: ${pending.length}` : "Без вложений"}</span><button className="btn" type="button" disabled={!controller.dirty} onClick={() => setDiscardOpen(true)}>Очистить черновик</button><button className="btn primary" type="submit">{busy ? "Сохраняем…" : "Сохранить"}</button></div>
        {discardOpen && <div className="banner stack" role="group" aria-label="Очистка черновика"><p>Очистить введённые поля и выбранные файлы? Уже сохранённое задание останется на устройстве.</p><div className="row"><button className="btn" type="button" onClick={() => setDiscardOpen(false)}>Продолжить редактирование</button><button className="btn" type="button" onClick={() => { controller.clear(chosenSubject || ""); refresh(); setDiscardOpen(false); }}>Очистить</button></div></div>}
        </fieldset>
      </form>
      {copiesFailed && <div className="card empty"><p>Общая домашка не загрузилась. Проверьте сеть и попробуйте ещё раз.</p><button className="btn primary" type="button" onClick={() => setCopiesRetry(value => value + 1)}>Повторить</button></div>}
      {targetMissing && browsing.shown > 0 && <div className="banner row" role="status"><span>Задание по ссылке не найдено в текущей группе или недоступно вашему аккаунту.</span><button className="btn quiet" type="button" onClick={() => { const query = new URLSearchParams(location.search); query.delete("id"); query.delete("sharedId"); navigate({ pathname: location.pathname, search: query.toString() }, { replace: true }); }}>Показать список</button></div>}
      {emptyKind && <div className="card empty"><p>{emptyKind === "missing" ? "Задание по ссылке не найдено в текущей группе или недоступно вашему аккаунту." : emptyKind === "empty" ? app.groupId ? "Заданий пока нет." : "Учебная группа не выбрана. Выберите группу, чтобы видеть пары и сроки." : "По выбранным фильтрам заданий нет."}</p>{emptyKind === "empty" ? app.groupId ? <button className="btn" type="button" onClick={() => setEditorOpen(true)}>Добавить первое</button> : <Link className="btn" to="/settings?section=study">Выбрать группу</Link> : <button className="btn" type="button" onClick={() => { resetFilters(); const query = new URLSearchParams(location.search); query.delete("subject"); query.delete("subjectKey"); query.delete("id"); query.delete("sharedId"); navigate({ pathname: location.pathname, search: query.toString() }); }}>Сбросить фильтры</button>}</div>}
      {visibleCopies.length > 0 && <h2 className="section-list-title">Общая домашка <span className="chip">{visibleCopies.length}</span></h2>}
      <div className="stack" style={{ marginTop: safeCopies.length > 0 ? 12 : 0 }}>
        {visibleCopies.map(item => (
          <article className={"card homework-task" + (item.completed ? " homework-completed" : "")} id={`homework-shared-${item.homeworkId}`} tabIndex={-1} aria-current={target?.kind === "shared" && target.id === item.homeworkId ? "true" : undefined} key={item.homeworkId}>
            <div className="row" style={{ justifyContent: "space-between" }}>
              <span className="row"><b>{item.title}</b><span className="chip">{audienceLabel(item.audience)}</span></span>
              {item.canComplete !== false && <button className="btn" type="button" disabled={!!copyBusy} onClick={() => void toggleCopy(item)}>{copyBusy === item.homeworkId ? "Сохраняем…" : item.completed ? "Снять отметку" : "Готово у меня"}</button>}
              {item.canEdit && <button className="btn quiet" type="button" disabled={!!copyBusy} onClick={() => { setEditingCopy({ id: item.homeworkId, revision: item.revision, title: item.title, body: item.body, deadline: localDateTimeInput(item.deadlineAt), audience: item.audience || allHomeworkAudience(), topicId: item.topicId || null }); setCopyEditConflict(false); }}>Изменить</button>}
            </div>
            <p className={item.completed ? "done-title" : ""}>{item.body}</p><p className="muted homework-deadline">Срок: {item.deadlineAt ? new Date(item.deadlineAt).toLocaleString("ru-RU") : "Без срока"}</p>
            {editingCopy?.id === item.homeworkId && <form className="stack" onSubmit={event => void saveCopyEdit(event)}><label className="field">Предмет<input required value={editingCopy.title} onChange={event => setEditingCopy({ ...editingCopy, title: event.target.value })}/></label><label className="field">Задание<textarea required value={editingCopy.body} onChange={event => setEditingCopy({ ...editingCopy, body: event.target.value })}/></label><label className="field">Срок<input type="datetime-local" value={editingCopy.deadline} onChange={event => setEditingCopy({ ...editingCopy, deadline: event.target.value })}/></label><HomeworkRecipients communityId={`${communityId}-copy`} value={editingCopy.audience} onChange={audience => setEditingCopy({ ...editingCopy, audience })} data={recipients}/>{copyEditConflict && <div className="banner" role="alert">Актуальная ревизия: {safeCopies.find(row => row.homeworkId === item.homeworkId)?.revision}. Сверьте поля перед повторной отправкой.<button className="btn" type="button" onClick={() => { setEditingCopy({ ...editingCopy, revision: safeCopies.find(row => row.homeworkId === item.homeworkId)?.revision ?? editingCopy.revision }); setCopyEditConflict(false); }}>Использовать актуальную ревизию</button></div>}<div className="row"><button className="btn primary" disabled={!!copyBusy || copyEditConflict}>Сохранить изменения</button><button className="btn quiet" type="button" onClick={() => { setEditingCopy(null); setCopyEditConflict(false); }}>Отмена</button></div></form>}
          </article>
        ))}
      </div>
      <PersonalHomeworkBatch key={JSON.stringify(["complete",copyOwnerKey,app.session?.familyId,app.groupId])} items={visibleHomework}/>
      <HomeworkPostpone key={JSON.stringify(["postpone",copyOwnerKey,app.session?.familyId,app.groupId])} items={visibleHomework} dateOf={personalDate}/>
      <HomeworkPublication key={JSON.stringify(["publish",copyOwnerKey,app.session?.familyId,app.groupId,communityId])} items={visibleHomework} communityId={communityId} recipients={recipients} dateOf={personalDate}/>
      {visibleHomework.length > 0 && <><h2 id="homework-personal-results" tabIndex={-1} className="section-list-title">Мои задания <span className="chip">{visibleHomework.length}</span></h2><HomeworkExportTools key={JSON.stringify([copyOwnerKey,app.session?.familyId,app.groupId])} items={visibleHomework} groupId={app.groupId} groupName={app.catalog?.groups.find(group=>group.id===app.groupId)?.name||app.groupId}/></>}
      {homeworkBuckets.length>1 && <button className="btn quiet" onClick={()=>setCollapsedBuckets(rows=>rows.length?[]:homeworkBuckets.map(group=>group.key))}>{collapsedBuckets.length?"Развернуть все сроки":"Свернуть все сроки"}</button>}
      <div className="stack" style={{ marginTop: 12 }}>
        {homeworkBuckets.map(group=><section className="stack" key={group.key}><button className="btn" aria-expanded={!collapsedBuckets.includes(group.key)} onClick={()=>setCollapsedBuckets(rows=>rows.includes(group.key)?rows.filter(key=>key!==group.key):[...rows,group.key])}>{group.label} · {group.rows.length}</button><div className="stack" hidden={collapsedBuckets.includes(group.key)}>{group.rows.map(item => (
          <article className={"card homework-task" + (item.done ? " homework-completed" : "")} id={item.id} tabIndex={-1} key={item.id} aria-current={target?.kind === "local" && target.id === item.id ? "true" : undefined}>
            <div className="row" style={{ justifyContent: "space-between" }}>
              <span className="row"><b>{item.subject}</b><span className="chip">{item.done ? "Выполнено" : "Личное"}</span></span>
              <button className="btn" type="button" onClick={() => togglePersonal(item)}>{item.done ? "Снова открыть" : "Сделано"}</button>
              <button className="btn quiet" type="button" onClick={()=>setPersonalEdit({id:item.id,owner:copyOwnerKey,subject:item.subject,text:item.text,nth:item.targetNthOccurrence??1})}>Изменить</button>
              <button className="btn quiet" type="button" onClick={()=>{if(controller.dirty&&!window.confirm("Заменить текущий черновик копией этого задания?"))return;controller.clear(item.subject);field("text",item.text);field("share",false);field("nth",1);setEditorOpen(true);requestAnimationFrame(()=>document.getElementById("homework-editor")?.scrollIntoView({block:"start"}));}}>Создать похожее</button>
              <button className="btn quiet" type="button" onClick={()=>{if(!window.confirm(`Удалить личное задание «${item.subject}»? ${item.text.slice(0,100)}`))return;try{app.privateHomework.remove(item.id);setPersonalEdit(current=>current?.id===item.id?null:current);setNote("Задание удалено");}catch(reason){setNote(reason instanceof Error?reason.message:"Удалить не удалось. Задание сохранено.");}}}>Удалить</button>
            </div>
            <p className={item.done ? "done-title" : ""}>{item.text}</p><p className="muted homework-deadline">Срок: {personalHomeworkDue(item,visibleLessons(app.lessons,app.subgroups[app.groupId]||{}),api.readCache().lessons[app.groupId]?.period||app.catalog?.period||{start:isoDay(app.date),weekCount:2,title:"",timeZone:""},app.invert)?.toLocaleDateString("ru-RU") || "Без срока"}</p>
            <HomeworkAttachments files={item.files || []} />
            <div className="row homework-task-footer">
              {app.lessons.some(lesson => sameSubject(lesson.subjectRaw, item.subject)) && <span className="chip">есть в расписании</span>}
              <ShareMenu card={homeworkCard(item)} />
              {browsePeriod && <button className="btn quiet" onClick={()=>{const date=Array.from({length:56},(_,i)=>addDays(new Date(),i)).find(date=>lessonsOn(visibleLessons(app.lessons,app.subgroups[app.groupId]||{}),date,browsePeriod.start,browsePeriod.weekCount,app.invert).some(lesson=>sameSubject(lesson.subjectRaw,item.subject)));if(date){app.setDate(date);navigate(`/schedule?date=${isoDay(date)}&subject=${encodeURIComponent(item.subject)}`);}else setNote("В ближайшие восемь недель подходящее занятие не найдено.");}}>К ближайшему занятию</button>}
              <Link className="btn quiet" to={`/group?homework=${encodeURIComponent(item.text)}&subject=${encodeURIComponent(item.subject)}&date=${isoDay(app.date)}`}>Обсудить задание</Link>
            </div>
          </article>
        ))}</div></section>)}
      </div>
      {visiblePersonalUndo && <div className="banner row homework-undo" role="status"><span>{visiblePersonalUndo.beforeDone ? "Отметка снята" : "Отмечено готово"}</span><button className="btn" type="button" onClick={undoPersonal}>Отменить</button></div>}
    </section>
  );
}

function communityRoleLabel(role: string | null): string {
  return role === "headman" ? "Староста" : role === "curator" ? "Куратор"
    : role === "member" ? "Участник" : "Не в группе";
}

export function CommunityPage() {
  const app = useApp();
  return <CommunityContent key={JSON.stringify([app.session?.authenticated, app.session?.user?.userId, app.session?.familyId, app.groupId])} />;
}

function CommunityContent() {
  const app = useApp();
  const [list, setList] = useState<Community[]>([]);
  const [error, setError] = useState("");
  const [search, setSearch] = useState("");
  const [reloadEpoch, setReloadEpoch] = useState(0);
  const [loading, setLoading] = useState(true);
  const [joining, setJoining] = useState<string[]>([]);
  const [requested, setRequested] = useState<string[]>([]);
  const pending = useRef(new Set<string>());
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  async function join(id: string) {
    if (pending.current.has(id) || requested.includes(id)) return;
    pending.current.add(id); setJoining([...pending.current]); setError("");
    try {
      await api.joinCommunity(id);
      if (!alive.current) return;
      setRequested(values => [...new Set([...values, id])]);
      try { const rows = await api.communities(app.groupId); if (alive.current) setList(rows); }
      catch { if (alive.current) setError("Заявка отправлена, но список не обновился. Повторите загрузку списка."); }
    } catch { if (alive.current) setError("Заявка не отправлена. Проверьте подключение и попробуйте ещё раз."); }
    finally { pending.current.delete(id); if (alive.current) setJoining([...pending.current]); }
  }
  useEffect(() => {
    if (!app.session?.authenticated) return;
    let stopped = false;
    setError("");
    setLoading(true);
    api.communities(app.groupId).then(rows => { if (!stopped) { setList(rows); setLoading(false); } })
      .catch(() => { if (!stopped) { setError("Сообщества не открылись"); setLoading(false); } });
    return () => { stopped = true; };
  }, [app.session?.authenticated, app.session?.user?.userId, app.session?.familyId, app.groupId, reloadEpoch]);
  const visible = list.filter(item => matchesBrowseQuery(search, item.name, item.description));
  if (!app.session?.authenticated) return <section className="page"><div className="card empty"><h1>Сообщество</h1><p>Войдите в аккаунт, чтобы видеть сообщества своей группы.</p><Link className="btn primary" to="/settings?section=account">Открыть настройки аккаунта</Link></div></section>;
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
              {!item.role && (requested.includes(item.communityId) ? <span role="status">Заявка отправлена · ожидает решения</span> : <button className="btn" type="button" disabled={joining.includes(item.communityId)} onClick={() => void join(item.communityId)}>{joining.includes(item.communityId) ? "Отправляем…" : "Подать заявку"}</button>)}
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
  const community = new URLSearchParams(location.search).get("communityId");
  return <GroupContent key={JSON.stringify([app.session?.authenticated,app.session?.user?.userId,app.session?.familyId,app.groupId,community])} />;
}

function GroupContent() {
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
    ? `${app.session?.user?.userId ?? "guest"}:${chat.conversationId}:${chat.kind === "group" && thread !== "list" ? thread.topicId ?? "general" : "direct"}`
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
  const [obligationTarget,setObligationTarget]=useState<Obligation|null>(null);
  const [globalBoardOpen,setGlobalBoardOpen]=useState(false);
  const visibleLog = filterMessages(log, messageFilter, app.session?.user?.userId || "");
  const [matchId,setMatchId]=useState("");
  function moveMatch(direction:number){if(!visibleLog.length)return;const at=visibleLog.findIndex(row=>row.messageId===matchId);const next=at<0?(direction>0?0:visibleLog.length-1):(at+direction+visibleLog.length)%visibleLog.length;setMatchId(visibleLog[next].messageId);focusElement(`group-message-${visibleLog[next].messageId}`);}
  const messageFiltered = !!messageFilter.query.trim() || messageFilter.author !== "all" || messageFilter.kind !== "all";
  const [copyNotice, setCopyNotice] = useState<{ key: string; text: string } | null>(null);
  const [drafts,setDrafts] = useStoredDraft<Record<string,string>>("zapara.group.drafts",()=>({}));
  const draft = drafts[viewKey] ?? "";
  const [discussionContexts,setDiscussionContexts] = useStoredDraft<Record<string,string>>("zapara.group.contexts",()=>({}));
  const context = discussionContexts[viewKey] ?? "";
  const draftScope=home&&chat?.kind==="group"&&thread!=="list"?{owner:app.session?.user?.userId||"guest",community:home.communityId,topic:thread.topicId??"general"}:null;
  const draftLease=draftScope?scopeLease(sessionStorage,draftScope):"";
  useEffect(()=>{if(draftScope&&viewKey)registerGroupChatDraft(sessionStorage,viewKey,draftScope);},[viewKey,home?.communityId]);
  const contextClaim = useRef<string | null>(sessionStorage.getItem("zapara.group.context-claim"));
  useEffect(() => {
    if(!viewKey || viewKeyRef.current!==viewKey || !location.search || draftScope&&!scopeLeaseValid(sessionStorage,draftScope,draftLease))return;
    const params=new URLSearchParams(location.search);const subject=params.get("subject"),task=params.get("homework");if(!subject&&!task)return;
    const claim=`${app.session?.user?.userId || "guest"}:${location.key}:${location.search}`;
    if(contextClaim.current===claim)return;
    contextClaim.current=claim;sessionStorage.setItem("zapara.group.context-claim",claim);
    const caption=[task?`Домашка: ${task}`:`Пара: ${subject}`,params.get("date"),params.get("time")].filter(Boolean).join(" · ");
    setDiscussionContexts(current=>attachDiscussionContext(current,viewKey,caption,true));
  },[viewKey,location.key,location.search,app.session?.user?.userId]);
  const [replyTo, setReplyTo] = useState<string | null>(null);
  const [editing, setEditing] = useState<ChatMessage | null>(null);
  const editReturnDraft = useRef<{ key: string; messageId: string; value: string } | null>(null);
  const repliedMessage = replyTo ? log.find(item => item.messageId === replyTo) : null;
  const [menu, setMenu] = useState<string | null>(null);
  const [reactionFor, setReactionFor] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [focusChat, setFocusChat] = useState(false);
  const [memberSearch, setMemberSearch] = useState("");
  const [leadersOnly,setLeadersOnly]=useState(false);
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
    if (!viewKey || viewKeyRef.current!==viewKey || draftScope&&!scopeLeaseValid(sessionStorage,draftScope,draftLease)) return;
    advanceDraftEpoch(sessionStorage,viewKey);
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
    const retain = !!home && (!selectedCommunityId || home.communityId === selectedCommunityId) && (!selectedConversationId || selectedConversationId === chat?.conversationId);
    if (!retain) drop();
    setError("");
    const openHome = (id: string) => void api.groupHome(id).then(loaded => {
      if (stop) return;
      registerGroupConversation(sessionStorage,app.session?.user?.userId||"guest",loaded.communityId,loaded.groupChat.conversationId);
      setHome(loaded);
      const requested = selectedConversationId === loaded.groupChat.conversationId
        ? loaded.groupChat : loaded.directs.find(item => item.conversationId === selectedConversationId);
      setThread(requested?.kind === "group"
        ? { topicId: null, title: "Общий поток", icon: "💬", lastBody: requested.lastBody,
          lastAuthor: null, lastAt: requested.lastAt, unread: requested.unread, canDelete: false, kind: "chat", activeBallots: 0,
          description: "", accent: "default", pinned: false, writePolicy: "all", canPost: true }
        : "list");
      setChat(requested ?? loaded.groupChat);
      const savedTopic = sessionStorage.getItem(`zapara.group.selection.${app.session?.user?.userId}:${loaded.communityId}`);
      if ((!requested || requested.kind === "group") && savedTopic && savedTopic !== "list") void selectedTopicAuthority(()=>api.topics(loaded.communityId),()=>api.archivedTopics(loaded.communityId),savedTopic==="general"?null:savedTopic).then(result=>{if(!stop&&result.selected)setThread(result.selected);}).catch(()=>undefined);
      setFocusChat(true);
    }).catch(error => { const denied = error instanceof Error && ["401","403","404"].includes(error.message); if(denied)revokeGroupDrafts(error.message==="401"?{owner:app.session?.user?.userId||"guest"}:{owner:app.session?.user?.userId||"guest",community:id}); if (!stop) { if (!retain || denied) drop(); setError(retain && !denied ? "Группа не обновилась. Показаны последние доступные данные." : "Не удалось загрузить группу"); } });
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
  }, [app.session?.authenticated, app.session?.user?.userId, app.session?.familyId, app.groupId, selectedCommunityId, selectedConversationId, reloadEpoch]);
  useEffect(() => {
    if (!app.session?.authenticated || !home?.communityId) return;
    const currentCommunity = home.communityId;
    let stopped = false;
    let running = false;
    const refreshHome = async () => {
      if (stopped || running || document.hidden) return;
      running = true;
      try {
        const next = await api.groupHome(currentCommunity);
        if (!stopped && groupViewKeyRef.current === groupViewKey) {
          setHomeState(current => current.key === groupViewKey && current.value?.communityId === currentCommunity ? { key: current.key, value: next } : current);
          setChat(current => current?.communityId === currentCommunity ? reconcileGroupHomeChat(current, next) : current);
        }
      } catch { /* Keep the last good list; the existing group access poll reports authorization changes. */ }
      finally { running = false; }
    };
    const wake = () => { void refreshHome(); };
    const timer = window.setInterval(wake, 10_000);
    window.addEventListener("focus", wake);
    window.addEventListener("online", wake);
    document.addEventListener("visibilitychange", wake);
    return () => { stopped = true; window.clearInterval(timer); window.removeEventListener("focus", wake); window.removeEventListener("online", wake); document.removeEventListener("visibilitychange", wake); };
  }, [home?.communityId, app.session?.user?.userId]);
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
    let stopped = false;
    const markPublishedRead = () => {
      if (stopped || !wantedReadId || wantedReadId === markedReadId || markingRead) return;
      const target = wantedReadId;
      markingRead = true;
      void api.markRead(chat.conversationId, target)
        .then(() => { if (!stopped) { markedReadId = target; setError(current => current === "Отметка о прочтении не сохранилась" ? "" : current); } })
        .catch(() => { if (!stopped) setError(current => current || "Отметка о прочтении не сохранилась"); })
        .finally(() => { markingRead = false; if (!stopped && wantedReadId !== target) markPublishedRead(); });
    };
    const poller = createGroupPoller(
      after => api.messages(chat.conversationId, topic, after ? { after } : undefined),
      () => logRef.current.key === viewKey ? logRef.current.messages : [],
      (updates, firstLoad) => {
        updateLog(viewKey, updates.messages, firstLoad ? updates.hasOlder : undefined);
        if (updates.throughMessageId) wantedReadId = updates.throughMessageId;
        markPublishedRead();
      },
      error => { if(error instanceof Error && ["401","403","404"].includes(error.message) && draftScope && scopeLeaseValid(sessionStorage,draftScope,draftLease))revokeGroupDrafts(error.message==="401"?{owner:draftScope.owner}:draftScope);if (viewKeyRef.current !== viewKey) return; if(error instanceof Error && ["401","403","404"].includes(error.message)){viewKeyRef.current="";selectionEpoch.current++;clearLog();setThread("list");setError("Доступ к каналу изменился");}else setError("Чат не обновился"); },
    );
    pollerRef.current = { key: viewKey, poller };
    void poller.poll();
    const timer = window.setInterval(() => void poller.poll(), 4000);
    return () => {
      stopped = true;
      poller.dispose();
      if (pollerRef.current?.poller === poller) pollerRef.current = null;
      window.clearInterval(timer);
    };
  }, [viewKey, app.session?.familyId]);
  const communityId = home?.communityId ?? "";
  useEffect(()=>{if(communityId && chat?.kind === "group") sessionStorage.setItem(`zapara.group.selection.${app.session?.user?.userId}:${communityId}`,thread === "list" ? "list" : thread.topicId ?? "general");},[communityId,chat?.kind,thread,app.session?.user?.userId]);
  const topicPage = topicPageState.key === communityId ? topicPageState.value : null;
  const communityTimetable = useCommunityTimetable(home?.groupName ?? null);
  const communitySnapshot = communityTimetable.payload;
  const groupContext = buildGroupChatContext({
    communityGroupName: communitySnapshot?.group.name ?? null,
    selectedGroupName: communitySnapshot?.group.name ?? null,
    timetableAvailable: !!communitySnapshot,
    lessons: communitySnapshot?.lessons ?? [],
    subgroupChoices: app.subgroups[communitySnapshot?.group.id ?? ""] ?? {},
    period: communitySnapshot?.period ?? null,
    invert: app.invert,
    topics: topicPage?.topics ?? [],
    now: app.date,
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
      }).catch(error => { if(!stop && ticket === deskEpoch.current && error instanceof Error && ["401","403","404"].includes(error.message)){ revokeGroupDrafts(error.message==="401"?{owner:app.session?.user?.userId||"guest"}:{owner:app.session?.user?.userId||"guest",community:communityId});viewKeyRef.current="";selectionEpoch.current++;clearLog();setHome(null);setChat(null);setDesk(null);setBoard(null);setError("Доступ к группе изменился"); } });
    };
    refresh();
    const timer = window.setInterval(refresh, 4000);
    return () => { stop = true; deskEpoch.current += 1; window.clearInterval(timer); };
  }, [app.session?.authenticated, communityId]);
  useEffect(()=>{
    if(!communityId||selectedTopicId===undefined)return;
    const captured={owner:app.session?.user?.userId||"guest",community:communityId,topic:selectedTopicId??"general"};const capturedLease=scopeLease(sessionStorage,captured);let stopped=false,epoch=0;
    const pull=()=>{const ticket=++epoch;void selectedTopicAuthority(()=>api.topics(communityId),()=>api.archivedTopics(communityId),selectedTopicId).then(({page,all,selected})=>{if(stopped||ticket!==epoch)return;reconcileGroupDrafts({owner:captured.owner,community:captured.community},all.map(topic=>topic.topicId));setTopicPageState({key:communityId,value:page});setCanManageChannels(page.canManageChannels);if(selected)setThread(selected);else{if(scopeLeaseValid(sessionStorage,captured,capturedLease))revokeGroupDrafts(captured);viewKeyRef.current="";selectionEpoch.current++;clearLog();setReplyTo(null);setEditing(null);setThread("list");setError("Доступ к каналу изменился");}}).catch(error=>{if(stopped||ticket!==epoch)return;if(error instanceof Error&&["401","403","404"].includes(error.message)){revokeGroupDrafts(error.message==="401"?{owner:captured.owner}:{owner:captured.owner,community:captured.community});viewKeyRef.current="";selectionEpoch.current++;clearLog();setReplyTo(null);setEditing(null);setThread("list");setTopicPageState({key:communityId,value:null});setError("Доступ к каналу изменился");}});};
    pull();const timer=window.setInterval(pull,4000);return()=>{stopped=true;epoch++;window.clearInterval(timer);};
  },[communityId,selectedTopicId,app.session?.user?.userId]);
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
      undefined, chat.kind === "group" && thread !== "list" ? thread.topicId ?? undefined : undefined).catch(() => undefined);
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
      throw reason;
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
    const originalDraft = draft;
    const wire = groupWireText(draft, context, !!editing);
    if (!wire.valid) { setError(wire.error); return; }
    const body = wire.body;
    const sentDraftEpoch = currentDraftEpoch(sessionStorage,key);
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
      if(draftScope&&!scopeLeaseValid(sessionStorage,draftScope,draftLease))return;
      markLogChanged(key);
      updateLog(key, [message]);
      let acknowledgedDraft=false;
      setDrafts(current => {
        const unchanged = draftEpochMatches(currentDraftEpoch(sessionStorage,key),sentDraftEpoch);
        acknowledgedDraft = unchanged && current[key] === originalDraft;
        const cleared = clearSentGroupDraft(current, key, originalDraft, unchanged);
        return target && previousDraft !== null && unchanged ? { ...cleared, [key]: previousDraft } : cleared;
      });
      if (target && editReturnDraft.current?.key === key && editReturnDraft.current.messageId === target.messageId)
        editReturnDraft.current = null;
      if (!target && acknowledgedDraft) setDiscussionContexts(current=>clearSentDiscussionContext(current,key,context));
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
  if (!app.session?.authenticated) return <section className="page"><div className="card empty"><h1>Группа</h1><p>Войдите в аккаунт, чтобы открыть группу, разделы чата и голосования.</p><Link className="btn primary" to="/settings?section=account">Открыть настройки аккаунта</Link></div></section>;
  return (
    <section className="page">
      <Head title="Группа" text={home ? `${home.name}${home.groupName ? " · " + home.groupName : ""}` : error || "Одногруппники и чат"}>{home && <Avatar kind="group" id={home.communityId} name={home.groupName || home.name} />}</Head>
      {error && <div className="banner row" role="alert"><span>{error}</span><button className="btn quiet" type="button" onClick={()=>setError("")}>Закрыть сообщение</button></div>}
      {error && <button className="btn" type="button" onClick={() => setReloadEpoch(value => value + 1)}>Повторить загрузку группы</button>}
      {home && desk && (desk.mine.length > 0 || desk.headman) && <GroupAdmin communityId={home.communityId} groupName={home.groupName || home.name} classmates={home.classmates} desk={desk} onChange={updateDesk} onReload={async () => { const [loaded, office] = await Promise.all([api.groupHome(home.communityId), api.groupDesk(home.communityId)]); setHome(loaded); updateDesk(office); }} onError={setError} />}
      {home&&<GroupObligations key={JSON.stringify([studyOwner(app),home.communityId])} communityId={home.communityId} onOpen={(topic,target,freshBoard)=>{if(!topic&&freshBoard){setBoard(freshBoard);setGlobalBoardOpen(true);setObligationTarget({...target});return;}if(!topic)return;selectionEpoch.current++;clearLog();setChat(home.groupChat);setThread(topic);setFocusChat(true);setObligationTarget({...target});}}/>}
      {home && !board && !votesOff && <p className="muted" role="status">Загрузка голосований…</p>}
      {home && votesOff && <div className="banner row" role="status"><span>{board ? "Голосования не обновились. Показана предыдущая доска." : "Голосования сейчас не открылись. Чат группы на месте."}</span>
        <button className="btn" type="button" onClick={() => setVotesRetry(value => value + 1)}>Повторить</button></div>}
      {home && board && <details className="group-board-panel" open={globalBoardOpen} onToggle={event=>setGlobalBoardOpen(event.currentTarget.open)}>
        <summary>Голосования группы · {board.ballots.length}</summary>
        {globalBoardOpen&&obligationTarget?.source===GLOBAL_BALLOTS&&<GroupObjectFocus key={obligationTarget.id} id={`obligation-ballot-general-${obligationTarget.id}`}/>}
        <BallotBoardView targetId={obligationTarget?.source===GLOBAL_BALLOTS?obligationTarget.id:undefined} communityId={home.communityId} board={board} classmates={home.classmates} roles={desk?.roles ?? []} onChange={setBoard} onError={setError} />
      </details>}
      {home && (
        <div className={"grid-2 split" + (focusChat ? " focus" : "")}>
          <div className="people split-list">
            <label className="field">Поиск участника
              <input type="search" value={memberSearch} onChange={event => setMemberSearch(event.target.value)}
                placeholder="Имя или логин" />
            </label>
            <button className="person" type="button" onClick={() => { selectionEpoch.current += 1; clearLog(); setChat(home.groupChat); setThread("list"); setFocusChat(true); }}><Avatar kind="group" id={home.communityId} name={home.groupName || home.name} /><span className="person-main"><b>Чат группы</b><span className="muted">Разделы и общий поток</span></span>{home.groupChat.unread > 0 && <span className="chip" aria-label={unreadBadgeDescription(home.groupChat.unread)}>{unreadBadgeText(home.groupChat.unread)}</span>}</button>
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
                  <TopicMark topic={topic} />
                  <span className="group-quick-topic-main">
                    <span className="group-quick-topic-top"><b>{topic.title}</b><span className="muted">{groupTopicWhen(topic.lastAt)}</span></span>
                    <span className="group-quick-topic-bottom"><span className="muted">{topicPreview(topic)}</span>
                      {topic.unread > 0 && <span className="chip" aria-label={unreadBadgeDescription(topic.unread)}>{unreadBadgeText(topic.unread)}</span>}</span>
                  </span>
                </button>;
              })}
            </div>}
            <label className="check"><input type="checkbox" checked={leadersOnly} onChange={event=>setLeadersOnly(event.target.checked)}/>Только старосты и кураторы приложения</label>{home.classmates.filter(person => (!leadersOnly||person.role==="headman"||person.role==="curator") && matchesBrowseQuery(memberSearch, person.displayName, person.username)).map(person => (
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
                <Avatar kind="user" id={person.userId} name={person.displayName || person.username} /><span className="person-main"><b>{person.displayName || person.username}</b><span className="muted">@{person.username}</span></span>
                <span className="row">{[person.role === "headman" ? "Староста" : person.role === "curator" ? "Куратор" : "Участник", ...titlesOf(desk, person.userId)].map(title => <span className="chip" key={title}>{title}</span>)}</span>
              </button>
            ))}
            {(!!memberSearch.trim()||leadersOnly) && !home.classmates.some(person => (!leadersOnly||person.role==="headman"||person.role==="curator")&&matchesBrowseQuery(memberSearch, person.displayName, person.username)) &&
              <div className="empty">Участник не найден <button className="btn" type="button" onClick={() => {setMemberSearch("");setLeadersOnly(false);}}>Сбросить поиск</button></div>}
            {home.directs.filter(item=>noteSearch(memberSearch,item.title,item.lastBody||"")).map(item => <button className="person" key={item.conversationId} type="button" onClick={() => { selectionEpoch.current += 1; clearLog(); setChat(item); setThread("list"); setFocusChat(true); }}><Avatar kind="user" id={item.peerUserId} name={item.title} /><span className="person-main"><b>{item.title}</b><span className="muted preview-line">{item.lastBody || "Нет сообщений"}</span></span></button>)}
          </div>
          <section className="card chat split-detail">
            <button className="btn back-only" type="button" onClick={() => setFocusChat(false)}>К списку</button>
            {!chat && <p className="muted">{error || "Чат открывается…"}</p>}
            {chat?.kind === "group" && thread === "list" && <GroupTopics key={home.communityId} communityId={home.communityId} groupName={home.groupName} onOpen={(next, allowed) => { selectionEpoch.current += 1; clearLog(); setCanManageChannels(allowed); setThread(next); }} onError={setError} />}
            {chat?.kind === "group" && thread !== "list" && !isChatChannel(thread) && <>
              {obligationTarget&&obligationTarget.source===thread.topicId&&<GroupObjectFocus key={JSON.stringify(obligationTarget)} id={`obligation-${obligationTarget.kind}-${obligationTarget.kind==="ballot"?obligationTarget.source+"-":""}${obligationTarget.id}`}/>}
              <div className="row">
                <button className="btn" type="button" onClick={() => { selectionEpoch.current += 1; setThread("list"); }}>Все разделы</button>
                <h2>{thread.icon} {thread.title}</h2>
                <button className="btn" type="button" disabled={!nextUnread || nextUnreadBusy} onClick={() => void openNextUnread()}
                  title={nextUnread ? `Открыть: ${nextUnread.title}` : "Непрочитанных каналов нет"}>{nextUnreadBusy ? "Проверяем…" : "Следующий непрочитанный"}</button>
              </div>
              {thread.description && <p className="muted">{thread.description}</p>}
              {activeBallotTopicId && channelBoard && <BallotBoardView key={activeBallotTopicId} communityId={home.communityId} board={channelBoard}
                classmates={home.classmates} roles={desk?.roles ?? []} topicId={activeBallotTopicId} title={thread.title} targetId={obligationTarget?.source===thread.topicId?obligationTarget.id:undefined}
                canCreate={canCreateBallot(thread, canManageChannels)} readOnly={!!thread.archived || !!thread.permissions && !thread.permissions.includes("vote")}
                onChange={value => setChannelBoardState({ topicId: activeBallotTopicId, value, failed: false })} onError={setError} />}
              {channelBoardFailed && <div className="banner row" role="status"><span>{channelBoard ? "Голосования не обновились. Показана предыдущая доска." : "Голосования не открылись."}</span>
                <button className="btn" type="button" onClick={() => setChannelVotesRetry(value => value + 1)}>Повторить</button></div>}
              {thread.kind === "ballots" && !channelBoard && !channelBoardFailed && <p className="muted" role="status">Загрузка голосований…</p>}
              <SpecializedChannel communityId={home.communityId} conversationId={home.groupChat.conversationId} groupName={home.groupName} topic={thread} onError={setError} targetId={obligationTarget?.source===thread.topicId?obligationTarget.id:undefined}/>
            </>}
            {chat && (chat.kind !== "group" || (thread !== "list" && isChatChannel(thread))) && <>
            <div className="row">
              {chat?.kind === "group" && <button className="btn" type="button" onClick={() => { selectionEpoch.current += 1; clearLog(); setThread("list"); }}>Все разделы</button>}
              <Avatar kind={chat.kind === "group" ? "group" : "user"} id={chat.kind === "group" ? home.communityId : chat.peerUserId} name={chat.kind === "group" ? home.groupName || home.name : chat.title} />
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
            {chat.kind === "group" && thread !== "list" && thread.subject && <SubjectChannelContext key={`${home.communityId}:${thread.topicId}`} communityId={home.communityId} groupName={home.groupName} topic={thread} onError={setError} />}
            <button className="btn message-browse-toggle" type="button" aria-expanded={messageBrowseOpen}
              onClick={() => setMessageBrowseOpen(value => !value)}>
              {messageBrowseOpen ? "Скрыть поиск" : messageFiltered ? `Поиск и фильтры · ${visibleLog.length} найдено` : "Поиск и фильтры"}
            </button>
            {messageFiltered&&<div className="row"><span className="muted">Совпадений: {visibleLog.length} · загружено: {log.length}{hasOlder?" · есть ранние сообщения":""}</span><button className="btn quiet" disabled={!visibleLog.length} onClick={()=>moveMatch(-1)}>Предыдущее совпадение</button><button className="btn quiet" disabled={!visibleLog.length} onClick={()=>moveMatch(1)}>Следующее совпадение</button></div>}
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
                const archived = chat.kind === "group" && thread !== "list" && !!thread.archived;
                const moderate = chat.kind === "group" && thread !== "list" && !archived && topicAction(thread,"moderate",desk);
                const actions = [...holdActions(kind, mine, !!message.deleted, menu === message.messageId),
                  ...(menu === message.messageId && !mine && !message.deleted && moderate ? ["delete"] : []),
                  ...(menu === message.messageId && canCopyMessageText(message) ? ["copy"] : [])]
                  .filter(action => (!archived || !["reply","edit","delete","reaction"].includes(action)) && (canWrite || action !== "reply" && action !== "edit"));
                const download = chat && groupMediaDownload(chat.conversationId, message);
                const previous = visibleLog[index - 1];
                const newDay = !previous || messageDayKey(previous.createdAt) !== messageDayKey(message.createdAt);
                const grouped = !messageFiltered && !!previous && sameMessageCluster(previous, message);
                return (
                  <Fragment key={message.messageId}>
                  {newDay && <div className="message-day" role="separator">{groupMessageDay(message.createdAt)}</div>}
                  <article id={`group-message-${message.messageId}`} tabIndex={-1} data-hold={kind} className={"bubble" + (mine ? " mine" : " chat-incoming") + (grouped ? " grouped" : "") + (kind === "circle" && !message.deleted ? " round" : "")}
                    aria-label={`Сообщение: ${message.senderName}`}
                    onPointerDown={event => { heldOpen.current = false; if (holdTimer.current) window.clearTimeout(holdTimer.current); if (event.target instanceof Element && event.target.closest(".actions, .react, .react-chips, .group-inline-media, .group-media-download, .message-action-toggle")) return; holdTimer.current = window.setTimeout(() => { holdTimer.current = 0; heldOpen.current = true; setMenu(message.messageId); }, 450); }}
                    onPointerUp={event => { if (holdTimer.current) window.clearTimeout(holdTimer.current); if (heldOpen.current && !(event.target instanceof Element && event.target.closest(".actions, .group-inline-media, .group-media-download, .message-action-toggle"))) event.preventDefault(); }}
                    onPointerLeave={() => { if (holdTimer.current) window.clearTimeout(holdTimer.current); }}
                    onClickCapture={event => { if (event.target instanceof Element && event.target.closest(".actions, .group-inline-media, .message-action-toggle")) return; if (event.target instanceof Element && event.target.closest(".group-media-download") && !heldOpen.current) return; if (heldOpen.current || menu === message.messageId) { event.preventDefault(); event.stopPropagation(); } }}>
                    {!mine && !grouped && <Avatar kind="user" id={message.senderId} name={message.senderName} className="message-avatar" />}
                    {!mine && !grouped && <b>{message.senderName}</b>}
                    {message.replyTo && <button type="button" className="btn quiet quote" onClick={() => setError(revealQuote(log,message.replyTo!,"group-message-"))}>↳ {log.find(item => item.messageId === message.replyTo)?.body || "Сообщение"}</button>}
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
            {context && <div className="compose-context"><span>{context}</span><button className="btn quiet" type="button" onClick={()=>setDiscussionContexts(current=>({...current,[viewKey]:""}))}>Убрать связь</button></div>}
            <input ref={fileRef} type="file" hidden aria-label="Файл" onChange={event => void onPicked(event)} />
            {chat.kind === "group" && thread !== "list" && !canComposeChannel(thread)
              ? <p className="muted channel-readonly">Публикация в этом канале недоступна для вашей роли или канал находится в архиве.</p>
              : <GroupComposer key={viewKey} wireContext={context} draft={draft} editing={!!editing} replyTo={!!replyTo}
              contextText={editing?.body ?? (replyTo ? (repliedMessage ? `${repliedMessage.senderName}: ${repliedMessage.body || "Сообщение"}` : "Сообщение") : "")}
              allowMedia={chat.kind !== "group" || thread !== "list" && (!thread.permissions || thread.permissions.includes("media"))}
              onPoll={chat.kind === "group" && topicPage?.topics.some(topic=>canCreateBallot(topic,canManageChannels)) ? () => { const topic=topicPage.topics.find(topic=>canCreateBallot(topic,canManageChannels)); if(topic){ selectionEpoch.current += 1; clearLog(); setThread(topic); } } : undefined}
              onLesson={chat.kind === "group" && groupContext.nextLesson ? () => { const lesson=groupContext.nextLesson!; setDiscussionContexts(current=>({...current,[viewKey]:`Пара: ${lesson.subject} · ${isoDay(lesson.date)} · ${lesson.time}${lesson.room ? ` · ${lesson.room}` : ""}`})); } : undefined}
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
  const app=useApp();const location=useLocation();const back={returnTo:location.pathname+location.search,owner:app.session?.user?.userId||"guest"};
  return (
    <nav className="stack legal-links" aria-label="Документы">
      <Link className="btn" to="/legal/agreement" state={back}><Icon name="file" size={18} />Пользовательское соглашение</Link>
      <Link className="btn" to="/legal/policy" state={back}><Icon name="shield" size={18} />Политика обработки персональных данных</Link>
    </nav>
  );
}

export function LegalPage({ id }: { id: LegalId }) {
  const doc = legalDocument(id);
  const [query,setQuery]=useState("");const[scale,setScale]=useState(1);
  const paragraphs=doc.body.split(/\n\n+/);
  const matches=paragraphs.map((text,index)=>({text,index})).filter(row=>noteSearch(query,row.text));
  const app=useApp();const location=useLocation();const back=legalReturn(location.state,app.session?.user?.userId||"guest");
  return (
    <section className="page legal">
      <h1>{doc.title}</h1>
      <SearchField label="Найти в документе" value={query} onChange={setQuery}/><div className="row"><button className="btn" disabled={scale<=.9} onClick={()=>setScale(value=>Math.max(.9,value-.1))}>Уменьшить текст</button><button className="btn" disabled={scale>=1.6} onClick={()=>setScale(value=>Math.min(1.6,value+.1))}>Увеличить текст</button><Link className="btn quiet" to={`/legal/${id==="agreement"?"policy":"agreement"}`} state={location.state}>{id==="agreement"?"Политика данных":"Пользовательское соглашение"}</Link></div>{query&&<p role="status">Найдено абзацев: {matches.length} из {paragraphs.length}</p>}{query&&!matches.length&&<FilterEmpty onReset={()=>setQuery("")}/>}
      <div style={{fontSize:`${scale}em`}}>{matches.map(({text,index}) => <p key={index}>{text}</p>)}</div><button className="btn quiet" onClick={()=>window.scrollTo({top:0})}>К началу документа</button>
      <p><Link to={back}>Вернуться к настройкам</Link></p>
    </section>
  );
}

export function SettingsPage() {
  const app=useApp();return <SettingsContent key={JSON.stringify([app.session?.authenticated,app.session?.user?.userId,app.session?.familyId])}/>;
}
function SettingsContent() {
  const app = useApp();
  const [supportState,setSupportState]=useState<SupportPendingState>({dirty:false,busy:false});
  const location = useLocation();
  const navigate = useNavigate();
  const requestedSection = new URLSearchParams(location.search).get("section");
  const section = requestedSection === "account" || requestedSection === "study" || requestedSection === "appearance" || requestedSection === "notifications" || requestedSection === "data" || requestedSection === "help"
    ? requestedSection : null;
  const owner=app.session?.user?.userId||"guest";
  const [username, setUsername] = useState(()=>accountDraft(owner).username);
  const [password, setPassword] = useState("");
  const [confirmation,setConfirmation]=useState("");
  const [display, setDisplay] = useState(()=>accountDraft(owner).display);
  const [mode, setMode] = useState<"login" | "register">(()=>accountDraft(owner).mode);
  useEffect(()=>{setPassword("");setConfirmation("");},[mode]);
  const [accepted, setAccepted] = useState(()=>accountDraft(owner).accepted);
  useEffect(()=>{rememberAccountDraft(owner,{username,display,mode,accepted});},[owner,username,display,mode,accepted]);
  const legalState={owner,returnTo:location.pathname+location.search};
  const [categoryQuery,setCategoryQuery]=useBrowseValue("settings-query","");
  const [groupQuery,setGroupQuery]=useState("");
  const [logoutBusy,setLogoutBusy]=useState(false);const[logoutNote,setLogoutNote]=useState("");const logoutPending=useRef(false);const logoutAck=useRef(false);
  async function logout(){if(logoutPending.current)return;if(!logoutAck.current&&!window.confirm(`Выйти из аккаунта «${app.session?.user?.displayName||app.session?.user?.username||""}» на этом устройстве? Локальные данные останутся.`))return;logoutPending.current=true;setLogoutBusy(true);setLogoutNote("");try{if(!logoutAck.current){await api.logout();logoutAck.current=true;}await app.refreshSession();}catch{setLogoutNote(logoutAck.current?"Выход подтверждён. Не удалось обновить экран; повторите проверку аккаунта.":"Выйти не удалось. Аккаунт и данные сохранены; можно повторить.");}finally{logoutPending.current=false;setLogoutBusy(false);}}
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const yandex = app.session?.capabilities.yandex === true;
  const vk = app.session?.capabilities.vk === true;
  const chosenGroupName = app.catalog?.groups.find(group => group.id === app.groupId)?.name;
  const sections = [
    { id: "account" as const, icon: "users" as const, title: "Аккаунт", summary: app.session?.authenticated ? (app.session.user?.displayName || app.session.user?.username || "Вход выполнен") : "Гостевой режим" },
    { id: "study" as const, icon: "calendar" as const, title: "Учёба", summary: chosenGroupName || "Группа не выбрана" },
    { id: "appearance" as const, icon: "sun" as const, title: "Оформление", summary: app.theme === "system" ? "Как в системе" : app.theme === "dark" ? "Тёмная тема" : "Светлая тема" },
    { id: "notifications" as const, icon: "calendar" as const, title: "Уведомления", summary: readReminders().enabled ? "Напоминания включены" : "Выключены" },
    { id: "data" as const, icon: "refresh" as const, title: "Данные и синхронизация", summary: app.session?.authenticated ? "Аккаунт и локальная копия" : "Копия на устройстве" },
    { id: "help" as const, icon: "file" as const, title: "Помощь и обновления", summary: "Поддержка, версия и документы" },
  ];
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
    if (busy) return;
    if(mode==="register"&&(password!==confirmation||!scalarInput(password,12,128,false))){setError("Проверьте пароль и его подтверждение.");return;}
    setBusy(true);
    setError("");
    if (mode === "register" && !accepted) {
      setError("Примите пользовательское соглашение и политику обработки персональных данных.");
      setBusy(false);
      return;
    }
    try {
      if (mode === "register") await api.register(username, password, display || username);
      await api.login(username, password);
      await app.refreshSession();
      setPassword("");
      const destination=(location.state as {authReturnTo?:unknown}|null)?.authReturnTo;if(destination)navigate(safeAppReturn(destination));
    } catch (reason) {
      setError(errorHint(reason, mode === "login" ? "Не удалось связаться с сервером входа. Проверьте подключение и повторите." : "Не удалось зарегистрироваться. Введённые поля сохранены."));
    } finally { setBusy(false); }
  }
  return (
    <section className="page settings-page">
      <Head title={section ? sections.find(item => item.id === section)?.title || "Настройки" : "Настройки"}
        text={section ? undefined : "Неофициальное приложение для студентов БГТУ «Военмех»."}>
        {section && <button className="btn" type="button" onClick={() => navigate("/settings")}>Все настройки</button>}
      </Head>
      {!section&&<label className="field">Найти настройки<input type="search" value={categoryQuery} onChange={event=>setCategoryQuery(event.target.value)} placeholder="Например, пароль или подгруппа"/></label>}
      {!section&&categoryQuery&&<div className="row"><span className="muted">Найдено: {sections.filter(item=>matchesWords(categoryQuery,item.title,settingsAliases[item.id])).length}</span><button className="btn quiet" type="button" onClick={()=>setCategoryQuery("")}>Сбросить поиск</button></div>}
      <div className={section ? "settings-space" : ""}>
      {section && <nav className="settings-rail" aria-label="Категории настроек">{sections.map(item=><button type="button" className={section===item.id?"settings-overview-row active":"settings-overview-row"} key={item.id} onClick={()=>navigate(`/settings?section=${item.id}`)} aria-current={section===item.id?"page":undefined}><Icon name={item.icon} /><span className="settings-overview-copy"><strong>{item.title}</strong><span>{item.summary}</span></span></button>)}</nav>}
      {!section ? <div className="settings-overview" aria-label="Разделы настроек">
        {[[0], [1, 2, 3], [4, 5]].map((indices, groupIndex) => <div className="settings-category" key={groupIndex}>
          <h2 className="settings-overview-title">{["Профиль", "Основное", "Сервис"][groupIndex]}</h2>
          <div className="card settings-category-card">{indices.map(index => {
            const item = sections[index];
            if(!matchesWords(categoryQuery,item.title,settingsAliases[item.id]))return null;
            return <button className="settings-overview-row" key={item.id} type="button" onClick={() => navigate(`/settings?section=${item.id}`)}>
              <span className="settings-overview-icon"><Icon name={item.icon} size={20} /></span>
              <span className="settings-overview-copy"><strong>{item.title}</strong><span>{item.summary}</span></span>
              <Icon name="right" size={20} />
            </button>;
          })}</div>
        </div>)}
      </div> : <div className="stack settings-detail">
        {section === "study" && <article className="card">
          <h2>Группа</h2>
          <SearchField label="Найти учебную группу" value={groupQuery} onChange={setGroupQuery}/>          <label className="field">Моя группа
            <select value={app.groupId} onChange={event => app.setGroupId(event.target.value)} aria-label="Моя группа">
              <option value="">Не выбрана</option>
              {(app.catalog?.groups || []).filter(group=>group.id===app.groupId||noteSearch(groupQuery,group.name,group.id)).sort((a,b)=>Number(b.id===app.groupId)-Number(a.id===app.groupId)).map(group => <option key={group.id} value={group.id}>{group.name}{group.id===app.groupId?" · выбрана":""}</option>)}
            </select>
          </label>
          {(app.catalog?.groups || []).length === 0 && <p className="muted">Список групп появится, когда расписание откроется. Пока можно пользоваться сохранённой копией.</p>}
          <details><summary>Дополнительно</summary><label className="switch-row"><span>Инвертировать чётность</span><input type="checkbox" role="switch" checked={app.invert} onChange={event => app.setInvert(event.target.checked)} /></label></details>
        </article>}
        {section === "study" && <StudyExtras />}
        {section === "study" && <SubgroupPreview key={studyOwner(app)}/>}
        {section === "notifications" && <NotificationSettings />}
        {section === "data" && <DataSettings />}
        {section === "appearance" && <article className="card">
          <h2>Оформление</h2>
          <div className="seg">
            <button type="button" className={app.theme === "system" ? "active" : ""} aria-pressed={app.theme === "system"} onClick={() => app.setTheme("system")}>Системная</button>
            <button type="button" className={app.theme === "light" ? "active" : ""} aria-pressed={app.theme === "light"} onClick={() => app.setTheme("light")}><Icon name="sun" size={16} />Светлая</button>
            <button type="button" className={app.theme === "dark" ? "active" : ""} aria-pressed={app.theme === "dark"} onClick={() => app.setTheme("dark")}><Icon name="moon" size={16} />Тёмная</button>
          </div>
        </article>}
        {section === "appearance" && <article className="card stack"><label className="switch-row"><span>Анимации</span><input type="checkbox" role="switch" checked={app.animations} onChange={event => app.setAnimations(event.target.checked)} /></label><p className="muted">Системное уменьшение движения имеет приоритет.</p><div className="lesson"><span className="muted">08:30–10:05 · Лекция · 312</span><h2>Предпросмотр карточки пары</h2><div className="row"><button className="btn primary" type="button">Открыть карту</button><button className="btn" type="button">Домашка</button></div></div></article>}
        {section === "account" && app.session?.authenticated && <><article className="card stack"><h2>Фото профиля</h2><AvatarEditor kind="user" id={app.session.user!.userId} name={app.session.user!.displayName || app.session.user!.username} /></article><AccountDetails /></>}
        {section === "account" && <article className="card">
          <h2>Аккаунт</h2>
          {app.sessionStatus && <div className="row"><p className="muted" role="status">{app.sessionStatus}</p>{app.sessionLoaded && <button className="btn" type="button" onClick={() => void app.refreshSession()}>Повторить загрузку аккаунта</button>}</div>}
          {app.session?.authenticated ? (
            <div className="stack">
              <div className="row">
                <span>{app.session.user?.displayName || app.session.user?.username}</span>
                <button className="btn" type="button" disabled={logoutBusy} onClick={()=>void logout()}>{logoutBusy?"Подождите…":logoutAck.current?"Проверить состояние аккаунта":"Выйти"}</button>
              </div>
              {logoutNote&&<p role="status">{logoutNote}</p>}
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
              {app.session?.capabilities.password !== false && <><label className="field">Логин<input value={username} onChange={event => setUsername(event.target.value)} autoComplete="username" /></label>
              <SecretInput label="Пароль" value={password} onChange={setPassword} autoComplete={mode==="register"?"new-password":"current-password"}/>{mode==="register"&&<><SecretInput label="Повторите пароль" value={confirmation} onChange={setConfirmation} autoComplete="new-password"/><ul className="muted"><li>{[...password].length>=12&&[...password].length<=128?"✓":"○"} От 12 до 128 символов</li><li>{scalarInput(password,0,100000,false)?"✓":"○"} Без недопустимых символов</li><li>{password&&password===confirmation?"✓":"○"} Подтверждение совпадает</li></ul></>}</>}
              {mode === "register" && <label className="field">Имя<input value={display} onChange={event => setDisplay(event.target.value)} /></label>}
              {mode === "register" && (
                <label className="check">
                  <input type="checkbox" checked={accepted} onChange={event => setAccepted(event.target.checked)} />
                  <span className="check-marks" aria-hidden="true"><Icon name="file" size={16} /><Icon name="shield" size={16} /></span>
                  <span>Я принимаю <Link to="/legal/agreement" state={legalState}>пользовательское соглашение</Link> и <Link to="/legal/policy" state={legalState}>политику обработки персональных данных</Link>.</span>
                </label>
              )}
              {error && <div className="banner">{error}</div>}
              <LegalLinks />
              {app.session?.capabilities.password !== false && <button className="btn primary" type="submit" disabled={busy || (mode === "register" && (!accepted || password!==confirmation || !scalarInput(password,12,128,false)))}>{busy ? "Входим…" : mode === "login" ? "Войти" : "Создать аккаунт"}</button>}
            </form>
          )}
        </article>}
        {section === "account" && <PasswordRecovery login={username} onLogin={value => { setUsername(value); setMode("login"); setPassword(""); document.querySelector<HTMLInputElement>("input[autocomplete=username]")?.focus(); }} key={JSON.stringify([app.session?.authenticated, app.session?.user?.userId, app.session?.familyId])} />}
        {section === "help" && <>
          <SupportCard onState={setSupportState}/>
          <SupportDiagnostics key={studyOwner(app)}/>
          <UpdateSettings supportState={supportState}/>
          <article className="card"><h2>Документы</h2><LegalLinks /></article>
        </>}
      </div>}
      </div>
    </section>
  );
}

function FollowUp({ onSend, onState }: { onSend: (text: string, photos: File[], logs: File[]) => Promise<boolean>; onState?:(dirty:boolean,busy:boolean)=>void }) {
  const [text, setText] = useState("");
  const [photos, setPhotos] = useState<File[]>([]);
  const [logs, setLogs] = useState<File[]>([]);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  useEffect(()=>{onState?.(!!text.trim()||photos.length>0||logs.length>0,busy);},[text,photos,logs,busy]);
  return (
    <form className="stack" onSubmit={async event => {
      event.preventDefault();
      if (pending.current) return;
      const value = text.trim();
      if (!value) return;
      const files = supportFiles(photos, logs);
      if (files.error) { setNote(files.error); return; }
      pending.current = true; setBusy(true);
      setNote("");
      const saved = await sendSupportDraft(() => onSend(value, photos, logs), () => { setText(""); setPhotos([]); setLogs([]); });
      if (!saved) setNote("Ответ не отправлен. Текст и вложения сохранены — можно повторить.");
      pending.current = false; setBusy(false);
    }}>
      <fieldset className="stack" disabled={busy}>
      <label className="field">Уточнение<textarea value={text} onChange={event => setText(event.target.value)} maxLength={4000} rows={3} /></label>
      <Attach photos={photos} logs={logs} onPhotos={setPhotos} onLogs={setLogs} onNote={setNote} />
      {note && <p className="banner">{note}</p>}
      <button className="btn" type="submit">{busy ? "Отправляем…" : "Ответить"}</button>
      </fieldset>
    </form>
  );
}

function Attach({ photos, logs, onPhotos, onLogs, onNote }: { photos: File[]; logs: File[]; onPhotos: (files: File[]) => void; onLogs: (files: File[]) => void; onNote: (note: string) => void }) {
  function add(kind: "photo" | "log", list: FileList | null, input: HTMLInputElement) {
    const next = [...(kind === "photo" ? photos : logs), ...Array.from(list ?? [])];
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

function SupportCard({onState}:{onState:(state:SupportPendingState)=>void}) {
  const app = useApp();
  return <SupportContent onState={onState} key={JSON.stringify([app.session?.authenticated, app.session?.user?.userId, app.session?.familyId])} />;
}

function SupportContent({onState}:{onState:(state:SupportPendingState)=>void}) {
  const app = useApp();
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [photos, setPhotos] = useState<File[]>([]);
  const [logs, setLogs] = useState<File[]>([]);
  const [threads, setThreads] = useState<api.SupportThread[]>([]);
  const [historyError, setHistoryError] = useState("");
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyRetry, setHistoryRetry] = useState(0);
  const [query,setQuery]=useState("");const[selectedThread,setSelectedThread]=useState<string|null>(null);
  const [replyDirty,setReplyDirty]=useState(false);const[replyBusy,setReplyBusy]=useState(false);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(()=>{onState({dirty:!!subject||!!body||photos.length>0||logs.length>0||replyDirty,busy:busy||replyBusy});},[subject,body,photos,logs,replyDirty,busy,replyBusy,onState]);
  useEffect(()=>()=>onState({dirty:false,busy:false}),[onState]);
  const pending = useRef(false);
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => {
    if (!app.session?.authenticated) { setThreads([]); return; }
    let stopped = false;
    setHistoryLoading(true); setHistoryError("");
    void api.supportList().then(rows => { if (!stopped) setThreads(rows); }).catch(() => { if (!stopped) setHistoryError("История обращений не загрузилась. Ранее загруженные ответы сохранены."); }).finally(() => { if (!stopped) setHistoryLoading(false); });
    return () => { stopped = true; };
  }, [app.session?.authenticated, historyRetry]);
  async function send(event: FormEvent) {
    event.preventDefault();
    if (pending.current) return;
    setNote("");
    const draft = supportDraft(!!app.session?.authenticated, subject, body);
    if (draft.error || !draft.subject || !draft.body) { setNote(draft.error || "Опишите тему и что случилось."); return; }
    const files = supportFiles(photos, logs);
    if (files.error) { setNote(files.error); return; }
    pending.current = true; setBusy(true);
    try {
      const opened = await api.supportOpen(draft.subject, draft.body, photos, logs);
      if (!alive.current) return;
      setThreads(list => [opened, ...list.filter(item => item.id !== opened.id)]);
      setSelectedThread(opened.id);
      setSubject("");
      setBody("");
      setPhotos([]);
      setLogs([]);
    } catch (error) {
      if (alive.current) setNote(error instanceof Error && error.message ? error.message : "Не удалось отправить сообщение. Черновик сохранён.");
    } finally { pending.current = false; if (alive.current) setBusy(false); }
  }
  async function follow(id: string, text: string, nextPhotos: File[], nextLogs: File[]) {
    const draft = supportDraft(!!app.session?.authenticated, "уточнение", text);
    if (draft.error || !draft.body) { setNote(draft.error || "Опишите, что случилось."); return false; }
    const next = supportAppend([], "user", draft.body);
    if (next.length !== 1) return false;
    try {
      const updated = await api.supportReply(id, next[0].body, nextPhotos, nextLogs);
      if (!alive.current) return false;
      setThreads(list => list.map(item => item.id === updated.id ? updated : item));
      return true;
    } catch (error) {
      if (alive.current) setNote(error instanceof Error && error.message ? error.message : "Не удалось отправить сообщение.");
      return false;
    }
  }
  if(!app.session?.authenticated)return <article className="card stack"><h2>Поддержка</h2><p>Войдите, чтобы отправить обращение и прочитать ответ. Гостевое расписание и карты остаются доступны.</p><Link className="btn primary" to="/settings?section=account" state={{authReturnTo:"/settings?section=help"}}>Войти и вернуться к поддержке</Link></article>;
  return (
    <article className="card stack">
      <h2>Сообщить о баге</h2>
      <form className="stack" onSubmit={event => void send(event)}>
        <fieldset className="stack" disabled={busy}>
        {!app.session?.authenticated && <p>Войдите в аккаунт, чтобы отправить сообщение об ошибке и увидеть ответ. Расписание и карты остаются доступны без входа.</p>}
        <label className="field">Тема<input value={subject} onChange={event => setSubject(event.target.value)} maxLength={120} required={!!app.session?.authenticated} /></label>
        <label className="field">Что случилось<textarea value={body} onChange={event => setBody(event.target.value)} maxLength={4000} required={!!app.session?.authenticated} rows={4} /></label>
        <Attach photos={photos} logs={logs} onPhotos={setPhotos} onLogs={setLogs} onNote={setNote} />
        <button className="btn primary" type="submit">{busy ? "Отправляем…" : "Отправить"}</button>
        {(subject||body||photos.length>0||logs.length>0)&&<button className="btn quiet" type="button" onClick={()=>{if(window.confirm("Очистить тему, текст и выбранные вложения этого черновика?")){setSubject("");setBody("");setPhotos([]);setLogs([]);setNote("");}}}>Очистить черновик обращения</button>}
        </fieldset>
      </form>
      {note && <p className="banner">{note}</p>}
      {historyLoading && <p role="status">Загрузка истории обращений…</p>}
      {historyError && <div className="banner" role="alert">{historyError}<button className="btn" disabled={historyLoading} onClick={() => setHistoryRetry(value => value + 1)}>Повторить загрузку истории</button></div>}
      {!historyLoading && !historyError && app.session?.authenticated && threads.length === 0 && <p className="muted">Обращений пока нет.</p>}
      {threads.length>0&&<SearchField label="Найти обращение" value={query} onChange={setQuery}/>}
      {threads.filter(thread=>noteSearch(query,thread.subject,...thread.messages.map(line=>line.body))).map(thread=><button className={thread.id===selectedThread?"btn primary":"btn"} disabled={replyBusy} key={thread.id} onClick={()=>{if(replyDirty&&!window.confirm("Переключить обращение и отбросить несохранённое уточнение?"))return;setReplyDirty(false);setSelectedThread(thread.id);}}>{thread.subject} · сообщений: {thread.messages.length}</button>)}
      {threads.filter(thread=>thread.id===selectedThread).map(thread => (
        <div key={thread.id} className="stack">
          <h3>{thread.subject}</h3>
          {thread.messages.map((line, index) => <SupportLineView key={thread.id + index} line={line} />)}
          <FollowUp key={thread.id} onState={(dirty,busy)=>{setReplyDirty(dirty);setReplyBusy(busy);}} onSend={(text, nextPhotos, nextLogs) => follow(thread.id, text, nextPhotos, nextLogs)} />
        </div>
      ))}
    </article>
  );
}
