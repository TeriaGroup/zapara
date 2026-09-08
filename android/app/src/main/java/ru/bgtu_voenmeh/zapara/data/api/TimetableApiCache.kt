package ru.bgtu_voenmeh.zapara.data.api

import ru.bgtu_voenmeh.zapara.data.GroupInfo
import java.time.LocalDate

class TimetableApiCache(private val store: TimetableStore) {
    fun read(groupId: String?): CacheMetadata? = groupId?.let { store.readMetadata(it) }

    fun canIntersect(selected: String, friend: String): Boolean {
        val mine = read(selected)
        val other = read(friend)
        if (mine?.source != "api" && other?.source != "api") return true
        return mine?.source == "api" && other?.source == "api" && mine.sourceBase == other.sourceBase &&
            mine.period == other.period && mine.meta?.snapshotId == other.meta?.snapshotId &&
            mine.meta?.stale == false && other.meta?.stale == false
    }

    fun apply(snapshot: TimetableApiSnapshot, sourceBase: String = "") {
        validate(snapshot)
        store.runInTransaction {
            val settings = store.settings()
            val firstAdoption = read("") == null
            val oldPeriod = TimetableApiPeriod(
                settings.periodStart,
                settings.weekCount,
                settings.periodTitle.orEmpty(),
                "Europe/Moscow"
            )
            for (group in store.storedGroups()) {
                val fetched = if (firstAdoption) settings.lastFetchedAt else null
                if (read(group.id) == null && (fetched != null || store.allLessons(group.id).isNotEmpty())) {
                    store.writeMetadata(group.id, CacheMetadata(oldPeriod, null, fetched, "legacy", ""))
                }
            }
            store.clearCatalog()
            for (group in snapshot.groups) {
                store.ensureGroup(group.id, group.name)
                store.insertCatalog(group.id, group.name)
            }
            store.writeMetadata(
                "",
                CacheMetadata(snapshot.period, snapshot.meta, snapshot.meta.fetchedAt, "api", sourceBase)
            )
            for ((id, downloaded) in snapshot.downloaded.entries.sortedBy { it.key }) {
                store.ensureGroup(id, downloaded.group.name, "")
                store.replaceLessons(id, downloaded.lessons.map { it.toLesson(id) })
                store.writeMetadata(
                    id,
                    CacheMetadata(snapshot.period, downloaded.meta, downloaded.meta.fetchedAt, "api", sourceBase)
                )
            }
        }
    }

    private fun validate(snapshot: TimetableApiSnapshot) {
        if (snapshot.meta.snapshotId == "00000000-0000-0000-0000-000000000000" ||
            snapshot.period.weekCount != 2 ||
            snapshot.period.timeZone != "Europe/Moscow" ||
            snapshot.groups.any { it.id.isBlank() || it.lessonCount < 0 } ||
            snapshot.groups.map { it.id }.distinct().size != snapshot.groups.size
        ) {
            throw TimetableApiException(TimetableApiFailure.InvalidPayload)
        }
        for ((id, group) in snapshot.downloaded) {
            if (group.group.id != id || group.group !in snapshot.groups ||
                group.meta.snapshotId != snapshot.meta.snapshotId ||
                group.lessons.size != group.group.lessonCount
            ) {
                throw TimetableApiException(TimetableApiFailure.InvalidPayload)
            }
        }
    }
}

fun defaultPeriodStart(): LocalDate = LocalDate.of(2026, 9, 1)

internal fun CacheMetadata.encode(): String = buildString {
    append('{')
    append("\"period\":{\"start\":\"").append(period.start).append("\",\"weekCount\":").append(period.weekCount)
    append(",\"title\":").append(jsonEsc(period.title)).append(",\"timeZone\":").append(jsonEsc(period.timeZone)).append("},")
    append("\"meta\":")
    if (meta == null) append("null") else {
        append("{\"snapshotId\":").append(jsonEsc(meta.snapshotId))
        append(",\"fetchedAt\":").append(jsonEsc(meta.fetchedAt))
        append(",\"publishedAt\":").append(jsonEsc(meta.publishedAt))
        append(",\"sourceModifiedAt\":")
        if (meta.sourceModifiedAt == null) append("null") else append(jsonEsc(meta.sourceModifiedAt))
        append(",\"sourceKind\":").append(jsonEsc(meta.sourceKind))
        append(",\"sourceUrl\":")
        if (meta.sourceUrl == null) append("null") else append(jsonEsc(meta.sourceUrl))
        append(",\"sourceSha256\":").append(jsonEsc(meta.sourceSha256))
        append(",\"stale\":").append(meta.stale).append('}')
    }
    append(",\"fetchedAt\":")
    if (fetchedAt == null) append("null") else append(jsonEsc(fetchedAt))
    append(",\"source\":").append(jsonEsc(source))
    append(",\"sourceBase\":").append(jsonEsc(sourceBase))
    append('}')
}

private fun jsonEsc(value: String): String = buildString {
    append('"')
    for (c in value) when (c) {
        '\\' -> append("\\\\")
        '"' -> append("\\\"")
        else -> append(c)
    }
    append('"')
}

internal fun decodeCacheMetadata(payload: String): CacheMetadata {
    val root = StrictJson.parse(payload).obj()
    val periodObj = root.field("period").obj()
    val period = TimetableApiPeriod(
        LocalDate.parse(periodObj.text("start", 10)),
        periodObj.int("weekCount"),
        periodObj.text("title", 256),
        periodObj.text("timeZone", 64)
    )
    val meta = if (root.field("meta") is JsonValue.Null) null else {
        val m = root.field("meta").obj()
        TimetableApiMeta(
            m.text("snapshotId", 36),
            m.text("fetchedAt", 40),
            m.text("publishedAt", 40),
            m.nullableText("sourceModifiedAt", 40),
            m.text("sourceKind", 64),
            m.nullableText("sourceUrl"),
            m.text("sourceSha256", 64),
            m.bool("stale")
        )
    }
    return CacheMetadata(period, meta, root.nullableText("fetchedAt", 40), root.text("source", 32), root.text("sourceBase", 2048))
}
