package ru.bgtu_voenmeh.zapara.ui.calendar

import ru.bgtu_voenmeh.zapara.ui.schedule.DayPage
import ru.bgtu_voenmeh.zapara.ui.week.WeekDayUi
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

data class CalendarLesson(
    val date: LocalDate,
    val start: String,
    val end: String,
    val summary: String,
    val subjectNorm: String,
    val location: String = "",
    val subjectRaw: String = summary,
    val teacherRaw: String = "",
    val classroomRaw: String = ""
)

object CalendarLessonExport {
    private val campusZone = ZoneId.of("Europe/Moscow")
    private val dayFormat = DateTimeFormatter.ofPattern("d MMMM yyyy", Locale.forLanguageTag("ru"))
    fun canonicalId(groupId: String, subjectRaw: String, teacherRaw: String,
        classroomRaw: String): String = listOf(groupId, subjectRaw, teacherRaw, classroomRaw)
        .joinToString("") { "${it.length}:$it" }

    fun week(days: List<WeekDayUi>): List<CalendarLesson> = days.flatMap { day -> day.rows.map { row ->
        CalendarLesson(day.date, row.start, row.end, row.name, row.subjectNorm, row.room,
            row.subjectRaw, row.teacherRaw, row.classroomRaw)
    } }

    fun day(page: DayPage): List<CalendarLesson> = page.lessons.map { lesson ->
        CalendarLesson(page.date, lesson.timeStart, lesson.timeEnd, lesson.name,
            lesson.subjectNorm, lesson.room, lesson.subjectRaw,
            lesson.teacher.takeUnless { it == "—" }.orEmpty(), lesson.classroomRaw)
    }

    fun ics(lessons: List<CalendarLesson>, groupId: String, name: String,
        generatedAt: Instant): CalendarExportResult {
        var malformed = 0
        val entries = lessons.mapNotNull { lesson ->
            val start = runCatching { LocalTime.parse(lesson.start) }.getOrNull()
            val end = runCatching { LocalTime.parse(lesson.end) }.getOrNull()
            if (start == null || end == null) {
                malformed++
                null
            } else {
                val id = canonicalId(groupId, lesson.subjectRaw, lesson.teacherRaw, lesson.classroomRaw)
                CalendarEntry(id, lesson.date.atTime(start).atZone(campusZone).toInstant(),
                    lesson.date.atTime(end).atZone(campusZone).toInstant(), lesson.summary,
                    lesson.location.takeIf(String::isNotBlank))
            }
        }
        val result = CalendarExport.create(entries, name, generatedAt)
        return result.copy(skippedCount = result.skippedCount + malformed)
    }

    fun plainText(name: String, lessons: List<CalendarLesson>, emptyLabel: String = ""): String = buildString {
        append(name.trim())
        if (lessons.isEmpty() && emptyLabel.isNotBlank()) { append("\n"); append(emptyLabel) }
        lessons.groupBy { it.date }.toSortedMap().forEach { (date, dayLessons) ->
            append("\n\n")
            append(date.format(dayFormat))
            dayLessons.sortedBy { it.start }.forEach { lesson ->
                append("\n")
                append(lesson.start)
                append("–")
                append(lesson.end)
                append(" · ")
                append(singleLine(lesson.summary))
                if (lesson.location.isNotBlank()) {
                    append(" · ")
                    append(singleLine(lesson.location))
                }
            }
        }
    }

    private fun singleLine(value: String): String = value.replace(Regex("[\\r\\n]+"), " ").trim()
}
