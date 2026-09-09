package ru.bgtu_voenmeh.zapara.ui.summary

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy

data class SummaryTiles(
    val total: Int,
    val byType: List<Pair<String, Int>>,
    val bySubject: List<Pair<String, Int>>,
    val byTeacher: List<Pair<String, Int>>,
    val rooms: List<String>
)

object SummaryComposer {
    fun tiles(segment: Int, lessons: List<Lesson>, displayName: (norm: String, dow: Int) -> String, copy: UiCopy): SummaryTiles {
        val filtered = when (segment) {
            0 -> lessons.filter { it.parity == 0 || it.parity == 1 }
            1 -> lessons.filter { it.parity == 0 || it.parity == 2 }
            else -> lessons
        }
        fun counts(key: (Lesson) -> String) = filtered
            .map(key)
            .filter { it.isNotBlank() && it != "—" }
            .groupingBy { it }
            .eachCount()
            .toList()
            .sortedWith(compareByDescending<Pair<String, Int>> { it.second }.thenBy { it.first })
        val byType = counts { LessonFormat.typeLabel(it.typeRaw, copy).ifBlank { "—" } }
        val bySubject = counts { displayName(it.subjectNormalized, it.dayOfWeek).ifBlank { LessonFormat.stripType(it.subjectRaw, it.typeRaw) } }
        val byTeacher = filtered
            .filter { it.teacherRaw.isNotBlank() && it.teacherRaw != "—" }
            .flatMap { it.teacherRaw.split(";").map { part -> part.trim() }.filter { part -> part.isNotEmpty() } }
            .groupingBy { it }
            .eachCount()
            .toList()
            .sortedWith(compareByDescending<Pair<String, Int>> { it.second }.thenBy { it.first })
        val rooms = counts { LessonFormat.roomLabel(it, copy) }.map { it.first }
        return SummaryTiles(filtered.size, byType.filter { it.first != "—" }, bySubject, byTeacher, rooms)
    }
}
