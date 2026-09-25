import type { FriendItem, Group, Lesson, Period, TimetablePayload } from "./types";
import { addDays, lessonsOn, score, timesOverlap } from "./parity.ts";

export type FriendSchedule = {
  groupName: string;
  members: string;
  enabled: boolean;
  color?: string;
  lessons: Lesson[] | null;
};
export type IntersectionInput = {
  mineLessons: Lesson[];
  friends: FriendSchedule[];
  period: Period;
  invert: boolean;
  strictness: number;
  now: Date;
  horizonDays?: number;
};
export type Encounter = {
  date: Date;
  time: string;
  subject: string;
  friendGroupName: string;
  members: string;
  friendRoom: string;
  color?: string;
  score: number;
};
export type FriendMark = { groupName: string; members: string; score: number; present: boolean; color?: string };
export type IntersectionForecast = {
  encounters: Encounter[];
  missingGroups: string[];
  checkedGroups: number;
};

export function resolveFriendSchedules(friends: FriendItem[], groups: Group[],
  cache: Record<string, TimetablePayload>, own?: TimetablePayload): FriendSchedule[] {
  return friends.map(friend => {
    const saved = friend.groupName.trim();
    const known = groups.find(group => group.id === saved ||
      group.name.toLocaleLowerCase("ru-RU") === saved.toLocaleLowerCase("ru-RU"));
    const cached = (known && cache[known.id]) || cache[saved] || (known && cache[known.name]);
    const ownGroup = own?.group;
    const self = !!ownGroup && (known?.id === ownGroup.id || saved === ownGroup.id ||
      saved.toLocaleLowerCase("ru-RU") === ownGroup.name.toLocaleLowerCase("ru-RU"));
    const compatible = !self && (!own || !cached || !!(own.period && cached.period && own.meta && cached.meta &&
      own.period.start === cached.period.start && own.period.weekCount === cached.period.weekCount &&
      own.meta.snapshotId === cached.meta.snapshotId && !own.meta.stale && !cached.meta.stale));
    return {
      groupName: friend.groupName, members: friend.members, enabled: friend.enabled, color: friend.color,
      lessons: compatible ? cached?.lessons ?? null : null,
    };
  });
}

function minutes(value: string): number | null {
  const match = /^(\d{1,2}):(\d{2})$/.exec(value.trim());
  if (!match) return null;
  const hour = Number(match[1]);
  const minute = Number(match[2]);
  return hour < 24 && minute < 60 ? hour * 60 + minute : null;
}

function bestMatch(mine: Lesson, date: Date, friend: FriendSchedule, input: IntersectionInput):
  { lesson: Lesson; score: number } | null {
  if (!friend.enabled || friend.lessons === null) return null;
  let best: { lesson: Lesson; score: number } | null = null;
  for (const other of lessonsOn(friend.lessons, date, input.period.start, input.period.weekCount, input.invert)) {
    if (!timesOverlap(mine.timeStart, mine.timeEnd, other.timeStart, other.timeEnd)) continue;
    const value = score(mine.roomRaw, mine.buildingRaw, other.roomRaw, other.buildingRaw);
    if (best === null || value > best.score) best = { lesson: other, score: value };
  }
  return best;
}

export function intersectionPlace(scoreValue: number): string {
  return scoreValue >= 100 ? "в той же аудитории" : scoreValue >= 75 ? "на том же этаже"
    : scoreValue >= 50 ? "в том же корпусе" : scoreValue >= 25 ? "в вузе" : "не рядом";
}

export function markDescription(mark: FriendMark): string {
  if (mark.present) return intersectionPlace(mark.score);
  return mark.score > 0 ? `${intersectionPlace(mark.score)} · ниже выбранной точности` : "нет совпадения по времени";
}

export function marksForLesson(lesson: Lesson, date: Date, input: IntersectionInput, alwaysShow: boolean): FriendMark[] {
  const out: FriendMark[] = [];
  for (const friend of input.friends.filter(item => item.enabled).slice(0, 5)) {
    if (friend.lessons === null) continue;
    const best = bestMatch(lesson, date, friend, input);
    const value = best?.score ?? 0;
    const present = value >= input.strictness;
    if (present || alwaysShow) out.push({ groupName: friend.groupName, members: friend.members, score: value, present, color: friend.color });
  }
  return out;
}

export function forecastIntersections(input: IntersectionInput): IntersectionForecast {
  const friends = input.friends.filter(friend => friend.enabled).slice(0, 5);
  const missingGroups = friends.filter(friend => friend.lessons === null).map(friend => friend.groupName);
  const checkedGroups = friends.length - missingGroups.length;
  const encounters: Encounter[] = [];
  const days = Math.max(0, Math.min(input.horizonDays ?? 14, 14));
  for (let offset = 0; offset < days; offset += 1) {
    const date = addDays(input.now, offset);
    const own = lessonsOn(input.mineLessons, date, input.period.start, input.period.weekCount, input.invert)
      .sort((a, b) => (minutes(a.timeStart) ?? Number.MAX_SAFE_INTEGER) - (minutes(b.timeStart) ?? Number.MAX_SAFE_INTEGER));
    for (const lesson of own) {
      const start = minutes(lesson.timeStart);
      if (start === null) continue;
      const end = minutes(lesson.timeEnd) ?? start + 95;
      if (offset === 0 && end <= input.now.getHours() * 60 + input.now.getMinutes()) continue;
      for (const friend of friends) {
        const best = bestMatch(lesson, date, friend, input);
        if (!best || best.score < input.strictness) continue;
        encounters.push({
          date, time: lesson.timeStart,
          subject: lesson.subjectNormalized.trim() || lesson.subjectRaw.trim(),
          friendGroupName: friend.groupName, members: friend.members,
          friendRoom: (best.lesson.roomRaw || best.lesson.classroomRaw || "").trim(), color: friend.color,
          score: best.score,
        });
        if (encounters.length === 3) return { encounters, missingGroups, checkedGroups };
      }
    }
  }
  return { encounters, missingGroups, checkedGroups };
}
