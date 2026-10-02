package ru.bgtu_voenmeh.zapara.ui.teachers

import ru.bgtu_voenmeh.zapara.data.LecturerLesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import java.time.LocalDate
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy

data class TeacherRowLesson(
    val time: String,
    val subject: String,
    val groups: String,
    val room: String,
    val isMyGroup: Boolean,
    val parity: Int,
    val date: LocalDate? = null,
    val classroomRaw: String = ""
)

data class TeacherDayUi(val dow: Int, val title: String, val rows: List<TeacherRowLesson>,
    val date: LocalDate? = null)

object TeacherDetailsComposer {
    fun parityLabel(parity: Int, copy: UiCopy): String = copy.get(when (parity) {
        0 -> "teacher_parity_both"
        1 -> "teacher_parity_odd"
        2 -> "teacher_parity_even"
        else -> "teacher_parity_unknown"
    })

    fun compose(lessons: List<LecturerLesson>, parityFilter: Int, myGroupId: String, copy: UiCopy,
        invert: Boolean = false, today: LocalDate? = null, context: SchedCtx? = null): List<TeacherDayUi> {
        val storedFilter = if (invert && parityFilter in 1..2) 3 - parityFilter else parityFilter
        val filtered = lessons.filter { lesson ->
            when (storedFilter) {
                1 -> lesson.parity == 0 || lesson.parity == 1
                2 -> lesson.parity == 0 || lesson.parity == 2
                else -> true
            }
        }
        return filtered.groupBy { it.dayOfWeek }.toSortedMap().map { (dow, dayLessons) ->
            TeacherDayUi(
                dow = dow,
                title = Parity.dayNumberToTitle(dow),
                rows = dayLessons.sortedBy { it.timeStart }.map { lesson ->
                    TeacherRowLesson(
                        time = listOf(lesson.timeStart, lesson.timeEnd).filter(String::isNotBlank).joinToString("–"),
                        subject = LessonFormat.stripType(lesson.disciplineRaw.ifBlank { lesson.subjectRaw }, lesson.typeRaw),
                        groups = lesson.groups.joinToString(", ") { it.number },
                        room = LessonFormat.roomLabel(lesson.roomRaw, lesson.buildingRaw, lesson.classroomRaw, copy),
                        isMyGroup = lesson.groups.any { it.idGroup == myGroupId || it.number == myGroupId },
                        parity = if (invert && lesson.parity in 1..2) 3 - lesson.parity else lesson.parity,
                        date = if (today != null && context != null) teacherNextDate(
                            lesson.dayOfWeek, lesson.parity, today, context) else null,
                        classroomRaw = lesson.classroomRaw
                    )
                }
            )
        }.map { day -> day.copy(date = day.rows.mapNotNull { it.date }.minOrNull()) }
            .filter { it.rows.isNotEmpty() }
    }
}

internal fun teacherNextDate(dayOfWeek: Int, storedParity: Int, today: LocalDate,
    context: SchedCtx): LocalDate? {
    if (dayOfWeek !in 1..7) return null
    for (offset in 0..(7 * maxOf(2, context.weekCount * 2))) {
        val date = today.plusDays(offset.toLong())
        if (date.dayOfWeek.value != dayOfWeek) continue
        var code = Parity.weekCode(date, context.periodStart, context.weekCount)
        if (context.invert) code = if (code == 1) 2 else 1
        if (storedParity == 0 || storedParity == code) return date
    }
    return null
}
