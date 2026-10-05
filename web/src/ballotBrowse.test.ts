import assert from "node:assert/strict";
import { test } from "node:test";
import { ballotSummary, ballotVoteTotal, filterBallots, isBallotDeadlineSoon, voteShare } from "./ballotBrowse.ts";
import type { Ballot } from "./types.ts";

function ballot(id: string, status: string, deadlineAt: string, question = id): Ballot {
  return {
    ballotId: id, question, origin: "collective", status, deadlineAt,
    supporters: 2, supportersNeeded: 3, supported: false,
    options: [
      { optionId: `${id}-one`, label: "Вторник", votes: 1, chosen: false },
      { optionId: `${id}-two`, label: "Среда", votes: 7, chosen: true },
    ],
    effect: "", outcome: "",
  };
}

const rows = [
  ballot("late", "open", "2026-09-28T12:00:00Z", "Где встретимся?"),
  ballot("closed", "closed", "2026-09-27T12:00:00Z", "Когда сдаём?"),
  ballot("early", "collecting", "2026-09-26T12:00:00Z", "Куда идём?"),
  ballot("tie", "open", "2026-09-26T12:00:00Z", "Что читаем?"),
];

test("ballot browse searches question and options on only the supplied current board", () => {
  const filter = { query: "  СРЕДА  ", status: "all" as const, sort: "default" as const };
  assert.deepEqual(filterBallots(rows, filter).map(row => row.ballotId), ["late", "closed", "early", "tie"]);
  assert.deepEqual(filterBallots(rows, { ...filter, query: " куда " }).map(row => row.ballotId), ["early"]);
  assert.deepEqual(filterBallots([], filter), []);
  assert.deepEqual(rows.map(row => row.ballotId), ["late", "closed", "early", "tie"]);
});

test("status uses the server field and filters recompute for a replaced live board", () => {
  const filter = { query: "", status: "open" as const, sort: "default" as const };
  assert.deepEqual(filterBallots(rows, filter).map(row => row.ballotId), ["late", "tie"]);
  const updated = rows.map(row => row.ballotId === "early" ? { ...row, status: "open" } : row);
  assert.deepEqual(filterBallots(updated, filter).map(row => row.ballotId), ["late", "early", "tie"]);
  assert.deepEqual(filterBallots(rows, { ...filter, status: "closed" }).map(row => row.ballotId), ["closed"]);
  assert.deepEqual(filterBallots(rows, { ...filter, status: "collecting" }).map(row => row.ballotId), ["early"]);
});

test("deadline sorting is stable on ties and leaves the server list untouched", () => {
  const base = { query: "", status: "all" as const };
  assert.deepEqual(filterBallots(rows, { ...base, sort: "default" }).map(row => row.ballotId), ["late", "closed", "early", "tie"]);
  assert.deepEqual(filterBallots(rows, { ...base, sort: "nearest" }).map(row => row.ballotId), ["early", "tie", "closed", "late"]);
  assert.deepEqual(filterBallots(rows, { ...base, sort: "farthest" }).map(row => row.ballotId), ["late", "closed", "early", "tie"]);
  assert.deepEqual(rows.map(row => row.ballotId), ["late", "closed", "early", "tie"]);
});

test("vote share rounds halves up and treats an empty vote total as zero", () => {
  assert.equal(voteShare(1, 8), 13);
  assert.equal(voteShare(2, 3), 67);
  assert.equal(voteShare(0, 0), 0);
  assert.equal(ballotVoteTotal(rows[0]), 8);
  assert.equal(ballotVoteTotal({ ...rows[0], options: [] }), 0);
});

test("copied ballot summary names only the question, server status, deadline and vote shares", () => {
  const row = { ...rows[0], voterName: "Секретный студент", createdBy: "Автор голосования" } as Ballot;
  const summary = ballotSummary(row);
  assert.match(summary, /Где встретимся\?/);
  assert.match(summary, /Идёт/);
  assert.match(summary, /28 сентября/);
  assert.match(summary, /Вторник: 1 голос · 13%/);
  assert.match(summary, /Среда: 7 голосов · 88%/);
  assert.match(summary, /доля голосов/i);
  assert.doesNotMatch(summary, /Секретный студент|Автор голосования/);
  assert.match(ballotSummary({ ...row, options: row.options.map(option => ({ ...option, votes: 0 })) }), /0%/);
  assert.match(ballotSummary(rows[2]), /Поддержали: 2 из 3/);
});

test("urgency only marks still-live ballots due within 24 hours", () => {
  const now = Date.parse("2026-09-25T12:00:00Z");
  assert.equal(isBallotDeadlineSoon(ballot("soon", "open", "2026-09-26T11:00:00Z"), now), true);
  assert.equal(isBallotDeadlineSoon(ballot("soon", "collecting", "2026-09-26T11:00:00Z"), now), true);
  assert.equal(isBallotDeadlineSoon(ballot("closed", "closed", "2026-09-26T11:00:00Z"), now), false);
  assert.equal(isBallotDeadlineSoon(ballot("later", "open", "2026-09-27T12:00:00Z"), now), false);
  assert.equal(isBallotDeadlineSoon(ballot("past", "open", "2026-09-25T11:00:00Z"), now), false);
});
