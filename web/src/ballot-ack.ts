import type { BallotBoard } from "./types";
/** Mutation responses contain the whole group; preserve independent, newer card state. */
export function mergeBallotAck(current: BallotBoard, ack: BallotBoard, topic?: string, target?: string): BallotBoard {
  const known = new Set(current.ballots.map(row => row.ballotId));
  const accepted = ack.ballots.filter(row => (!topic || row.topicId === topic) && (target ? row.ballotId === target : !known.has(row.ballotId)));
  const changed = new Map(accepted.map(row => [row.ballotId,row]));
  return {...ack, ballots:[...accepted.filter(row=>!known.has(row.ballotId)),...current.ballots.map(row=>changed.get(row.ballotId)??row)]};
}
