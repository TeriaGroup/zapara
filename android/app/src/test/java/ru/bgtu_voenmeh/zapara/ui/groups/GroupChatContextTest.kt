package ru.bgtu_voenmeh.zapara.ui.groups

import java.time.LocalDate
import java.time.LocalDateTime
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic

class GroupChatContextTest {
    @Test fun academicGroupMustMatchExactlyBeforeShowingItsLesson() {
        assertTrue(sameAcademicGroup(" Н162С ", "н162с"))
        assertFalse(sameAcademicGroup("Н162С", "Н163С"))
        assertFalse(sameAcademicGroup(null, "Н162С"))
    }

    @Test fun contextCountsOnlyRealChannelsAndHidesAnEmptyCard() {
        val general = topic(null, "chat", unread = 3)
        val polls = topic("polls", "ballots", ballots = 2)
        val aggregate = topic(null, "aggregate", unread = 50, ballots = 9)
        val hint = GroupLessonHint(LocalDate.of(2026, 9, 25), "12:40", "Физика", "311")
        val summary = groupChatContext(listOf(aggregate, general, polls), hint)
        assertEquals(3, summary.unread)
        assertEquals(2, summary.activeBallots)
        assertEquals(hint, summary.nextLesson)
        assertTrue(summary.hasContent)
        assertFalse(groupChatContext(listOf(aggregate), null).hasContent)
    }

    @Test fun nextLessonNeverReadsScheduleForAnotherGroup() {
        var reads = 0
        val now = LocalDateTime.of(2026, 9, 21, 11, 0)
        val lesson = Lesson(timeStart = "12:40", subjectNormalized = "Физика", roomRaw = "311")
        assertNull(nextGroupLesson("Н163С", "Н162С", now) { reads++; listOf(lesson) })
        assertEquals(0, reads)
        val matched = nextGroupLesson("Н162С", "н162с", now) { date ->
            reads++
            if (date == now.toLocalDate()) listOf(lesson) else emptyList()
        }
        assertEquals("Физика", matched?.subject)
        assertEquals("12:40", matched?.time)
        assertEquals(1, reads)
    }

    @Test fun anOngoingLessonRemainsTheNearestGroupLesson() {
        val now = LocalDateTime.of(2026, 9, 21, 13, 0)
        val lesson = Lesson(timeStart = "12:40", timeEnd = "14:15",
            subjectNormalized = "Физика", roomRaw = "311")
        val current = nextGroupLesson("Н162С", "Н162С", now) { date ->
            if (date == now.toLocalDate()) listOf(lesson) else emptyList()
        }
        assertEquals(now.toLocalDate(), current?.date)
        assertEquals("Физика", current?.subject)
    }

    private fun topic(id: String?, kind: String, unread: Int = 0, ballots: Int = 0) =
        GroupTopic(id, id ?: "Общее", "#", kind, null, null, null, unread, false, ballots)
}
