// Нормализация строк расписания для показа (#12). Тот же алгоритм — в Vograph.Core/Services/ScheduleText.cs;
// оба проверяются одними примерами из design/strings/schedule-cases.json. Ключи (subjectRaw) не меняются — только показ.
import { S, abbreviations, leadingAbbreviations, lessonTypes, lowerWords, subjectDictionary, yo, type StringKey } from "./strings.gen.ts";

export { S };

/** «{0} этаж» → «2 этаж». */
export function fmt(template: string, ...args: (string | number)[]): string {
  return template.replace(/\{(\d+)\}/g, (match, index) => index < args.length ? String(args[Number(index)]) : match);
}
export function t(key: StringKey, ...args: (string | number)[]): string { return fmt(S[key], ...args); }

const typeTokens = new Set(["лек", "пр", "лаб", "конс", "зач", "экз", "курс"]);
const vowels = /[АЕЁИОУЫЭЮЯAEIOUY]/;
const abbr = new Set(abbreviations);
const lower = new Set(lowerWords);
/** Ключ словаря без пробелов и точек: «ОСН РОС ГОС» и «ОСН.РОС.ГОС» — один предмет. */
const dictionaryKey = (value: string) => value.toUpperCase().replace(/[\s.]/g, "");
const dictionary = new Map(Object.entries(subjectDictionary).map(([key, value]) => [dictionaryKey(key), value]));

function collapse(value: string | null | undefined): string { return (value ?? "").replace(/\s+/g, " ").trim(); }

/** Текст из фида: буквы почти все заглавные. Названия, которые студент задал сам, не трогаем. */
function feedLike(value: string): boolean {
  const letters = value.match(/[А-ЯЁа-яёA-Za-z]/g) ?? [];
  if (letters.length === 0) return false;
  const upper = letters.filter(ch => ch === ch.toUpperCase()).length;
  return upper / letters.length >= 0.6;
}

/** «пр УПР.ПРОЕКТАМИ» → «УПР.ПРОЕКТАМИ»: префикс типа показывает чип. */
export function stripTypePrefix(raw: string | null | undefined, typeRaw?: string | null): string {
  const value = collapse(raw);
  const space = value.indexOf(" ");
  if (space <= 0) return value;
  const head = value.slice(0, space).toLowerCase();
  const type = collapse(typeRaw).toLowerCase();
  return head === type || typeTokens.has(head) ? value.slice(space + 1) : value;
}

function withYo(word: string): string {
  for (const [plain, dotted] of Object.entries(yo)) if (word.startsWith(plain)) return dotted + word.slice(plain.length);
  return word;
}

function readable(value: string): string {
  const spaced = collapse(value.replace(/([.,])(?=[А-ЯЁа-яёA-Za-z])/g, "$1 "));
  const out = spaced.replace(/[А-ЯЁа-яёA-Za-z]+/g, (word, offset: number) => {
    const upper = word.toUpperCase();
    const dotted = spaced[offset + word.length] === ".";
    if (word.length === 1) return dotted ? upper : word.toLowerCase();
    if (/[а-яё]/.test(word) && /[А-ЯЁ]/.test(word.slice(1))) return word; // «МиР»: смешанный регистр — как в фиде
    if (abbr.has(upper) || offset === 0 && leadingAbbreviations.includes(upper)) return upper;
    if (lower.has(upper)) return withYo(word.toLowerCase());
    if (!vowels.test(upper)) return upper;
    return withYo(word.toLowerCase());
  });
  return out.charAt(0).toUpperCase() + out.slice(1);
}

export type SubjectText = { short: string; full: string };

/** Короткое имя для строки пары и полное — для листа/подсказки. */
export function subjectText(raw: string | null | undefined, typeRaw?: string | null): SubjectText {
  const value = stripTypePrefix(raw, typeRaw);
  if (!value || !feedLike(value)) return { short: value, full: value };
  const known = dictionary.get(dictionaryKey(value));
  if (known) return { ...known };
  const short = readable(value);
  return { short, full: short };
}
export function subjectShort(raw: string | null | undefined, typeRaw?: string | null): string { return subjectText(raw, typeRaw).short; }

