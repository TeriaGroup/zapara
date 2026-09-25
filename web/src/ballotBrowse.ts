import type { Ballot } from "./types";

export type BallotBrowseFilter = { query: string; status: "all" | "collecting" | "open" | "closed"; sort: "default" | "nearest" | "farthest" };

export function filterBallots(ballots: Ballot[], filter: BallotBrowseFilter): Ballot[] {
  const query = filter.query.trim().toLocaleLowerCase("ru-RU");
  const matched = ballots.filter(ballot =>
    (filter.status === "all" || ballot.status === filter.status) &&
    (!query || `${ballot.question} ${ballot.options.map(option => option.label).join(" ")}`.toLocaleLowerCase("ru-RU").includes(query)));
  if (filter.sort === "default") return matched;
  return matched.map((ballot, index) => ({ ballot, index, deadline: Date.parse(ballot.deadlineAt) }))
    .sort((left, right) => {
      const leftValid = Number.isFinite(left.deadline);
      const rightValid = Number.isFinite(right.deadline);
      if (!leftValid || !rightValid) return leftValid === rightValid ? left.index - right.index : leftValid ? -1 : 1;
      const difference = filter.sort === "nearest" ? left.deadline - right.deadline : right.deadline - left.deadline;
      return difference || left.index - right.index;
    }).map(item => item.ballot);
}

export function ballotVoteTotal(ballot: Ballot): number {
  return ballot.options.reduce((sum, option) => sum + Math.max(0, option.votes), 0);
}

export function voteShare(votes: number, total: number): number {
  return total > 0 && Number.isFinite(total) ? Math.min(100, Math.round(Math.max(0, votes) / total * 100)) : 0;
}

export function ballotStatusTitle(status: string): string {
  if (status === "collecting") return "Сбор поддержки";
  if (status === "open") return "Идёт";
  if (status === "closed") return "Завершено";
  return status;
}

export function ballotDeadlineLabel(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : new Intl.DateTimeFormat("ru-RU", {
    day: "numeric", month: "long", year: "numeric", hour: "2-digit", minute: "2-digit",
  }).format(date);
}

function voteWord(votes: number): string {
  const ten = votes % 10;
  const hundred = votes % 100;
  if (ten === 1 && hundred !== 11) return "голос";
  if (ten >= 2 && ten <= 4 && (hundred < 12 || hundred > 14)) return "голоса";
  return "голосов";
}

export function ballotSummary(ballot: Ballot): string {
  const total = ballotVoteTotal(ballot);
  return [
    `Вопрос: ${ballot.question}`,
    `Статус: ${ballotStatusTitle(ballot.status)}`,
    `Срок: ${ballotDeadlineLabel(ballot.deadlineAt)}`,
    ...(ballot.status === "collecting" ? [`Поддержали: ${ballot.supporters} из ${ballot.supportersNeeded}`] : []),
    `Варианты (доля голосов, всего ${total} ${voteWord(total)}):`,
    ...ballot.options.map(option => `${option.label}: ${option.votes} ${voteWord(option.votes)} · ${voteShare(option.votes, total)}%`),
  ].join("\n");
}

export function isBallotDeadlineSoon(ballot: Ballot, nowMs: number): boolean {
  if (ballot.status !== "collecting" && ballot.status !== "open") return false;
  const remaining = Date.parse(ballot.deadlineAt) - nowMs;
  return remaining > 0 && remaining <= 24 * 60 * 60 * 1000;
}
