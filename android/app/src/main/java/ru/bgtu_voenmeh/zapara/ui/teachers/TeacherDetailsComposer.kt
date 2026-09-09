package ru.bgtu_voenmeh.zapara.ui.teachers

import ru.bgtu_voenmeh.zapara.data.LecturerLesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy

data class TeacherRowLesson(
    val time: String,
    val subject: String,
    val groups: String,
    val room: String,
    val isMyGroup: Boolean,
    val parity: Int
)

data class TeacherDayUi(val dow: Int, val title: String, val rows: List<TeacherRowLesson>)

object TeacherDetailsComposer {
    fun compose(lessons: List<LecturerLesson>, parityFilter: Int, myGroupId: String, copy: UiCopy): List<TeacherDayUi> {
        val filtered = lessons.filter { lesson ->
            when (parityFilter) {
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
                        time = lesson.timeStart,
                        subject = LessonFormat.stripType(lesson.disciplineRaw.ifBlank { lesson.subjectRaw }, lesson.typeRaw),
                        groups = lesson.groups.joinToString(", ") { it.number },
                        room = LessonFormat.roomLabel(lesson.roomRaw, lesson.buildingRaw, lesson.classroomRaw, copy),
                        isMyGroup = lesson.groups.any { it.idGroup == myGroupId },
                        parity = lesson.parity
                    )
                }
            )
        }.filter { it.rows.isNotEmpty() }
    }
}
