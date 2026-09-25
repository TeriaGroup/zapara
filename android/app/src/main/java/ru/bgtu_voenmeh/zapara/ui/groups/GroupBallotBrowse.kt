package ru.bgtu_voenmeh.zapara.ui.groups

import java.time.Instant
import ru.bgtu_voenmeh.zapara.data.communities.Ballot

internal enum class BallotStatus(val wire: String?) {
    All(null), Collecting("collecting"), Open("open"), Closed("closed")
}

internal enum class BallotSort { Original, Nearest, Farthest }
internal enum class DeadlineNotice { None, Soon, AwaitingServer }

internal fun browseBallots(
    rows: List<Ballot>, query: String,
    status: BallotStatus = BallotStatus.All,
    sort: BallotSort = BallotSort.Original
): List<Ballot> {
    val needle = query.trim()
    val visible = rows.filter { ballot ->
        (status.wire == null || ballot.status == status.wire) &&
            (needle.isEmpty() || ballot.question.contains(needle, ignoreCase = true) ||
                ballot.options.any { it.label.contains(needle, ignoreCase = true) })
    }
    return when (sort) {
        BallotSort.Original -> visible
        BallotSort.Nearest -> visible.withIndex()
            .sortedWith(compareBy<IndexedValue<Ballot>> { it.value.deadlineAt }.thenBy { it.index })
            .map { it.value }
        BallotSort.Farthest -> visible.withIndex()
            .sortedWith(compareByDescending<IndexedValue<Ballot>> { it.value.deadlineAt }.thenBy { it.index })
            .map { it.value }
    }
}

internal fun ballotTotalVotes(ballot: Ballot): Long = ballot.options.sumOf { it.votes.coerceAtLeast(0).toLong() }

internal fun ballotPercent(votes: Int, totalVotes: Long): Int =
    if (totalVotes <= 0) 0 else ((votes.coerceAtLeast(0).toLong() * 100 + totalVotes / 2) / totalVotes)
        .coerceIn(0, 100).toInt()

internal fun ballotDeadlineNotice(ballot: Ballot, now: Instant): DeadlineNotice = when {
    ballot.status !in setOf("collecting", "open") -> DeadlineNotice.None
    !ballot.deadlineAt.isAfter(now) -> DeadlineNotice.AwaitingServer
    !ballot.deadlineAt.isAfter(now.plusSeconds(24 * 60 * 60)) -> DeadlineNotice.Soon
    else -> DeadlineNotice.None
}

internal fun ballotSummary(ballot: Ballot, statusLine: String, deadlineLine: String,
    votesLabel: String, shareLabel: String, supportLine: String? = null): String {
    val total = ballotTotalVotes(ballot)
    return buildString {
        append(ballot.question).append('\n').append(statusLine).append('\n').append(deadlineLine)
        if (ballot.status == "collecting" && !supportLine.isNullOrBlank()) append('\n').append(supportLine)
        ballot.options.forEach { option ->
            append('\n').append(option.label).append(" — ").append(votesLabel).append(": ").append(option.votes)
                .append("; ").append(shareLabel).append(": ").append(ballotPercent(option.votes, total)).append('%')
        }
    }
}
