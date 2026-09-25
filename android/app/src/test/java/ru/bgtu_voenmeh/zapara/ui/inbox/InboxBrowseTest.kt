package ru.bgtu_voenmeh.zapara.ui.inbox

import java.time.Instant
import java.time.ZoneId
import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.InboxSource

class InboxBrowseTest {
    private val group = InboxRow("group", "О3313", "community", "Новости", unread = 2,
        source = InboxSource.Group)
    private val groupDirect = InboxRow("direct", "Борис", "community", "Лабораторная", unread = 3,
        source = InboxSource.GroupDirect)
    private val friend = InboxRow("friend", "Аня", lastBody = "Встретимся", unread = 1,
        source = InboxSource.Friend)

    @Test fun searchIsTrimmedCaseInsensitiveAndUsesTitleOrLastBody() {
        val rows = listOf(group, groupDirect, friend)
        assertEquals(listOf("direct"), browseInbox(rows, "  лАбоРАторНАЯ ").map { it.id })
        assertEquals(listOf("friend"), browseInbox(rows, " АНЯ ").map { it.id })
        assertEquals(emptyList<InboxRow>(), browseInbox(emptyList(), "аня"))
        assertEquals(listOf("group", "direct", "friend"), rows.map { it.id })
    }

    @Test fun sourceFilterUsesStructuredKindInsteadOfLocalizedSubtitle() {
        val rows = listOf(group, groupDirect, friend)
        assertEquals(listOf("group"), browseInbox(rows, "", InboxSourceFilter.Group).map { it.id })
        assertEquals(listOf("direct"), browseInbox(rows, "", InboxSourceFilter.GroupDirect).map { it.id })
        assertEquals(listOf("friend"), browseInbox(rows, "", InboxSourceFilter.Friend).map { it.id })
        assertEquals(listOf("group", "direct", "friend"), browseInbox(rows, "").map { it.id })
    }

    @Test fun unreadTotalAndLastEventTimeUseAllRowsAndLocalZone() {
        assertEquals(6L, totalInboxUnread(listOf(group, groupDirect, friend)))
        assertEquals("26.09.2026 00:30", formatInboxTime(
            Instant.parse("2026-09-25T21:30:00Z"), ZoneId.of("Europe/Moscow")))
    }
}
