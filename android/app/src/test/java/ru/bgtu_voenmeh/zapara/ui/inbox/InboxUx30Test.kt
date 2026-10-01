package ru.bgtu_voenmeh.zapara.ui.inbox

import java.time.Instant
import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage

class InboxUx30Test {
    @Test fun draftFilterUsesCurrentConversationIdsAndCombinesWithUnread() {
        val rows = listOf(InboxRow("a", "Анна"), InboxRow("b", "Борис", unread = 2))
        assertEquals(listOf("b"), browseInbox(rows, "", unreadOnly = true,
            draftIds = setOf("a", "b", "missing"), draftsOnly = true).map { it.id })
        assertEquals(emptyList<InboxRow>(), browseInbox(rows, "", draftIds = emptySet(), draftsOnly = true))
    }

    @Test fun unreadFirstKeepsOrderWithinBothBucketsAndDoesNotMutateInput() {
        val rows = listOf(InboxRow("a", "А"), InboxRow("b", "Б", unread = 1),
            InboxRow("c", "В", unread = 9), InboxRow("d", "Г"))
        assertEquals(listOf("b", "c", "a", "d"), browseInbox(rows, "", unreadFirst = true).map { it.id })
        assertEquals(listOf("a", "b", "c", "d"), rows.map { it.id })
    }

    @Test fun inboxWordsCanMatchAcrossNameGroupAndPreview() {
        val row = InboxRow("a", "Анна", lastBody = "Всё готово", subtitle = "Личный чат · ИВТ")
        assertEquals(listOf(row), browseInbox(listOf(row), "ивт все анна"))
        assertEquals(emptyList<InboxRow>(), browseInbox(listOf(row), "ивт физика"))
    }

    @Test fun personalAuthorAndMediaFiltersComposeAndExcludeDeleted() {
        val mine = message("mine", "me", "file", "Лаба.pdf")
        val other = message("other", "peer", "image", "Лаба.png")
        val deleted = message("deleted", "me", "file", "Лаба.pdf").copy(deleted = true)
        val rows = listOf(mine, other, deleted)
        assertEquals(listOf(mine), browseLoadedPersonalHistory(rows, "лаба", PersonalHistoryKind.Documents,
            PersonalHistoryAuthor.Mine, "me"))
        assertEquals(listOf(other), browseLoadedPersonalHistory(rows, "", PersonalHistoryKind.PhotoVideo,
            PersonalHistoryAuthor.Others, "me"))
        assertEquals(rows, browseLoadedPersonalHistory(rows, ""))
    }

    private fun message(id: String, sender: String, kind: String, filename: String) = SocialMessage(
        id, sender, "Анна", null, kind, Instant.EPOCH, null, null, false, false,
        false, emptyList(), "attachment", filename)
}
