package ru.bgtu_voenmeh.zapara.data.api

import ru.bgtu_voenmeh.zapara.data.Parity
import java.time.Duration
import java.time.LocalDate
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.time.format.DateTimeParseException
import java.time.format.ResolverStyle

internal object TimetableApiJson {
    private val dateFmt = DateTimeFormatter.ofPattern("uuuu-MM-dd").withResolverStyle(ResolverStyle.STRICT)
    private val timeFmt = DateTimeFormatter.ofPattern("HH:mm")
    private val uuidRe = Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")

    fun require(condition: Boolean) {
        if (!condition) throw TimetableApiException(TimetableApiFailure.InvalidPayload)
    }

    fun validId(id: String?): Boolean =
        id != null && id.length in 1..64 && id.isNotBlank() && id == id.trim() && id.none { it.isISOControl() }

    fun catalog(root: JsonValue): TimetableApiSnapshot {
        val obj = try {
            root.obj()
        } catch (_: JsonFail) {
            throw TimetableApiException(TimetableApiFailure.InvalidPayload)
        }
        return try {
            val period = period(obj.field("period"))
            val meta = meta(obj.field("meta"))
            val refresh = refresh(obj.field("refresh"))
            val groups = ArrayList<TimetableApiGroup>()
            val ids = HashSet<String>()
            var count = 0
            for (item in obj.array("groups", 5000).items) {
                val group = group(item)
                require(ids.add(group.id))
                count += group.lessonCount
                require(count <= 50_000)
                groups.add(group)
            }
            TimetableApiSnapshot(period, meta, refresh, groups, emptyMap())
        } catch (_: JsonFail) {
            throw TimetableApiException(TimetableApiFailure.InvalidPayload)
        } catch (_: DateTimeParseException) {
            throw TimetableApiException(TimetableApiFailure.InvalidPayload)
        }
    }

    fun timetable(root: JsonValue, catalog: TimetableApiSnapshot, expected: TimetableApiGroup): TimetableApiDownloadedGroup {
        val obj = try {
            root.obj()
        } catch (_: JsonFail) {
            throw TimetableApiException(TimetableApiFailure.InvalidPayload)
        }
        return try {
            require(period(obj.field("period")) == catalog.period)
            val meta = meta(obj.field("meta"))
            require(meta.copy(stale = catalog.meta.stale) == catalog.meta)
            val refresh = refresh(obj.field("refresh"))
            val group = group(obj.field("group"))
            require(group == expected)
            val array = obj.array("lessons", 50_000)
            require(array.items.size == group.lessonCount)
            val lessons = ArrayList<TimetableApiLesson>(array.items.size)
            val keys = HashSet<Triple<Int, Int, Int>>()
            for (item in array.items) {
                val lesson = lesson(item)
                require(keys.add(Triple(lesson.dayOfWeek, lesson.parity, lesson.index)))
                lessons.add(lesson)
            }
            TimetableApiDownloadedGroup(group, meta, refresh, lessons)
        } catch (_: JsonFail) {
            throw TimetableApiException(TimetableApiFailure.InvalidPayload)
        } catch (_: DateTimeParseException) {
            throw TimetableApiException(TimetableApiFailure.InvalidPayload)
        }
    }

    private fun period(value: JsonValue): TimetableApiPeriod {
        val obj = value.obj()
        val start = try {
            LocalDate.parse(obj.text("start", 10), dateFmt)
        } catch (_: DateTimeParseException) {
            throw JsonFail()
        }
        val weeks = obj.int("weekCount")
        val zone = obj.text("timeZone", 64)
        require(weeks == 2 && zone == "Europe/Moscow")
        return TimetableApiPeriod(start, weeks, obj.text("title", 256, true), zone)
    }

    private fun meta(value: JsonValue): TimetableApiMeta {
        val obj = value.obj()
        val hash = obj.text("sourceSha256", 64)
        require(hash.length == 64 && hash.all { it in '0'..'9' || it in 'a'..'f' })
        return TimetableApiMeta(
            snapshotId = uuid(obj, "snapshotId"),
            fetchedAt = timestamp(obj, "fetchedAt"),
            publishedAt = timestamp(obj, "publishedAt"),
            sourceModifiedAt = nullableTimestamp(obj, "sourceModifiedAt"),
            sourceKind = obj.text("sourceKind", 64, true),
            sourceUrl = obj.nullableText("sourceUrl"),
            sourceSha256 = hash,
            stale = obj.bool("stale")
        )
    }

    private fun refresh(value: JsonValue): TimetableApiRefresh {
        val obj = value.obj()
        val id = if (obj.field("lastAttemptId") is JsonValue.Null) null else uuid(obj, "lastAttemptId")
        return TimetableApiRefresh(
            id,
            obj.nullableText("lastAttemptStatus"),
            nullableTimestamp(obj, "lastSuccessAt"),
            nullableTimestamp(obj, "lastFailureAt"),
            obj.nullableText("lastFailureCode"),
            obj.bool("abandoned")
        )
    }

    private fun group(value: JsonValue): TimetableApiGroup {
        val obj = value.obj()
        val id = obj.text("id", 64, true)
        require(validId(id))
        val count = obj.int("lessonCount")
        require(count in 0..50_000)
        return TimetableApiGroup(id, obj.text("name", 128, true), count)
    }

    private fun lesson(value: JsonValue): TimetableApiLesson {
        val obj = value.obj()
        val day = obj.int("dayOfWeek")
        val parity = obj.int("parity")
        val index = obj.int("index")
        require(day in 1..7 && parity in 0..2 && index > 0)
        val start = time(obj, "timeStart")
        val end = time(obj, "timeEnd")
        require(end > start && Duration.between(start, end) == Duration.ofMinutes(95))
        val raw = obj.text("subjectRaw", 2048, true)
        val normalized = obj.text("subjectNormalized", 2048, true)
        require(normalized == Parity.normalizeSubject(raw))
        return TimetableApiLesson(
            day, parity, index, obj.text("timeStart", 5), obj.text("timeEnd", 5), raw, normalized,
            obj.nullableText("typeRaw"), obj.nullableText("teacherRaw"), obj.nullableText("classroomRaw"),
            obj.nullableText("roomRaw"), obj.nullableText("buildingRaw")
        )
    }

    private fun uuid(obj: JsonValue.Obj, name: String): String {
        val text = obj.text(name, 36)
        require(uuidRe.matches(text) && text != "00000000-0000-0000-0000-000000000000")
        return text.lowercase()
    }

    private fun timestamp(obj: JsonValue.Obj, name: String): String {
        val text = obj.text(name, 40)
        require(text.endsWith("Z") || text.endsWith("+00:00"))
        return text
    }

    private fun nullableTimestamp(obj: JsonValue.Obj, name: String): String? =
        if (obj.field(name) is JsonValue.Null) null else timestamp(obj, name)

    private fun time(obj: JsonValue.Obj, name: String): LocalTime {
        val text = obj.text(name, 5)
        require(text.length == 5)
        return try {
            LocalTime.parse(text, timeFmt)
        } catch (_: DateTimeParseException) {
            throw JsonFail()
        }
    }
}
