package ru.bgtu_voenmeh.zapara.data

import java.time.LocalDate
import java.time.format.TextStyle
import java.util.Locale

// Pure port of NotificationService.BuildNotificationText (no Android deps).
object NotificationText {

    fun build(
        date: LocalDate,
        groupId: String?,
        lessons: List<Lesson>,
        displayOf: (Lesson) -> String,
        burningMark: (Lesson) -> String?,
        isOdd: Boolean,
        dayName: (LocalDate) -> String,
        parityName: (Boolean) -> String,
        noLessonsText: String
    ): String {
        if (groupId.isNullOrEmpty()) return noLessonsText
        val parityStr = parityName(isOdd)
        val dayStr = dayName(date)
        if (lessons.isEmpty()) return "$dayStr, $parityStr: $noLessonsText"
        val sb = StringBuilder()
        sb.append("$dayStr, $parityStr: ")
        var n = 1
        for (l in lessons.sortedBy { it.timeStart }) {
            val display = displayOf(l)
            val hwMark = burningMark(l)?.let { " $it" }.orEmpty()
            sb.append("${n++}. $display ${l.classroomRaw}$hwMark; ")
        }
        return sb.toString().trimEnd(' ', ';')
    }

    fun localDayName(date: LocalDate): String {
        val loc = Locale("ru")
        return date.dayOfWeek.getDisplayName(TextStyle.FULL, loc)
            .replaceFirstChar { it.uppercase() }
    }
}
