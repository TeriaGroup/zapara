package ru.bgtu_voenmeh.zapara.data

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

// Lecturer schedule from bundled assets (offline-first). Network refresh lands in A5.
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

    fun isLoaded(): Boolean = data != null

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
        val q = query.trim().lowercase()
        if (q.isNotEmpty()) {
            list = list.filter { l ->
                l.name.lowercase().contains(q) || l.id.contains(q) ||
                    l.kafedra.lowercase().contains(q) ||
                    lessonsFor(l.id).any { it.disciplineRaw.lowercase().contains(q) }
            }
        }
        // No cap: LazyColumn renders lazily, all 718 lecturers are fine (was take(100)).
        return list.sortedBy { it.name }.toList()
    }
}
