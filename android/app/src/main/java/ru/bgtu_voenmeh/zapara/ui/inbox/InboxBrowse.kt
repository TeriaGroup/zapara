package ru.bgtu_voenmeh.zapara.ui.inbox

import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.InboxSource

internal enum class InboxSourceFilter(val source: InboxSource?) {
    All(null), Group(InboxSource.Group), GroupDirect(InboxSource.GroupDirect), Friend(InboxSource.Friend)
}

internal fun browseInbox(rows: List<InboxRow>, query: String,
    source: InboxSourceFilter = InboxSourceFilter.All): List<InboxRow> {
    val needle = query.trim()
    return rows.filter { row ->
        (source.source == null || row.source == source.source) &&
            (needle.isEmpty() || row.title.contains(needle, ignoreCase = true) ||
                row.lastBody?.contains(needle, ignoreCase = true) == true)
    }
}

internal fun totalInboxUnread(rows: List<InboxRow>): Long = rows.sumOf { it.unread.coerceAtLeast(0).toLong() }

internal fun formatInboxTime(time: Instant, zone: ZoneId): String =
    DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm").withZone(zone).format(time)
