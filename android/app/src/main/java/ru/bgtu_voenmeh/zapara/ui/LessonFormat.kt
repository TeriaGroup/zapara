package ru.bgtu_voenmeh.zapara.ui

import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.Intersection
import ru.bgtu_voenmeh.zapara.data.Lesson
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.time.format.TextStyle
import java.util.Locale

object LessonFormat {
    private val RU = Locale("ru")
    private val MONTH_DAY = DateTimeFormatter.ofPattern("d MMMM", RU)
    private val TYPE_TOKENS = setOf("лек", "пр", "лаб", "конс", "зач", "экз", "курс", "практика")

    fun stripType(name: String, typeRaw: String): String {
        val t = typeRaw.trim()
        return if (t.isNotEmpty() && name.length > t.length + 1 && name.startsWith("$t ", ignoreCase = true)) {
            name.substring(t.length + 1).trim()
        } else {
            val parts = name.trim().split(Regex("\\s+"), limit = 2)
            if (parts.size == 2 && parts[0].lowercase(RU) in TYPE_TOKENS) parts[1] else name
        }
    }

    fun typeLabel(typeRaw: String, copy: UiCopy): String = when (typeRaw.trim().lowercase(RU)) {
        "лек" -> copy.get("type_lecture")
        "пр" -> copy.get("type_practice")
        "лаб" -> copy.get("type_lab")
        "конс" -> copy.get("type_consult")
        "зач" -> copy.get("type_credit")
        "экз" -> copy.get("type_exam")
        "курс" -> copy.get("type_course")
        "практика" -> copy.get("type_practice")
        "" -> ""
        else -> typeRaw.trim()
    }

    fun roomLabel(lesson: Lesson, copy: UiCopy): String =
        roomLabel(lesson.roomRaw, lesson.buildingRaw, lesson.classroomRaw, copy)

    fun roomLabel(roomRaw: String, buildingRaw: String, classroomRaw: String, copy: UiCopy): String {
        if (isRemote(classroomRaw) || roomRaw.contains("дистанционно", ignoreCase = true)) return copy.get("remote_room")
        val building = when {
            buildingRaw.isNotBlank() -> buildingRaw
            classroomRaw.contains("*") -> "УЛК"
            classroomRaw.contains("ВЦ", ignoreCase = true) -> "ВЦ"
            else -> "ГК"
        }
        val room = roomRaw.trim().ifBlank { classroomRaw.trim().trimEnd(';').replace("*", "").trim() }
        return "$room $building".trim()
    }

    fun isRemote(classroomRaw: String?): Boolean =
        classroomRaw.orEmpty().contains("дистанционно", ignoreCase = true)

    fun weekdayShort(date: LocalDate, copy: UiCopy): String = weekdayShort(date.dayOfWeek.value, copy)

    fun weekdayShort(dow: Int, copy: UiCopy): String = if (dow in 1..7) copy.get("weekday_$dow") else ""

    fun weekdayFull(date: LocalDate): String =
        date.dayOfWeek.getDisplayName(TextStyle.FULL, RU).replaceFirstChar { it.titlecase(RU) }

    fun monthDay(date: LocalDate): String = date.format(MONTH_DAY)

    fun dayMonth(date: LocalDate): String = "%02d.%02d".format(date.dayOfMonth, date.monthValue)

    fun caption(date: LocalDate, odd: Boolean, weekNumber: Int, copy: UiCopy): String {
        val parity = copy.get(if (odd) "parity_week_odd" else "parity_week_even")
        return copy.get("schedule_caption", weekdayFull(date), monthDay(date), parity, weekNumber)
    }

    fun nextHint(date: LocalDate, name: String, copy: UiCopy): String =
        copy.get("next_lesson_hint", monthDay(date), name)

    fun hwCardLabel(hw: Homework, copy: UiCopy): String = when (hw.status) {
        "done" -> copy.get("hw_done")
        "overdue" -> copy.get("hw_overdue")
        "burning", "burning_urgent" -> copy.get("hw_burning")
        else -> hw.due?.let { copy.get("hw_due", dayMonth(it), weekdayShort(it, copy)) } ?: copy.get("hw_due_none")
    }

    fun friendHint(members: String, group: String, score: Int, copy: UiCopy): String {
        val scoreText = if (score >= 100) copy.get("friend_same_room") else Intersection.scoreToTextRu(score)
        val who = members.ifBlank { group }
        return copy.get("friend_hint", who, group, scoreText)
    }
}
