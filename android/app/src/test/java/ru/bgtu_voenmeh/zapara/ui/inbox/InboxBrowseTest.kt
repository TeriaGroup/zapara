package ru.bgtu_voenmeh.zapara.ui.inbox

import java.time.Instant
import java.time.ZoneId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.InboxSource
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage

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

    @Test fun unreadFilterCombinesWithSourceAndQueryWithoutChangingTotals() {
        val rows = listOf(group, groupDirect.copy(unread = 0), friend)
        assertEquals(listOf("group"), browseInbox(rows, "новости", InboxSourceFilter.Group, unreadOnly = true).map { it.id })
        assertEquals(emptyList<InboxRow>(), browseInbox(rows, "лабораторная", InboxSourceFilter.GroupDirect, unreadOnly = true))
        assertEquals(2, unreadInboxConversations(rows))
        assertEquals(3L, totalInboxUnread(rows))
    }

    private fun message(id: String, body: String?, kind: String = "text", deleted: Boolean = false,
        fileName: String? = null) = SocialMessage(
        id = id, senderId = "me", senderName = "Я", body = body, kind = kind,
        createdAt = Instant.parse("2026-09-30T10:00:00Z"), replyTo = null, replyBody = null,
        deleted = deleted, edited = false, read = false, reactions = emptyList(),
        attachmentId = null, fileName = fileName
    )

    @Test fun localHistorySearchAndCopyIgnoreDeletedAndNonTextMessages() {
        val rows = listOf(message("a", "Задачи по матану").copy(senderName = "Анна Иванова"), message("b", "Задачи по физике", deleted = true),
            message("c", "https://service.invalid/media/c", kind = "image", fileName = "Схема аудитории.png"))
        assertEquals(listOf("a"), browseLoadedPersonalHistory(rows, "  МАТАНУ задачи ").map { it.id })
        assertEquals(listOf("a"), browseLoadedPersonalHistory(rows, "Иванова").map { it.id })
        assertEquals(listOf("c"), browseLoadedPersonalHistory(rows, "аудитории.png").map { it.id })
        assertEquals(emptyList<SocialMessage>(), browseLoadedPersonalHistory(rows, "service.invalid"))
        assertEquals(3, browseLoadedPersonalHistory(rows, "").size)
        assertEquals("Задачи по матану", copyablePersonalText(rows[0]))
        assertNull(copyablePersonalText(rows[1]))
        assertNull(copyablePersonalText(rows[2]))
        assertNull(copyablePersonalText(message("d", "   ")))
    }

    @Test fun russian_search_normalizes_yo_and_ye_in_query_and_fields() {
        val rows = listOf(message("yo", "Всё готово"), message("ye", "Все готово"))
        assertEquals(listOf("yo", "ye"), browseLoadedPersonalHistory(rows, "все").map { it.id })
        assertEquals(listOf("yo", "ye"), browseLoadedPersonalHistory(rows, "всё").map { it.id })
        assertEquals(listOf("friend"), browseInbox(listOf(friend.copy(title = "Ёлка")), "елка").map { it.id })
    }

    @Test fun composer_sessions_expose_only_nonempty_drafts_per_conversation() {
        val sessions = PersonalComposerSessions()
        sessions.save("one", PersonalComposer().type("  Мой текст  "))
        sessions.save("two", PersonalComposer())
        assertEquals(mapOf("one" to "Мой текст"), sessions.draftPreviews())
        assertEquals(PersonalComposer().type("  Мой текст  "), sessions.restore("one"))
        assertEquals(emptyMap<String, String>(), PersonalComposerSessions().draftPreviews())
    }

    @Test fun quote_lookup_stays_in_loaded_conversation_even_when_search_hides_target() {
        val rows = listOf(message("earlier", "Физика"), message("later", "Математика"),
            message("deleted", "Было", deleted = true))
        assertEquals(listOf("later"), browseLoadedPersonalHistory(rows, "Математика").map { it.id })
        assertEquals(PersonalQuoteTarget.Loaded, personalQuoteTarget(rows, "earlier"))
        assertEquals(0, browseLoadedPersonalHistory(rows, "").indexOfFirst { it.id == "earlier" })
        assertEquals(PersonalQuoteTarget.Deleted, personalQuoteTarget(rows, "deleted"))
        assertEquals(PersonalQuoteTarget.Earlier, personalQuoteTarget(rows, "not-loaded"))
    }
}
