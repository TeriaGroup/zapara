package ru.bgtu_voenmeh.zapara.data

import java.time.LocalDate
import java.time.LocalTime
import ru.bgtu_voenmeh.zapara.data.api.JsonFail
import ru.bgtu_voenmeh.zapara.data.api.JsonValue
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.obj

object VoenmehScheduleParser {
    fun parseMeta(json: String): VoenmehScheduleMeta {
        val obj = payloadObject(json)
        val hasData = (obj.fields["has_data"] as? JsonValue.Bool)?.value ?: false
        if (!hasData) throw IllegalStateException("Расписание пока не загружено на сайте университета")
        val title = str(obj, "period")
        val groups = strings(obj.fields["groups"])
        if (groups.isEmpty()) throw IllegalStateException("Сайт университета не отдал список групп")
        return VoenmehScheduleMeta(
            periodStart = periodStart(title),
            weekCount = 2,
            periodTitle = title.ifBlank { "Расписание" },
            groups = groups,
            updatedAt = str(obj, "updated_at").ifBlank { null }
        )
    }

    fun parseLessons(json: String, groupId: String): List<Lesson> {
        val obj = payloadObject(json)
        val rows = (obj.fields["lessons"] as? JsonValue.Arr)?.items.orEmpty()
        val indexPerParity = mutableMapOf<Int, Int>()
        val lessons = ArrayList<Lesson>(rows.size)
        for (item in rows) {
            val row = try { item.obj() } catch (_: JsonFail) { continue }
            val day = int(row, "day")
            if (day !in 1..7) continue
            val parity = when (str(row, "week").lowercase()) {
                "odd", "нечетная", "нечётная" -> 1
                "even", "четная", "чётная" -> 2
                else -> 0
            }
            val kind = str(row, "kind").trim()
            val subject = str(row, "subject").trim()
            val discRaw = when {
                kind.isNotEmpty() && subject.isNotEmpty() -> "$kind $subject"
                subject.isNotEmpty() -> subject
                else -> kind
            }
            val timeStart = padTime(str(row, "time"))
            val timeEnd = try {
                val te = LocalTime.parse(timeStart).plusMinutes(95)
                "%02d:%02d".format(te.hour, te.minute)
            } catch (_: Exception) { "" }
            val teachers = strings(row.fields["teachers"])
            val rooms = strings(row.fields["rooms"])
            val classroomRaw = if (rooms.isEmpty()) "" else rooms.joinToString("; ").let { if (it.endsWith(";")) it else "$it;" }
            val place = GroupParser.placeOf(classroomRaw)
            val idx = (indexPerParity[parity] ?: 0) + 1
            indexPerParity[parity] = idx
            lessons.add(
                Lesson(
                    groupId = groupId,
                    dayOfWeek = day,
                    parity = parity,
                    index = idx,
                    timeStart = timeStart,
                    timeEnd = timeEnd,
                    subjectRaw = discRaw,
                    subjectNormalized = Parity.normalizeSubject(discRaw),
                    teacherRaw = teachers.joinToString("; "),
                    roomRaw = place.first,
                    buildingRaw = place.second,
                    typeRaw = kind,
                    classroomRaw = classroomRaw
                )
            )
        }
        return lessons
    }

    fun assemble(meta: VoenmehScheduleMeta, loaded: List<Pair<String, List<Lesson>>>): ParsedSchedule {
        val byName = loaded.associate { it.first to it.second }
        val groups = meta.groups.map { name ->
            GroupInfo(id = name, name = name, url = VoenmehScheduleClient.ORIGIN)
        }
        val lessons = ArrayList<Lesson>()
        for (g in groups) lessons += byName[g.id].orEmpty()
        return ParsedSchedule(groups, lessons, meta.periodStart, meta.weekCount, meta.periodTitle)
    }

    internal fun periodStart(title: String): LocalDate {
        val years = Regex("""(20\d{2})""").findAll(title).map { it.groupValues[1].toInt() }.toList()
        val year = years.firstOrNull() ?: 2026
        val spring = title.contains("весен", ignoreCase = true)
        return if (spring) LocalDate.of(year, 2, 9) else LocalDate.of(year, 9, 1)
    }

    private fun padTime(raw: String): String {
        val m = Regex("""(\d{1,2}):(\d{2})""").find(raw.trim()) ?: return ""
        return "%02d:%02d".format(m.groupValues[1].toInt(), m.groupValues[2].toInt())
    }

    private fun strings(value: JsonValue?): List<String> {
        val arr = value as? JsonValue.Arr ?: return emptyList()
        return arr.items.mapNotNull { (it as? JsonValue.Str)?.value?.trim()?.takeIf { s -> s.isNotEmpty() } }
    }

    private fun str(obj: JsonValue.Obj, name: String): String =
        (obj.fields[name] as? JsonValue.Str)?.value.orEmpty()

    private fun int(obj: JsonValue.Obj, name: String): Int {
        val n = obj.fields[name] as? JsonValue.Num ?: return 0
        return n.raw.toIntOrNull() ?: 0
    }

    private fun payloadObject(json: String): JsonValue.Obj {
        val trimmed = json.trimStart('\uFEFF', ' ', '\n', '\r', '\t')
        if (trimmed.startsWith("<")) throw IllegalStateException(TimetablePayload.NOT_XML)
        return try {
            StrictJson.parse(trimmed).obj()
        } catch (_: JsonFail) {
            throw IllegalStateException(TimetablePayload.NOT_XML)
        }
    }
}

data class VoenmehScheduleMeta(
    val periodStart: LocalDate,
    val weekCount: Int,
    val periodTitle: String,
    val groups: List<String>,
    val updatedAt: String?
)
