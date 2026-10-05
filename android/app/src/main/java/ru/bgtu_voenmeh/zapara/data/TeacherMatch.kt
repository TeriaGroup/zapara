package ru.bgtu_voenmeh.zapara.data

object TeacherMatch {
    fun myIds(
        groupLessons: List<Lesson>,
        lecturers: List<LecturerInfo>,
        lessonsFor: (String) -> List<LecturerLesson> = { emptyList() },
        myGroupId: String? = null,
        myGroupName: String? = null
    ): Set<String> {
        val shorts = groupLessons
            .flatMap { it.teacherRaw.split(";") }
            .map { it.trim() }
            .filter { it.isNotEmpty() && it != "—" }
        val ids = linkedSetOf<String>()
        for (short in shorts) {
            val hits = lecturers.filter { matches(it.name, short) }
            val mine = disambiguate(hits, lessonsFor, myGroupId, myGroupName)
            for (lect in mine) ids += lect.id
        }
        return ids
    }

    fun inMineList(lect: LecturerInfo, myIds: Set<String>): Boolean = lect.id in myIds

    private fun disambiguate(
        hits: List<LecturerInfo>,
        lessonsFor: (String) -> List<LecturerLesson>,
        myGroupId: String?,
        myGroupName: String?
    ): List<LecturerInfo> {
        if (hits.size <= 1) return hits
        val withGroup = hits.filter { lect ->
            lessonsFor(lect.id).any { lesson -> lesson.groups.any { teachesGroup(it, myGroupId, myGroupName) } }
        }
        return withGroup.ifEmpty { hits }
    }

    internal fun teachesGroup(ref: GroupRef, myGroupId: String?, myGroupName: String?): Boolean {
        if (!myGroupId.isNullOrBlank() && (ref.idGroup == myGroupId || ref.number == myGroupId)) return true
        if (!myGroupName.isNullOrBlank() && (ref.idGroup == myGroupName || ref.number == myGroupName)) return true
        return false
    }

    internal fun matches(lecturerName: String, short: String): Boolean {
        val want = person(short) ?: return false
        val have = person(lecturerName) ?: return false
        if (!want.last.equals(have.last, ignoreCase = true)) return false
        if (want.initials.isEmpty()) return have.initials.isEmpty()
        return want.initials == have.initials
    }

    private fun person(raw: String): Person? {
        val parts = raw.trim().split(Regex("\\s+")).filter { it.isNotEmpty() }
        if (parts.isEmpty()) return null
        val last = parts[0].trimEnd('.')
        if (last.isEmpty()) return null
        return Person(last, initialsFrom(parts.drop(1)))
    }

    private fun initialsFrom(parts: List<String>): List<String> {
        val out = ArrayList<String>()
        for (token in parts) {
            val dotted = token.contains('.')
            val letters = token.filter { it.isLetter() }
            if (letters.isEmpty()) continue
            if (dotted && letters.length <= 3) {
                letters.forEach { out += it.uppercaseChar().toString() }
            } else {
                out += letters.first().uppercaseChar().toString()
            }
        }
        return out
    }

    private data class Person(val last: String, val initials: List<String>)
}
