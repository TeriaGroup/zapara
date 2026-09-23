import { onWeek } from "./summary.ts";
import type { Lesson, Teacher, TeacherLesson } from "./types.ts";

export type TeacherRow = { id: string; name: string; detail: string; mine: boolean };

export type TeacherWeekRow = {
  time: string;
  subject: string;
  groups: string;
  room: string;
  parity: number;
  parityLabel: string;
  mine: boolean;
};

export type TeacherDay = { day: number; title: string; rows: TeacherWeekRow[] };

const days = ["", "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье"];

const parityLabels: Record<number, string> = {
  0: "Обе недели",
  1: "Нечётная неделя",
  2: "Чётная неделя",
};

/** Surname tokens before the first initial, then the initials the short form carries. */
export function sameTeacher(lecturerName: string, shortName: string): boolean {
  const { surname, initials: wanted } = parts(shortName);
  if (surname.length === 0) return false;
  const tokens = lecturerName.trim().split(/\s+/).filter(Boolean);
  if (tokens.length < surname.length) return false;
  for (let i = 0; i < surname.length; i++) {
    if (tokens[i].replace(/\.$/, "").toLocaleLowerCase("ru") !== surname[i].toLocaleLowerCase("ru")) return false;
  }
  const have = initialsOf(tokens.slice(surname.length));
  for (let i = 0; i < wanted.length; i++) {
    if (i >= have.length) return true;
    if (have[i].toLocaleLowerCase("ru") !== wanted[i].toLocaleLowerCase("ru")) return false;
  }
  return true;
}

export function myTeacherIds(catalog: Teacher[], lessons: Lesson[]): Set<string> {
  const shorts = [...new Set(lessons.flatMap(lesson => teacherParts(lesson.teacherRaw)))];
  const ids = new Set<string>();
  for (const lecturer of catalog) {
    if (shorts.some(short => sameTeacher(lecturer.name, short))) ids.add(lecturer.id);
  }
  for (const short of shorts) {
    if (!catalog.some(lecturer => sameTeacher(lecturer.name, short))) ids.add(groupTeacherId(short));
  }
  return ids;
}

export function teacherRows(catalog: Teacher[], lessons: Lesson[], query: string, onlyMine: boolean): { rows: TeacherRow[]; total: number } {
  const mine = myTeacherIds(catalog, lessons);
  const extra = [...mine].filter(id => id.startsWith("group:")).map(id => groupTeacher(id, lessons)).filter((row): row is Teacher => row !== null);
  const all = [...catalog, ...extra];
  const needle = query.trim().toLocaleLowerCase("ru");
  const rows = all.filter(teacher => {
    if (onlyMine && !mine.has(teacher.id)) return false;
    if (!needle) return true;
    const subjects = subjectsOf(teacher, lessons);
    return [teacher.name, teacher.kafedra, teacher.shortName, subjects].join(" ").toLocaleLowerCase("ru").includes(needle);
  }).map(teacher => ({
    id: teacher.id,
    name: teacher.name,
    detail: subjectsOf(teacher, lessons) || teacher.kafedra,
    mine: mine.has(teacher.id),
  }));
  return { rows, total: catalog.length + extra.length };
}

export function teacherCode(filter: number, invert: boolean): number {
  if (filter === 0) return 0;
  return invert ? (filter === 1 ? 2 : 1) : filter;
}

export function teacherWeek(lessons: TeacherLesson[], filter: number, myGroupId: string, myGroupName: string, invert = false): TeacherDay[] {
  const filtered = lessons.filter(lesson => onWeek(lesson.parity, filter));
  const grouped = new Map<number, TeacherLesson[]>();
  for (const lesson of filtered) {
    const list = grouped.get(lesson.dayOfWeek) || [];
    list.push(lesson);
    grouped.set(lesson.dayOfWeek, list);
  }
  return [...grouped.keys()].sort((a, b) => a - b).map(day => ({
    day,
    title: days[day] || `День ${day}`,
    rows: (grouped.get(day) || []).slice().sort((a, b) => a.timeStart.localeCompare(b.timeStart) || a.parity - b.parity).map(lesson => ({
      time: lesson.timeEnd ? `${lesson.timeStart}–${lesson.timeEnd}` : lesson.timeStart,
      subject: strip(lesson.disciplineRaw || lesson.subjectRaw || "", lesson.typeRaw || ""),
      groups: (lesson.groups || []).map(group => group.number).filter(Boolean).join(", "),
      room: roomOf(lesson),
      parity: lesson.parity,
      parityLabel: parityLabels[userParity(lesson.parity, invert)] || "Чётность не указана",
      mine: (lesson.groups || []).some(group => group.idGroup === myGroupId || group.number === myGroupId || group.number === myGroupName),
    })),
  })).filter(day => day.rows.length > 0);
}