/** «268*(фесто);» → «268 (Фесто)», «ВЦ 281; ВЦ 283;» → «ВЦ 281, ВЦ 283». */
export function roomText(raw: string | null | undefined): string {
  return (raw ?? "").split(";").map(part => collapse(part.replace(/\*/g, ""))
    .replace(/(\S)\(/g, "$1 (")
    .replace(/\((\S)/g, (_, ch: string) => "(" + ch.toUpperCase()))
    .filter(Boolean).join(", ");
}

const initials = /^(?:[А-ЯЁA-Z]\.)+$/;
function oneTeacher(raw: string): string {
  const tokens = collapse(raw).split(" ").filter(Boolean);
  if (tokens.length === 0) return "";
  let at = tokens.length;
  while (at > 1 && initials.test(tokens[at - 1])) at--;
  const tail = tokens.slice(at).join("").match(/[А-ЯЁA-Z]\./g) ?? [];
  let surname = tokens.slice(0, at);
  if (tail.length === 1 && surname.length >= 2) { tail.unshift(surname[surname.length - 1].charAt(0).toUpperCase() + "."); surname = surname.slice(0, -1); }
  return tail.length ? `${surname.join(" ")} ${tail.join(" ")}` : surname.join(" ");
}

/** «Кондратьев Сергей А.» → «Кондратьев С. А.»; несколько преподавателей — через запятую. */
export function teacherText(raw: string | null | undefined): string {
  return (raw ?? "").split(";").map(oneTeacher).filter(Boolean).join(", ");
}

/** Строка метаданных без пустых полей и висящих «·». */
export function metaLine(...parts: (string | null | undefined)[]): string {
  return parts.map(collapse).filter(part => part && part !== "—" && part !== "·").join(" · ");
}

const typeKeys: Record<string, StringKey> = {
  lecture: "typeLecture", practice: "typePractice", lab: "typeLab", consult: "typeConsult",
  credit: "typeCredit", exam: "typeExam", course: "typeCourse"
};
/** «лек» → "lecture" (для цвета чипа) или "" для незнакомого типа. */
export function lessonKindOf(typeRaw: string | null | undefined): string { return lessonTypes[collapse(typeRaw).toLowerCase()] ?? ""; }
/** Подпись чипа типа пары, с заглавной: «Практика». */
export function typeLabel(typeRaw: string | null | undefined): string {
  const kind = lessonKindOf(typeRaw);
  if (kind) return S[typeKeys[kind]];
  const value = collapse(typeRaw);
  return value.charAt(0).toUpperCase() + value.slice(1);
}

const shortMonths = ["янв.", "февр.", "мар.", "апр.", "мая", "июн.", "июл.", "авг.", "сент.", "окт.", "нояб.", "дек."];
const pad = (n: number) => String(n).padStart(2, "0");
/** «9 окт.»; год — только если он не текущий. */
export function formatDay(date: Date, now = new Date()): string {
  const base = `${date.getDate()} ${shortMonths[date.getMonth()]}`;
  return date.getFullYear() === now.getFullYear() ? base : `${base} ${date.getFullYear()}`;
}
/** «9 окт., 18:31». */
export function formatDateTime(value: Date | string, now = new Date()): string {
  const date = typeof value === "string" ? new Date(value) : value;
  return `${formatDay(date, now)}, ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
/** «5–11 окт.», «28 сент.–4 окт.» — короткое тире без пробелов. */
export function formatRange(from: Date, to: Date, now = new Date()): string {
  const sameMonth = from.getMonth() === to.getMonth() && from.getFullYear() === to.getFullYear();
  return sameMonth ? `${from.getDate()}–${formatDay(to, now)}` : `${formatDay(from, now)}–${formatDay(to, now)}`;
}
