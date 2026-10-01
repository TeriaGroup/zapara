package ru.bgtu_voenmeh.zapara.ui.summary

import ru.bgtu_voenmeh.zapara.data.LecturerInfo
import ru.bgtu_voenmeh.zapara.data.LecturerLesson
import ru.bgtu_voenmeh.zapara.data.TeacherMatch

/** Resolve only a unique known teacher. Ambiguous initials keep their raw display bucket. */
internal class SummaryTeacherIdentity(private val catalog: List<LecturerInfo>,
    private val lessonsFor: (String) -> List<LecturerLesson>,
    private val groupId: String, private val groupName: String?) {
    private val cache = HashMap<String, Pair<String, String>>()

    fun resolve(raw: String): Pair<String, String> = cache.getOrPut(raw) {
        val hits = catalog.filter { TeacherMatch.matches(it.name, raw) }
        val chosen = if (hits.size == 1) hits.single() else hits.filter { lecturer ->
            lessonsFor(lecturer.id).any { lesson -> lesson.groups.any { ref ->
                TeacherMatch.teachesGroup(ref, groupId, groupName)
            } }
        }.singleOrNull()
        if (chosen == null) "raw:$raw" to raw else "teacher:${chosen.id}" to chosen.name
    }
}
