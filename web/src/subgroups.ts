import type { Lesson } from "./types";

export type SubgroupOption = { id: string; label: string };
export type SubgroupStream = { id: string; title: string; options: SubgroupOption[]; joined: boolean };
export type SubgroupMark = { streamId: string; options: SubgroupOption[]; chosenId: string | null; showChooser: boolean };

type Member = { streamId: string; optionIds: string[]; joined: boolean };
type Index = { streams: SubgroupStream[]; membership: Map<string, Member> };

export function subgroupIndex(lessons: Lesson[]): Index {
  const streams = new Map<string, { id: string; title: string; same: boolean; joined: boolean; options: Map<string, string> }>();
  const membership = new Map<string, Member>();
  for (const cluster of clusters(lessons)) {
    let builder = streams.get(cluster.streamId);
    if (!builder) {
      builder = { id: cluster.streamId, title: cluster.title, same: cluster.same, joined: true, options: new Map() };
      streams.set(cluster.streamId, builder);
    }
    builder.joined = builder.joined && cluster.lessons.length === 1;
    for (const [id, label] of cluster.labels) if (!builder.options.has(id)) builder.options.set(id, label);
    for (const lesson of cluster.lessons) {
      const key = lessonKey(lesson);
      const ids = cluster.assignment?.get(key) ?? (cluster.same ? teacherNames(lesson.teacherRaw).map(teacherKey) : [slotOptionId(lesson)]);
      const previous = membership.get(key);
      const joined = cluster.lessons.length === 1 && previous?.joined !== false;
      membership.set(key, { streamId: cluster.streamId, optionIds: ids, joined: joined && cluster.lessons.length === 1 });
    }
  }
  const built = [...streams.values()]
    .filter(builder => builder.options.size >= 2)
    .map(builder => ({
      id: builder.id,
      title: builder.title,
      joined: builder.joined && builder.same,
      options: [...builder.options].map(([id, label]) => ({ id, label })),
    }))
    .sort((a, b) => a.id.localeCompare(b.id));
  const live = new Set(built.map(stream => stream.id));
  for (const [key, member] of [...membership]) if (!live.has(member.streamId)) membership.delete(key);
  return { streams: built, membership };
}

export function visibleLessons(lessons: Lesson[], choices: Record<string, string>): Lesson[] {
  const index = subgroupIndex(lessons);
  return lessons.filter(lesson => keep(lesson, index, choices));
}

export function subgroupMark(lesson: Lesson, day: Lesson[], index: Index, choices: Record<string, string>): SubgroupMark | null {
  const member = index.membership.get(lessonKey(lesson));
  if (!member) return null;
  const stream = index.streams.find(item => item.id === member.streamId);
  if (!stream) return null;
  const chosen = choices[stream.id] && stream.options.some(option => option.id === choices[stream.id]) ? choices[stream.id] : null;
  const first = day.filter(item => index.membership.get(lessonKey(item))?.streamId === stream.id)
    .sort((a, b) => timeKey(a.timeStart).localeCompare(timeKey(b.timeStart)) || a.index - b.index || lessonKey(a).localeCompare(lessonKey(b)))[0];
  return { streamId: stream.id, options: stream.options, chosenId: chosen, showChooser: !!first && lessonKey(first) === lessonKey(lesson) };
}

function keep(lesson: Lesson, index: Index, choices: Record<string, string>): boolean {
  const member = index.membership.get(lessonKey(lesson));
  if (!member) return true;
  const stream = index.streams.find(item => item.id === member.streamId);
  if (!stream) return true;
  const choice = choices[stream.id];
  if (!choice || !stream.options.some(option => option.id === choice)) return true;
  if (member.joined) return true;
  return member.optionIds.includes(choice);
}

type Cluster = { streamId: string; title: string; same: boolean; lessons: Lesson[]; labels: Map<string, string>; assignment?: Map<string, string[]> };

function clusters(lessons: Lesson[]) {
  const found: Cluster[] = [];
  const seen = new Set<string>();
  const slots = new Map<string, Lesson[]>();
  for (const lesson of lessons) {
    const key = `${lesson.dayOfWeek}|${timeKey(lesson.timeStart)}`;
    const rows = slots.get(key) || [];
    rows.push(lesson);
    slots.set(key, rows);
  }
  for (const [slot, rows] of slots) {
    const [day, time] = slot.split("|");
    const handled = new Set<string>();
    const bySubject = new Map<string, Lesson[]>();
    for (const lesson of rows) {
      const subject = subjectKey(lesson);
      const group = bySubject.get(subject) || [];
      group.push(lesson);
      bySubject.set(subject, group);
    }
    for (const group of bySubject.values()) {
      const paired = pairCluster(group);
      if (!paired) continue;
      found.push(paired);
      for (const lesson of group) handled.add(lessonKey(lesson));
    }
    const rest = rows.filter(lesson => !handled.has(lessonKey(lesson)));
    for (const week of weekCodes(rest)) {
      const active = rest.filter(lesson => lesson.parity === 0 || lesson.parity === week);
      const names = new Map<string, string>();
      for (const lesson of [...active].sort((a, b) => a.index - b.index || subjectKey(a).localeCompare(subjectKey(b)))) {
        for (const name of teacherNames(lesson.teacherRaw)) {
          const id = teacherKey(name);
          if (id && !names.has(id)) names.set(id, name.trim());
        }
      }
      if (names.size < 2 || active.length === 0) continue;
      const subjects = [...new Set(active.map(subjectKey).filter(Boolean))];
      const same = subjects.length === 1;
      const set = [...names.keys()].sort().join("+");
      const streamId = same ? `s:${subjects[0]}:${set}` : `t:${day}:${time}:${set}`;
      const signature = streamId + "#" + active.map(lessonKey).sort().join(",");
      if (seen.has(signature)) continue;
      seen.add(signature);
      const labels = same ? names : new Map(active.map(lesson => [slotOptionId(lesson), slotLabel(lesson)] as const));
      const title = [...active].sort((a, b) => a.dayOfWeek - b.dayOfWeek || timeKey(a.timeStart).localeCompare(timeKey(b.timeStart)) || a.index - b.index)[0].subjectRaw;
      found.push({ streamId, title, same, lessons: active, labels });
    }
  }
  return found;
}

