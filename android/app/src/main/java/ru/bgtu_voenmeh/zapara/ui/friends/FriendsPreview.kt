package ru.bgtu_voenmeh.zapara.ui.friends

import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.IntersectionService
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.LocalDate

object FriendsPreview {
    fun line(
        today: LocalDate,
        myGroupId: String,
        friends: List<Friend>,
        strictness: Int,
        periodStart: LocalDate,
        weekCount: Int,
        invert: Boolean,
        allForGroup: (String) -> List<Lesson>,
        resolveId: (String) -> String?,
        copy: UiCopy,
        horizonDays: Int = 14
    ): String {
        val none = copy.get("friends_preview_none")
        val enabled = friends.filter { it.enabled }
        if (myGroupId.isBlank() || enabled.isEmpty()) return none
        val mineAll = allForGroup(myGroupId)
        for (offset in 0..horizonDays) {
            val date = today.plusDays(offset.toLong())
            val day = Schedule.lessonsForDate(mineAll, myGroupId, date, periodStart, weekCount, invert)
            for (lesson in day) {
                val hits = IntersectionService.intersections(
                    my = lesson,
                    date = date,
                    friends = enabled,
                    strictness = strictness,
                    periodStart = periodStart,
                    weekCount = weekCount,
                    invert = invert,
                    lessonsFor = { fid, dow, parity ->
                        allForGroup(fid).filter { it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) }
                    },
                    resolveId = resolveId
                )
                if (hits.isEmpty()) continue
                val name = LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
                return copy.get(
                    "friends_preview",
                    LessonFormat.weekdayShort(date, copy),
                    LessonFormat.dayMonth(date),
                    lesson.timeStart,
                    name
                )
            }
        }
        return none
    }
}
