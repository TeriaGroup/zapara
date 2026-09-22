package ru.bgtu_voenmeh.zapara.data

import java.util.Locale

/**
 * A split is two or more teachers present in the same week at one bell.
 * Odd and even lessons at that bell are not a split: they never meet.
 */
object Subgroups {
    data class Option(val id: String, val label: String)
    data class Stream(val id: String, val title: String, val options: List<Option>, val joined: Boolean)
    data class Mark(val streamId: String, val options: List<Option>, val chosenId: String?, val showChooser: Boolean)

    class Index(
        val streams: List<Stream>,
        private val membership: Map<String, Member>
    ) {
        fun member(lesson: Lesson): Member? = membership[lessonKey(lesson)]
    }

    data class Member(val streamId: String, val optionIds: Set<String>, val joined: Boolean)

    fun index(lessons: List<Lesson>): Index {
        val clusters = clusters(lessons)
        val streams = linkedMapOf<String, StreamBuilder>()
        val membership = linkedMapOf<String, Member>()
        for (cluster in clusters) {
            val builder = streams.getOrPut(cluster.streamId) {
                StreamBuilder(cluster.streamId, cluster.title, cluster.sameSubject)
            }
            builder.joined = builder.joined && cluster.lessons.size == 1
            for ((id, label) in cluster.optionLabels) builder.options.putIfAbsent(id, label)
            for (lesson in cluster.lessons) {
                val key = lessonKey(lesson)
                val ids = if (cluster.sameSubject) teacherNames(lesson.teacherRaw).map { teacherKey(it) }.toSet()
                else setOf(slotOptionId(lesson))
                val previous = membership[key]
                val joined = cluster.lessons.size == 1 && (previous?.joined != false)
                membership[key] = Member(cluster.streamId, ids, joined && cluster.lessons.size == 1)
            }
        }
        return Index(
            streams.values.map { it.build() }.filter { it.options.size >= 2 }.sortedBy { it.id },
            membership.filterValues { member -> (streams[member.streamId]?.options?.size ?: 0) >= 2 }
        )
    }

    fun visible(lessons: List<Lesson>, choices: Map<String, String>): List<Lesson> {
        val built = index(lessons)
        return lessons.filter { keep(it, built, choices) }
    }

    fun keep(lesson: Lesson, index: Index, choices: Map<String, String>): Boolean {
        val member = index.member(lesson) ?: return true
        val stream = index.streams.firstOrNull { it.id == member.streamId } ?: return true
        val choice = choices[stream.id]?.takeIf { id -> stream.options.any { it.id == id } } ?: return true
        if (member.joined) return true
        return choice in member.optionIds
    }

    fun mark(lesson: Lesson, day: List<Lesson>, index: Index, choices: Map<String, String>): Mark? {
        val member = index.member(lesson) ?: return null
        val stream = index.streams.firstOrNull { it.id == member.streamId } ?: return null
        val chosen = choices[stream.id]?.takeIf { id -> stream.options.any { it.id == id } }
        val first = day.filter { index.member(it)?.streamId == stream.id }
            .minWithOrNull(compareBy({ timeKey(it.timeStart) }, { it.index }, { lessonKey(it) }))
        return Mark(stream.id, stream.options, chosen, first != null && lessonKey(first) == lessonKey(lesson))
    }

    internal fun teacherNames(raw: String): List<String> =
        raw.split(';').map { it.trim() }.filter { it.isNotEmpty() && it != "—" && it != "-" }

    internal fun teacherKey(name: String): String =
        name.trim().lowercase(Locale.forLanguageTag("ru")).replace('ё', 'е')
            .replace(Regex("[.\\s]+"), " ").trim()

    internal fun timeKey(raw: String): String {
        val match = Regex("""(\d{1,2}):(\d{2})""").find(raw) ?: return raw.trim()
        return "%02d:%s".format(match.groupValues[1].toInt(), match.groupValues[2])
    }

    private fun subjectKey(lesson: Lesson): String {
        val norm = lesson.subjectNormalized.trim()
        if (norm.isNotEmpty()) return norm
        return lesson.subjectRaw.trim().lowercase(Locale.forLanguageTag("ru")).replace('ё', 'е')
    }

    private fun lessonKey(lesson: Lesson): String = listOf(
        lesson.groupId, lesson.dayOfWeek, lesson.parity, lesson.index,
        timeKey(lesson.timeStart), subjectKey(lesson), teacherKey(lesson.teacherRaw), lesson.classroomRaw.trim()
    ).joinToString("|")

    private fun slotOptionId(lesson: Lesson): String {
        val teachers = teacherNames(lesson.teacherRaw).map { teacherKey(it) }.sorted().joinToString("+")
        return subjectKey(lesson) + "|" + teachers + "|" + lesson.classroomRaw.trim()
    }

    private data class Cluster(
        val streamId: String,
        val title: String,
        val sameSubject: Boolean,
        val lessons: List<Lesson>,
        val optionLabels: Map<String, String>
    )

    private class StreamBuilder(val id: String, val title: String, val sameSubject: Boolean) {
        var joined: Boolean = true
        val options = linkedMapOf<String, String>()
        fun build(): Stream = Stream(id, title, options.map { Option(it.key, it.value) }, joined && sameSubject)
    }

    private fun clusters(lessons: List<Lesson>): List<Cluster> {
        val found = mutableListOf<Cluster>()
        val seen = mutableSetOf<String>()
        for ((slot, rows) in lessons.groupBy { it.dayOfWeek to timeKey(it.timeStart) }) {
            for (week in weekCodes(rows)) {
                val active = rows.filter { it.parity == 0 || it.parity == week }
                val names = linkedMapOf<String, String>()
                for (lesson in active.sortedWith(compareBy({ it.index }, { subjectKey(it) }))) {
                    for (name in teacherNames(lesson.teacherRaw)) {
                        val id = teacherKey(name)
                        if (id.isNotEmpty()) names.putIfAbsent(id, name.trim())
                    }
                }
                if (names.size < 2 || active.isEmpty()) continue
                val subjects = active.map { subjectKey(it) }.filter { it.isNotEmpty() }.distinct()
                val same = subjects.size == 1
                val set = names.keys.sorted().joinToString("+")
                val streamId = if (same) "s:${subjects.first()}:$set" else "t:${slot.first}:${slot.second}:$set"
                val signature = streamId + "#" + active.map { lessonKey(it) }.sorted().joinToString(",")
                if (!seen.add(signature)) continue
                val labels = if (same) names else active.associate { slotOptionId(it) to slotLabel(it) }
                val title = active.minWith(compareBy({ it.dayOfWeek }, { timeKey(it.timeStart) }, { it.index })).subjectRaw
                found += Cluster(streamId, title, same, active, labels)
            }
        }
        return found
    }

    private fun slotLabel(lesson: Lesson): String {
        val teachers = teacherNames(lesson.teacherRaw).joinToString(", ")
        return listOf(lesson.subjectRaw.trim(), teachers).filter { it.isNotEmpty() }.joinToString(" · ")
            .ifBlank { teachers }
    }

    private fun weekCodes(rows: List<Lesson>): Set<Int> {
        val codes = rows.map { it.parity }.filter { it > 0 }.toSet()
        return if (codes.isEmpty()) setOf(1) else codes
    }
}
