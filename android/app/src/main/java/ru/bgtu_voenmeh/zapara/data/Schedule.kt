package ru.bgtu_voenmeh.zapara.data

import java.time.DayOfWeek
import java.time.LocalDate

object Schedule {

    /** Lessons for [groupId] on [date] filtered by computed parity. */
    fun lessonsForDate(
        all: List<Lesson>,
        groupId: String,
        date: LocalDate,
        periodStart: LocalDate,
        weekCount: Int = 2,
        invert: Boolean = false
    ): List<Lesson> {
        if (date.dayOfWeek == DayOfWeek.SUNDAY) return emptyList()
        val dow = date.dayOfWeek.value // Mon=1..Sat=6
        var code = Parity.weekCode(date, periodStart, weekCount)
        if (invert) code = if (code == 1) 2 else 1
        return all.filter { it.groupId == groupId && it.dayOfWeek == dow && (it.parity == code || it.parity == 0) }
            .sortedWith(compareBy({ it.timeStart }, { it.index }))
    }

    /** Day after [from], through +60, skipping Sunday. */
    fun nextOccurrenceBySubject(
        all: List<Lesson>,
        groupId: String,
        norm: String,
        from: LocalDate,
        periodStart: LocalDate,
        weekCount: Int = 2,
        invert: Boolean = false,
        maxDays: Int = 60
    ): LocalDate? {
        if (norm.isBlank()) return null
        for (offset in 1..maxDays) {
            val date = from.plusDays(offset.toLong())
            if (date.dayOfWeek == DayOfWeek.SUNDAY) continue
            val dayLessons = lessonsForDate(all, groupId, date, periodStart, weekCount, invert)
            if (dayLessons.any { Parity.sameSubject(it.subjectNormalized, norm) }) return date
        }
        return null
    }

    /** Same +60 Sunday-skipping scan, matching the teacher short name. */
    fun nextOccurrenceByTeacher(
        all: List<Lesson>,
        groupId: String,
        teacher: String?,
        from: LocalDate,
        periodStart: LocalDate,
        weekCount: Int = 2,
        invert: Boolean = false,
        maxDays: Int = 60
    ): LocalDate? {
        val t = teacher?.trim().orEmpty()
        if (t.isEmpty() || t == "—") return null
        val teachNorm = t.split(";")[0].trim().lowercase()
        for (offset in 1..maxDays) {
            val date = from.plusDays(offset.toLong())
            if (date.dayOfWeek == DayOfWeek.SUNDAY) continue
            val dayLessons = lessonsForDate(all, groupId, date, periodStart, weekCount, invert)
            for (l in dayLessons) {
                if (l.teacherRaw.isBlank()) continue
                val lNorm = l.teacherRaw.split(";")[0].trim().lowercase()
                if (lNorm == teachNorm || l.teacherRaw.lowercase().contains(teachNorm) || teachNorm.contains(lNorm)) {
                    return date
                }
            }
        }
        return null
    }
}
