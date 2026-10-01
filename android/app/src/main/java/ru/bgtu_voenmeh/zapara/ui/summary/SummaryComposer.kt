package ru.bgtu_voenmeh.zapara.ui.summary

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.MapResolve
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy

data class SummaryTiles(
    val total: Int,
    val byType: List<Pair<String, Int>>,
    val bySubject: List<Pair<String, Int>>,
    val byTeacher: List<Pair<String, Int>>,
    val rooms: List<String>,
    val byDay: List<Pair<Int, Int>> = emptyList(),
    val byRoom: List<Pair<String, Int>> = emptyList(),
    val subjectNormByLabel: Map<String, String> = emptyMap(),
    val teacherIdByLabel: Map<String, String> = emptyMap(),
    val roomRawByLabel: Map<String, String> = emptyMap()
)

object SummaryComposer {
    fun tiles(segment: Int, lessons: List<Lesson>, displayName: (norm: String, dow: Int) -> String,
        copy: UiCopy, teacherIdentity: (String) -> Pair<String, String> = { "raw:$it" to it }): SummaryTiles {
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
        fun subjectLabel(lesson: Lesson) = displayName(lesson.subjectNormalized, lesson.dayOfWeek)
            .ifBlank { LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw) }
        val bySubject = counts(::subjectLabel)
        val subjectLookup = filtered.groupBy(::subjectLabel).mapNotNull { (label, rows) ->
            rows.map { it.subjectNormalized }.filter(String::isNotBlank).distinct().singleOrNull()?.let { label to it }
        }.toMap()
        val teacherRows = filtered
            .filter { it.teacherRaw.isNotBlank() && it.teacherRaw != "—" }
            .flatMap { it.teacherRaw.split(";").map { part -> part.trim() }.filter { part -> part.isNotEmpty() } }
            .map(teacherIdentity)
        val teacherNames = teacherRows.associate { it.first to it.second }
        val byTeacher = teacherRows
            .map { it.first }
            .groupingBy { it }
            .eachCount()
            .map { (key, count) -> (teacherNames[key] ?: key) to count }
            .sortedWith(compareByDescending<Pair<String, Int>> { it.second }.thenBy { it.first })
        val teacherLookup = teacherRows.groupBy { it.second }.mapNotNull { (label, rows) ->
            rows.mapNotNull { (key, _) -> key.removePrefix("teacher:").takeIf { key.startsWith("teacher:") } }
                .distinct().singleOrNull()?.let { label to it }
        }.toMap()
        val byDay = (1..if (filtered.any { it.dayOfWeek == 7 }) 7 else 6)
            .map { day -> day to filtered.count { it.dayOfWeek == day } }
        val byRoom = counts {
            val room = it.roomRaw.trim().ifBlank { it.classroomRaw.trim().trimEnd(';').replace("*", "").trim() }
            if (room.isBlank() || room == "—") "" else LessonFormat.roomLabel(it, copy)
        }
        val roomLookup = filtered.mapNotNull { lesson ->
            val room = lesson.roomRaw.trim().ifBlank { lesson.classroomRaw.trim().trimEnd(';').replace("*", "").trim() }
            if (room.isBlank() || room == "—") null
            else LessonFormat.roomLabel(lesson, copy) to lesson.classroomRaw.trim()
        }.groupBy { it.first }.mapNotNull { (label, rows) ->
            rows.map { it.second }.filter(String::isNotBlank).distinct().singleOrNull()
                ?.takeIf { raw -> runCatching { MapResolve.resolve(raw)?.hasMap == true }.getOrDefault(false) }
                ?.let { label to it }
        }.toMap()
        return SummaryTiles(filtered.size, byType.filter { it.first != "—" }, bySubject, byTeacher,
            byRoom.map { it.first }, byDay, byRoom, subjectLookup, teacherLookup, roomLookup)
    }
}
