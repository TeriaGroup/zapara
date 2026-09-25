package ru.bgtu_voenmeh.zapara.ui.groups

import java.time.Instant
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic

class GroupMessageBrowseTest {
    private val mine = GroupMessageUi("mine", "Аня", "  Завтра ЛАБА  ", "12.09 10:00", true)
    private val other = GroupMessageUi("other", "Аня", "Лаба сегодня", "12.09 10:01", false)
    private val photo = GroupMessageUi("photo", "Борис", "Схема", "12.09 10:02", false, kind = "image")
    private val voice = GroupMessageUi("voice", "Борис", "voice.m4a", "12.09 10:03", false, kind = "voice")
    private val deleted = GroupMessageUi("deleted", "Борис", "Сообщение удалено", "12.09 10:04", false, deleted = true)

    @Test fun searchOnlyLoadedBodyIgnoresCaseAndKeepsServerOrder() {
        val rows = listOf(mine, other, photo)
        assertEquals(listOf("mine", "other"), browseMessages(rows, "  лАБа ").map { it.id })
        assertEquals(listOf("mine", "other", "photo"), rows.map { it.id })
        assertEquals(emptyList<GroupMessageUi>(), browseMessages(emptyList(), "лаба"))
    }

    @Test fun authorFilterUsesMineFlagInsteadOfDuplicateDisplayName() {
        val rows = listOf(mine, other)
        assertEquals(listOf("mine"), browseMessages(rows, "", MessageAuthor.Mine).map { it.id })
        assertEquals(listOf("other"), browseMessages(rows, "", MessageAuthor.Others).map { it.id })
    }

    @Test fun kindFilterGroupsMediaAndKeepsDeletedTombstoneOnlyInUnfilteredView() {
        val rows = listOf(mine, photo, voice, deleted)
        assertEquals(listOf("photo"), browseMessages(rows, "", kind = MessageKind.PhotoVideo).map { it.id })
        assertEquals(listOf("voice"), browseMessages(rows, "", kind = MessageKind.VoiceCircle).map { it.id })
        assertEquals(listOf("mine"), browseMessages(rows, "", kind = MessageKind.Text).map { it.id })
        assertEquals(listOf("mine", "photo", "voice", "deleted"), browseMessages(rows, "").map { it.id })
        assertEquals(emptyList<GroupMessageUi>(), browseMessages(rows, "удалено"))
    }

    @Test fun copyingAllowsOnlyVisibleNonemptyTextBody() {
        assertEquals("  Завтра ЛАБА  ", copyableMessageText(mine))
        assertNull(copyableMessageText(photo))
        assertNull(copyableMessageText(deleted))
        assertNull(copyableMessageText(GroupMessageUi("blank", "Аня", "  ", "12.09 10:05", true)))
    }

    @Test fun nextUnreadChoosesRealChannelAndSkipsCurrent() {
        val aggregate = topic("aggregate", "Все голосования", 9).copy(kind = "aggregate")
        val general = topic(null, "Общее", 2)
        val first = topic("one", "Новости", 1)
        val second = topic("two", "Вопросы", 3)
        assertEquals(first, nextUnreadChannel(listOf(general, first, second), activeTopicId = null, inChannel = true))
        assertEquals(general, nextUnreadChannel(listOf(aggregate, general, first, second), activeTopicId = null, inChannel = false))
        assertEquals(second, nextUnreadChannel(listOf(general, first, second), activeTopicId = "one", inChannel = true))
        assertNull(nextUnreadChannel(listOf(topic("empty", "Пусто", 0)), activeTopicId = null, inChannel = false))
    }

    @Test fun adjacentMessagesClusterOnlyForSameSenderLocalDayAndFiveMinuteGap() {
        val first = mine.copy(senderId = "sender-one", day = "25.09.2026",
            createdAt = Instant.parse("2026-09-25T12:00:00Z"))
        val nearby = other.copy(senderId = "sender-one", day = "25.09.2026",
            createdAt = Instant.parse("2026-09-25T12:04:00Z"))
        assertEquals(true, sameMessageCluster(first, nearby))
        assertEquals(false, sameMessageCluster(first, nearby.copy(senderId = "sender-two")))
        assertEquals(false, sameMessageCluster(first, nearby.copy(day = "26.09.2026")))
        assertEquals(false, sameMessageCluster(first, nearby.copy(createdAt = Instant.parse("2026-09-25T12:06:00Z"))))
        assertEquals(false, sameMessageCluster(nearby, first))
        assertEquals(false, sameMessageCluster(null, nearby))
    }

    private fun topic(id: String?, title: String, unread: Int) = GroupTopic(
        id, title, "💬", "chat", null, null, null, unread, false, 0
    )
}
