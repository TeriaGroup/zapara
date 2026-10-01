package ru.bgtu_voenmeh.zapara.data

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.util.Locale

// Lecturer schedule from bundled assets (offline-first).
class LecturerStore(private val context: Context) {

    @Volatile
    private var data: ParsedLecturerSchedule? = null

    suspend fun load(): ParsedLecturerSchedule = withContext(Dispatchers.IO) {
        data ?: run {
            val xml = context.assets.open("TimetableLecturer50.xml")
                .bufferedReader(Charsets.UTF_8).readText()
            LecturerParser.parse(xml).also { data = it }
        }
    }

    fun lecturers(): List<LecturerInfo> = data?.lecturers.orEmpty()

    fun lessonsFor(lecturerId: String): List<LecturerLesson> =
        data?.lessons?.filter { it.lecturerId == lecturerId }
            ?.sortedWith(compareBy({ it.dayOfWeek }, { it.parity }, { it.timeStart }))
            .orEmpty()

    /** Ids + names of teachers leading the group, matched by last name and initials. */
    fun myTeacherIds(
        groupLessons: List<Lesson>,
        myGroupId: String? = null,
        myGroupName: String? = null
    ): Set<String> = TeacherMatch.myIds(groupLessons, lecturers(), { lessonsFor(it) }, myGroupId, myGroupName)

    fun search(query: String, onlyMy: Boolean, myIds: Set<String>): List<LecturerInfo> {
        var list = lecturers().asSequence()
        if (onlyMy) {
            list = list.filter { TeacherMatch.inMineList(it, myIds) }
        }
        val words = query.trim().lowercase(Locale.ROOT).replace('ё', 'е')
            .split(Regex("\\s+")).filter { it.isNotEmpty() }
        if (words.isNotEmpty()) {
            list = list.filter { l ->
                lecturerMatchesWords(words, listOf(l.name, l.id, l.kafedra) +
                    lessonsFor(l.id).map { it.disciplineRaw.ifBlank { it.subjectRaw } })
            }
        }
        // No cap: LazyColumn renders lazily, all 718 lecturers are fine (was take(100)).
        return list.sortedBy { it.name }.toList()
    }
}

internal fun lecturerMatchesWords(words: List<String>, fields: List<String>): Boolean {
    val haystack = fields.joinToString(" ").lowercase(Locale.ROOT).replace('ё', 'е')
    return words.all(haystack::contains)
}
