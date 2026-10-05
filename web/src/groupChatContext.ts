import type { GroupTopic, Lesson, Period } from "./types";
import { addDays, lessonsOn } from "./parity.ts";
import { visibleLessons } from "./subgroups.ts";

export type GroupChatContext = {
  nextLesson: { date: Date; subject: string; time: string; room: string } | null;
  activeBallots: number;
  unread: number;
  hasContent: boolean;
};

export type GroupChatContextInput = {
  communityGroupName: string | null;
  selectedGroupName: string | null;
  timetableAvailable: boolean;
  lessons: Lesson[];
  subgroupChoices: Record<string, string>;
  period: Period | null;
  invert: boolean;
  topics: GroupTopic[];
  now: Date;
};

function timeMinutes(value: string): number | null {
  const parts = value.split(":").map(Number);
  if (parts.length !== 2 || parts.some(part => !Number.isInteger(part)) ||
    parts[0] < 0 || parts[0] > 23 || parts[1] < 0 || parts[1] > 59) return null;
  return parts[0] * 60 + parts[1];
}

export function buildGroupChatContext(input: GroupChatContextInput): GroupChatContext {
  const real = input.topics.filter(topic => topic.kind === "chat" || topic.topicId !== null);
  const activeBallots = real.filter(topic => topic.kind === "ballots")
    .reduce((sum, topic) => sum + Math.max(0, topic.activeBallots), 0);
  const unread = real.filter(topic => topic.kind === "chat")
    .reduce((sum, topic) => sum + Math.max(0, topic.unread), 0);
  const sameGroup = !!input.communityGroupName?.trim() && !!input.selectedGroupName?.trim() &&
    input.communityGroupName.trim().toLocaleLowerCase("ru-RU") === input.selectedGroupName.trim().toLocaleLowerCase("ru-RU");
  let nextLesson: GroupChatContext["nextLesson"] = null;
  if (sameGroup && input.timetableAvailable && input.period && input.lessons.length) {
    const shown = visibleLessons(input.lessons, input.subgroupChoices);
    for (let offset = 0; offset < 14 && !nextLesson; offset += 1) {
      const date = addDays(input.now, offset);
      const day = lessonsOn(shown, date, input.period.start, input.period.weekCount, input.invert);
      const lesson = day.find(item => {
        const start = timeMinutes(item.timeStart);
        if (start === null) return false;
        const end = timeMinutes(item.timeEnd) ?? start + 95;
        const now = input.now.getHours() * 60 + input.now.getMinutes();
        return offset > 0 || start > now || end > now;
      });
      if (lesson) {
        nextLesson = {
          date,
          subject: lesson.subjectNormalized.trim() || lesson.subjectRaw.trim(),
          time: lesson.timeStart,
          room: (lesson.roomRaw || lesson.classroomRaw || "").trim(),
        };
      }
    }
  }
  return { nextLesson, activeBallots, unread, hasContent: !!nextLesson || activeBallots > 0 || unread > 0 };
}