export function lessonsOfGroupTeacher(lessons: Lesson[], name: string, groupId: string, groupName: string): TeacherLesson[] {
  return lessons.filter(lesson => teacherParts(lesson.teacherRaw).some(part => sameTeacher(name, part) || sameTeacher(part, name))).map(lesson => ({
    dayOfWeek: lesson.dayOfWeek,
    timeStart: lesson.timeStart,
    timeEnd: lesson.timeEnd,
    disciplineRaw: lesson.subjectRaw,
    classroomRaw: lesson.classroomRaw || "",
    roomRaw: lesson.roomRaw || "",
    buildingRaw: lesson.buildingRaw || "",
    typeRaw: lesson.typeRaw || "",
    parity: lesson.parity,
    subjectRaw: lesson.subjectRaw,
    groups: [{ idGroup: groupId, number: groupName || groupId }],
  }));
}

/** Stored week 1/2 swapped when the person inverted parity. 0 stays both weeks. */
function userParity(xmlParity: number, invert: boolean): number {
  if (!invert || xmlParity === 0) return xmlParity;
  if (xmlParity === 1) return 2;
  if (xmlParity === 2) return 1;
  return xmlParity;
}

function groupTeacherId(name: string): string {
  return "group:" + name.toLocaleLowerCase("ru").replace(/[.\s]+/g, " ").trim();
}

function groupTeacher(id: string, lessons: Lesson[]): Teacher | null {
  const name = lessons.flatMap(lesson => teacherParts(lesson.teacherRaw)).find(part => groupTeacherId(part) === id);
  return name ? { id, name, kafedra: "", shortName: name } : null;
}

function subjectsOf(teacher: Teacher, lessons: Lesson[]): string {
  const names = teacher.id.startsWith("group:") ? [teacher.name] : [teacher.name, teacher.shortName];
  const subjects = [...new Set(lessons.filter(lesson => teacherParts(lesson.teacherRaw).some(part => names.some(name => name && (sameTeacher(name, part) || sameTeacher(part, name)))))
    .map(lesson => strip(lesson.subjectRaw || "", lesson.typeRaw || ""))
    .filter(Boolean))].slice(0, 4);
  return subjects.join(" · ");
}

function teacherParts(raw: string | null | undefined): string[] {
  return (raw || "").split(";").map(part => part.trim()).filter(part => part && part !== "—" && part !== "-");
}

function parts(name: string): { surname: string[]; initials: string[] } {
  const tokens = name.trim().split(/\s+/).filter(Boolean);
  let split = 0;
  while (split < tokens.length && !isInitial(tokens[split]) && tokens[split].replace(/\./g, "").length > 2) split++;
  return { surname: tokens.slice(0, split).map(token => token.replace(/\.$/, "")), initials: initialsOf(tokens.slice(split)) };
}

function isInitial(token: string): boolean {
  return /(?:^|\s)(\p{L})\./u.test(token) || /^(\p{L}\.)+$/u.test(token);
}

function initialsOf(tokens: string[]): string[] {
  const initials: string[] = [];
  for (const token of tokens) {
    const marks = [...token.matchAll(/(\p{L})\./gu)].map(match => match[1]);
    if (marks.length) initials.push(...marks);
    else if (token.length) initials.push(token[0]);
  }
  return initials;
}

function strip(name: string, typeRaw: string): string {
  const subject = name.trim();
  const type = typeRaw.trim();
  if (type && subject.toLocaleLowerCase("ru").startsWith((type + " ").toLocaleLowerCase("ru"))) return subject.slice(type.length + 1).trim();
  return subject;
}

function roomOf(lesson: TeacherLesson): string {
  const classroom = (lesson.classroomRaw || "").trim();
  const room = (lesson.roomRaw || "").trim();
  if (/дистанционно/i.test(classroom) || /дистанционно/i.test(room)) return "дистанционно";
  const cleaned = classroom.replace(/;+\s*$/g, "").replaceAll("*", "").trim();
  const number = room || cleaned;
  if (!number || number === "—") return "—";
  const building = (lesson.buildingRaw || "").trim()
    || (classroom.includes("*") ? "УЛК" : /вц/i.test(classroom) ? "ВЦ" : "ГК");
  return `${number} ${building}`.trim();
}
