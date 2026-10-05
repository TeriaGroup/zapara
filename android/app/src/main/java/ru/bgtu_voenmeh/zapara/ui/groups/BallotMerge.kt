package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.BallotBoard

/** A POST owns only its ballot; an older response cannot replace another ballot's result. */
internal fun mergeBallotPost(current: BallotBoard?, posted: BallotBoard, topicId: String?, ballotId: String?): BallotBoard {
    val scoped = posted.copy(ballots = posted.ballots.filter { topicId == null || it.topicId == topicId })
    if (current == null) return scoped
    val owned = if (ballotId == null) scoped.ballots.filter { row -> current.ballots.none { it.ballotId == row.ballotId } }
        else scoped.ballots.filter { it.ballotId == ballotId }
    if (owned.isEmpty()) return current
    val replacements = owned.associateBy { it.ballotId }
    return current.copy(ballots = current.ballots.map { replacements[it.ballotId] ?: it } +
        owned.filter { row -> current.ballots.none { it.ballotId == row.ballotId } })
}
