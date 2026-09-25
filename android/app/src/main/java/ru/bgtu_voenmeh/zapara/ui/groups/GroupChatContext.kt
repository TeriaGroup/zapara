package ru.bgtu_voenmeh.zapara.ui.groups

import java.time.LocalDate
import java.time.LocalDateTime
import java.time.DayOfWeek
import java.time.LocalTime
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic

data class GroupLessonHint(val date: LocalDate, val time: String, val subject: String, val room: String)
data class GroupChatContext(val nextLesson: GroupLessonHint?, val activeBallots: Int, val unread: Int) {
    val hasContent: Boolean get() = nextLesson != null || activeBallots > 0 || unread > 0
}

fun sameAcademicGroup(communityGroupName: String?, selectedGroupName: String?): Boolean {
    val community = communityGroupName?.trim().orEmpty()
    val selected = selectedGroupName?.trim().orEmpty()
    return community.isNotEmpty() && selected.isNotEmpty() && community.equals(selected, ignoreCase = true)
}

fun groupChatContext(channels: List<GroupTopic>, nextLesson: GroupLessonHint?): GroupChatContext {
    val real = channels.filter { it.kind == "chat" || it.topicId != null }
    return GroupChatContext(
        nextLesson,
        real.filter { it.kind == "ballots" }.sumOf { it.activeBallots.coerceAtLeast(0) },
        real.filter { it.kind == "chat" }.sumOf { it.unread.coerceAtLeast(0) }
    )
}

fun nextGroupLesson(
    communityGroupName: String?,
    selectedGroupName: String?,
    now: LocalDateTime,
    lessonsByDate: (LocalDate) -> List<Lesson>
): GroupLessonHint? {
    if (!sameAcademicGroup(communityGroupName, selectedGroupName)) return null
    for (offset in 0 until 14) {
        val date = now.toLocalDate().plusDays(offset.toLong())
        if (date.dayOfWeek == DayOfWeek.SUNDAY) continue
        val lessons = lessonsByDate(date).sortedWith(compareBy({ it.timeStart }, { it.index }))
        for (lesson in lessons) {
            val start = runCatching { LocalTime.parse(lesson.timeStart) }.getOrNull() ?: continue
            val startsAt = date.atTime(start)
            val endTime = runCatching { LocalTime.parse(lesson.timeEnd) }.getOrNull()
            val endsAt = endTime?.let {
                val parsed = date.atTime(it)
                if (parsed.isBefore(startsAt)) parsed.plusDays(1) else parsed
            } ?: startsAt.plusMinutes(95)
            if (offset > 0 || startsAt.isAfter(now) || endsAt.isAfter(now)) {
                return GroupLessonHint(date, lesson.timeStart,
                    lesson.subjectNormalized.ifBlank { lesson.subjectRaw }.trim(),
                    lesson.roomRaw.ifBlank { lesson.classroomRaw }.trim())
            }
        }
    }
    return null
}