function slotLabel(lesson: Lesson): string {
  const teachers = teacherNames(lesson.teacherRaw).join(", ");
  return [lesson.subjectRaw.trim(), teachers].filter(Boolean).join(" · ") || teachers;
}

function slotOptionId(lesson: Lesson): string {
  const teachers = teacherNames(lesson.teacherRaw).map(teacherKey).sort().join("+");
  return `${subjectKey(lesson)}|${teachers}|${(lesson.classroomRaw || "").trim()}`;
}

function lessonKey(lesson: Lesson): string {
  return [lesson.dayOfWeek, lesson.parity, lesson.index, timeKey(lesson.timeStart), subjectKey(lesson), teacherKey(lesson.teacherRaw || ""), (lesson.classroomRaw || "").trim()].join("|");
}

function subjectKey(lesson: Lesson): string {
  const norm = (lesson.subjectNormalized || "").trim();
  if (norm) return norm;
  return (lesson.subjectRaw || "").trim().toLocaleLowerCase("ru").replaceAll("ё", "е");
}

function teacherNames(raw: string | null | undefined): string[] {
  return (raw || "").split(";").map(part => part.trim()).filter(part => part && part !== "—" && part !== "-");
}

function teacherKey(name: string): string {
  return name.trim().toLocaleLowerCase("ru").replaceAll("ё", "е").replace(/[.\s]+/g, " ").trim();
}

function timeKey(raw: string): string {
  const match = /(\d{1,2}):(\d{2})/.exec(raw || "");
  if (!match) return (raw || "").trim();
  return `${match[1].padStart(2, "0")}:${match[2]}`;
}

function pairCluster(rows: Lesson[]): Cluster | null {
  if (rows.length < 3 || rows.some(lesson => lesson.parity < 0 || lesson.parity > 2) || !subjectKey(rows[0])) return null;
  const info = new Map<string, { label: string; weeks: Set<number> }>();
  for (const lesson of rows) {
    const weeks = lesson.parity === 0 ? [1, 2] : [lesson.parity];
    for (const name of teacherNames(lesson.teacherRaw)) {
      const id = teacherKey(name);
      if (!id) continue;
      const current = info.get(id) || { label: name.trim(), weeks: new Set<number>() };
      for (const week of weeks) current.weeks.add(week);
      info.set(id, current);
    }
  }
  const odd = [...info].filter(([, value]) => value.weeks.has(1)).map(([id]) => id);
  const even = [...info].filter(([, value]) => value.weeks.has(2)).map(([id]) => id);
  const stable = odd.filter(id => even.includes(id));
  const oddOnly = odd.filter(id => !even.includes(id));
  const evenOnly = even.filter(id => !odd.includes(id));
  if (stable.length === 0 || oddOnly.length !== 1 || evenOnly.length !== 1) return null;
  const oddKey = oddOnly[0];
  const evenKey = evenOnly[0];
  const pairId = `w:${oddKey}+${evenKey}`;
  const labels = new Map<string, string>();
  for (const id of [...stable].sort()) labels.set(id, info.get(id)!.label);
  labels.set(pairId, `${info.get(oddKey)!.label} · нечётная / ${info.get(evenKey)!.label} · чётная`);
  const streamId = `s:${subjectKey(rows[0])}:` + [...stable, oddKey, evenKey].sort().join("+");
  const assignment = new Map<string, string[]>();
  for (const lesson of rows) {
    const keys = new Set(teacherNames(lesson.teacherRaw).map(teacherKey));
    const ids = [...keys].filter(id => stable.includes(id));
    if (keys.has(oddKey) || keys.has(evenKey)) ids.push(pairId);
    assignment.set(lessonKey(lesson), ids);
  }
  const title = [...rows].sort((a, b) => a.dayOfWeek - b.dayOfWeek || timeKey(a.timeStart).localeCompare(timeKey(b.timeStart)) || a.index - b.index)[0].subjectRaw;
  return { streamId, title, same: true, lessons: rows, labels, assignment };
}

function weekCodes(rows: Lesson[]): number[] {
  const codes = [...new Set(rows.map(lesson => lesson.parity).filter(parity => parity > 0))];
  return codes.length ? codes : [1];
}
