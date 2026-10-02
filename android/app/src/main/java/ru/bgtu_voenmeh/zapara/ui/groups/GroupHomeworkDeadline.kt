package ru.bgtu_voenmeh.zapara.ui.groups

import java.time.Instant
import java.time.ZoneId

internal fun sharedHomeworkDeadlineMatches(deadline: Instant?, filter: String, now: Instant,
    zone: ZoneId): Boolean = when (filter) {
    "overdue" -> deadline != null && deadline.isBefore(now)
    "near" -> deadline != null && !deadline.isBefore(now) && deadline.isBefore(
        now.atZone(zone).toLocalDate().plusDays(2).atStartOfDay(zone).toInstant())
    "none" -> deadline == null
    else -> true
}
