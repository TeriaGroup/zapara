package ru.bgtu_voenmeh.zapara.ui.friends

import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.Intersection
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime

data class FriendEncounter(
    val date: LocalDate,
    val time: String,
    val subject: String,
    val groupName: String,
    val members: String,
    val friendRoom: String,
    val colorHex: String,
    val score: Int
)

data class FriendsForecast(
    val encounters: List<FriendEncounter>,
    val missingGroups: List<String>,
    val checkedGroups: Int
)

object FriendsPreview {
    fun forecast(
        now: LocalDateTime,
        myGroupId: String,
        friends: List<Friend>,
        strictness: Int,
        periodStart: LocalDate,
        weekCount: Int,
        invert: Boolean,
        allForGroup: (String) -> List<Lesson>,
        resolveId: (String) -> String?,
        hasSchedule: (String) -> Boolean = { true },
        canIntersect: (String, String) -> Boolean = { _, _ -> true },
        horizonDays: Int = 14
    ): FriendsForecast {
        val enabled = friends.filter { it.enabled }.take(5)
        if (myGroupId.isBlank() || enabled.isEmpty()) return FriendsForecast(emptyList(), emptyList(), 0)
        val cache = mutableMapOf<String, List<Lesson>>()
        fun lessons(id: String) = cache.getOrPut(id) { allForGroup(id) }
        val ready = enabled.map { friend ->
            val id = resolveId(friend.groupName)
            Triple(friend, id, id != null && id != myGroupId && hasSchedule(id) && canIntersect(myGroupId, id))
        }
        val missing = ready.filter { !it.third }.map { it.first.groupName }
        val encounters = mutableListOf<FriendEncounter>()
        val mine = lessons(myGroupId)
        for (offset in 0 until horizonDays.coerceIn(0, 14)) {
            val date = now.toLocalDate().plusDays(offset.toLong())
            val own = Schedule.lessonsForDate(mine, myGroupId, date, periodStart, weekCount, invert)
            for (lesson in own) {
                val start = runCatching { LocalTime.parse(lesson.timeStart) }.getOrNull() ?: continue
                val end = runCatching { LocalTime.parse(lesson.timeEnd) }.getOrNull() ?: start.plusMinutes(95)
                if (offset == 0 && !end.isAfter(now.toLocalTime())) continue
                for ((friend, id, available) in ready) {
                    if (!available || id == null) continue
                    val others = Schedule.lessonsForDate(lessons(id), id, date, periodStart, weekCount, invert)
                    val best = others.asSequence()
                        .filter { Intersection.timesOverlap(lesson.timeStart, lesson.timeEnd, it.timeStart, it.timeEnd) }
                        .map { it to Intersection.scoreOf(lesson.roomRaw, lesson.buildingRaw, it.roomRaw, it.buildingRaw) }
                        .maxByOrNull { it.second } ?: continue
                    if (best.second < strictness) continue
                    encounters += FriendEncounter(
                        date, lesson.timeStart, LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw),
                        friend.groupName, friend.memberNames, best.first.roomRaw.ifBlank { best.first.classroomRaw },
                        friend.colorHex, best.second
                    )
                    if (encounters.size == 3) return FriendsForecast(encounters, missing, ready.size - missing.size)
                }
            }
        }
        return FriendsForecast(encounters, missing, ready.size - missing.size)
    }

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
        val first = forecast(today.atStartOfDay(), myGroupId, friends, strictness, periodStart, weekCount,
            invert, allForGroup, resolveId, horizonDays = horizonDays).encounters.firstOrNull()
            ?: return copy.get("friends_preview_none")
        return copy.get("friends_preview", LessonFormat.weekdayShort(first.date, copy),
            LessonFormat.dayMonth(first.date), first.time, first.subject)
    }
}
