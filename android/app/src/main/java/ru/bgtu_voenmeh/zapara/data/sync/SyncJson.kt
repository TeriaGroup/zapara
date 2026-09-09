package ru.bgtu_voenmeh.zapara.data.sync

import ru.bgtu_voenmeh.zapara.data.api.JsonFail
import ru.bgtu_voenmeh.zapara.data.api.JsonValue
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.field
import ru.bgtu_voenmeh.zapara.data.api.obj
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.format.DateTimeParseException
import java.time.format.ResolverStyle
import java.util.Locale
import java.util.UUID

internal object SyncJson {
    private val DATE = DateTimeFormatter.ofPattern("uuuu-MM-dd", Locale.ROOT).withResolverStyle(ResolverStyle.STRICT)
    private val TIME = DateTimeFormatter.ofPattern("HH:mm:ss", Locale.ROOT)
    private val UTC_HEAD = Regex("^(\\d{4}-\\d{2}-\\d{2})T(\\d{2}:\\d{2}:\\d{2})(\\.(\\d{1,7}))?Z$")

    fun valueUtf8(value: SyncValue): ByteArray = serializeValue(value).toByteArray(Charsets.UTF_8)

    fun serialize(mutation: SyncMutation): ByteArray = obj {
        str("syncEpoch", mutation.syncEpoch.toString())
        str("opId", mutation.opId.toString())
        str("entityType", mutation.entityType)
        str("entityId", mutation.entityId.toString())
        num("expectedRevision", mutation.expectedRevision)
        str("action", mutation.action)
        raw("value", mutation.value?.let { serializeValue(it) })
    }.toByteArray(Charsets.UTF_8)

    fun serialize(record: SyncRecord): ByteArray = writeRecord(record).toByteArray(Charsets.UTF_8)

    fun serialize(page: SyncChangesPage): ByteArray = obj {
        raw("metadata", writeMetadata(page.metadata))
        num("afterSequence", page.afterSequence)
        num("nextAfterSequence", page.nextAfterSequence)
        bool("hasMore", page.hasMore)
        raw("changes", arr(page.changes) { change ->
            obj {
                num("sequence", change.sequence)
                str("opId", change.opId.toString())
                raw("record", writeRecord(change.record))
            }
        })
    }.toByteArray(Charsets.UTF_8)

    fun serialize(page: SyncResyncPage): ByteArray = obj {
        raw("manifest", writeManifest(page.manifest))
        num("afterOrdinal", page.afterOrdinal)
        num("nextAfterOrdinal", page.nextAfterOrdinal)
        bool("hasMore", page.hasMore)
        raw("items", arr(page.items) { item ->
            obj {
                num("ordinal", item.ordinal)
                raw("record", writeRecord(item.record))
            }
        })
    }.toByteArray(Charsets.UTF_8)

    fun metadata(bytes: ByteArray): SyncMetadata = readMetadata(root(bytes, SyncValidation.REQUEST_BYTES))

    fun mutationResult(bytes: ByteArray): SyncMutationResult {
        val root = root(bytes, SyncValidation.REQUEST_BYTES)
        root.requireKeys("status", "code", "metadata", "serverRecord")
        val record = when (val value = root.field("serverRecord")) {
            is JsonValue.Null -> null
            else -> readRecord(value.obj())
        }
        return SyncMutationResult(root.int32("status"), root.str("code"), readMetadata(root.field("metadata").obj()), record)
    }

    fun changesPage(bytes: ByteArray): SyncChangesPage {
        val root = root(bytes, SyncValidation.PAGE_BYTES)
        root.requireKeys("metadata", "afterSequence", "nextAfterSequence", "hasMore", "changes")
        val changes = root.arr("changes").map { readChange(it.obj()) }
        return SyncChangesPage(
            readMetadata(root.field("metadata").obj()),
            root.int64("afterSequence"),
            root.int64("nextAfterSequence"),
            root.boolean("hasMore"),
            changes
        )
    }

    fun resyncManifest(bytes: ByteArray): SyncResyncManifest =
        readManifest(root(bytes, SyncValidation.REQUEST_BYTES))

    fun resyncPage(bytes: ByteArray): SyncResyncPage {
        val root = root(bytes, SyncValidation.PAGE_BYTES)
        root.requireKeys("manifest", "afterOrdinal", "nextAfterOrdinal", "hasMore", "items")
        val items = root.arr("items").map { readItem(it.obj()) }
        return SyncResyncPage(
            readManifest(root.field("manifest").obj()),
            root.int64("afterOrdinal"),
            root.int64("nextAfterOrdinal"),
            root.boolean("hasMore"),
            items
        )
    }

    fun error(bytes: ByteArray): SyncError {
        val root = try {
            StrictJson.parse(bytes, 16).obj()
        } catch (_: JsonFail) {
            SyncValidation.invalid()
        }
        if ("status" !in root.fields || "code" !in root.fields) SyncValidation.invalid()
        if (root.fields.keys.any { it !in setOf("status", "code", "title") }) SyncValidation.invalid()
        return SyncError(root.int32("status"), root.str("code"))
    }

    fun parseValue(type: String, bytes: ByteArray): SyncValue {
        val root = try {
            StrictJson.parse(bytes, 16).obj()
        } catch (_: JsonFail) {
            SyncValidation.invalid()
        }
        return readValue(type, root)
    }

    fun utcText(instant: Instant): String {
        val odt = instant.atOffset(ZoneOffset.UTC)
        val base = DATE.format(odt.toLocalDate()) + "T" + TIME.format(odt.toLocalTime())
        val nano = odt.nano
        if (nano % 100 != 0) SyncValidation.invalid()
        if (nano == 0) return base + "Z"
        val seven = String.format(Locale.ROOT, "%07d", nano / 100).trimEnd('0')
        return "$base.${seven}Z"
    }

    fun parseUtc(text: String): Instant {
        val match = UTC_HEAD.matchEntire(text) ?: SyncValidation.invalid()
        val date = try {
            LocalDate.parse(match.groupValues[1], DATE)
        } catch (_: DateTimeParseException) {
            SyncValidation.invalid()
        }
        val time = try {
            LocalTime.parse(match.groupValues[2], TIME)
        } catch (_: DateTimeParseException) {
            SyncValidation.invalid()
        }
        val frac = match.groupValues[4]
        val nanos = if (frac.isEmpty()) 0 else frac.padEnd(7, '0').toLong() * 100
        val instant = date.atTime(time).toInstant(ZoneOffset.UTC).plusNanos(nanos)
        if (utcText(instant) != text) SyncValidation.invalid()
        return instant
    }

    private fun writeMetadata(metadata: SyncMetadata): String = obj {
        str("syncEpoch", metadata.syncEpoch.toString())
        num("currentSequence", metadata.currentSequence)
        num("minAfterSequence", metadata.minAfterSequence)
    }

    private fun writeManifest(manifest: SyncResyncManifest): String = obj {
        str("manifestId", manifest.manifestId.toString())
        str("syncEpoch", manifest.syncEpoch.toString())
        num("highWater", manifest.highWater)
        str("createdAt", utcText(manifest.createdAt))
        str("expiresAt", utcText(manifest.expiresAt))
        num("itemCount", manifest.itemCount)
    }

    private fun writeRecord(record: SyncRecord): String = obj {
        str("entityType", record.entityType)
        str("entityId", record.entityId.toString())
        num("revision", record.revision)
        bool("tombstone", record.tombstone)
        str("changedAt", utcText(record.changedAt))
        raw("value", record.value?.let { serializeValue(it) })
    }

    private fun serializeValue(value: SyncValue): String = when (value) {
        is HomeworkValue -> obj {
            str("subjectRaw", value.subjectRaw)
            str("subjectKey", value.subjectKey)
            str("text", value.text)
            num("targetNthOccurrence", value.targetNthOccurrence.toLong())
            str("createdAtUtc", utcText(value.createdAtUtc))
            raw("legacyCreatedLocalDate", value.legacyCreatedLocalDate?.let { "\"${DATE.format(it)}\"" })
        }
        is CompletionValue -> obj {
            bool("done", value.done)
            raw("doneAtUtc", value.doneAtUtc?.let { "\"${utcText(it)}\"" })
        }
        is OverrideValue -> obj {
            str("subjectRaw", value.subjectRaw)
            str("subjectKey", value.subjectKey)
            str("scope", value.scope)
            str("displayName", value.displayName)
            raw("note", value.note?.let { jsonString(it) })
            str("createdAtUtc", utcText(value.createdAtUtc))
        }
        is FriendValue -> obj {
            raw("groupId", value.groupId?.let { jsonString(it) })
            str("groupName", value.groupName)
            str("memberNames", value.memberNames)
            num("paletteIndex", value.paletteIndex.toLong())
            bool("enabled", value.enabled)
        }
        is SettingsValue -> obj {
            raw("selectedGroupId", value.selectedGroupId?.let { jsonString(it) })
            bool("parityInvert", value.parityInvert)
            raw("notifyTime1", value.notifyTime1?.let { jsonString(it) })
            raw("notifyTime2", value.notifyTime2?.let { jsonString(it) })
            num("strictness", value.strictness.toLong())
            bool("alwaysShow", value.alwaysShow)
        }
    }

    private fun readMetadata(root: JsonValue.Obj): SyncMetadata {
        root.requireKeys("syncEpoch", "currentSequence", "minAfterSequence")
        return SyncMetadata(root.uuid("syncEpoch"), root.int64("currentSequence"), root.int64("minAfterSequence"))
    }

    private fun readManifest(root: JsonValue.Obj): SyncResyncManifest {
        root.requireKeys("manifestId", "syncEpoch", "highWater", "createdAt", "expiresAt", "itemCount")
        return SyncResyncManifest(
            root.uuid("manifestId"),
            root.uuid("syncEpoch"),
            root.int64("highWater"),
            parseUtc(root.str("createdAt")),
            parseUtc(root.str("expiresAt")),
            root.int64("itemCount")
        )
    }

    private fun readRecord(root: JsonValue.Obj): SyncRecord {
        root.requireKeys("entityType", "entityId", "revision", "tombstone", "changedAt", "value")
        val type = root.str("entityType")
        return SyncRecord(
            type,
            root.uuid("entityId"),
            root.int64("revision"),
            root.boolean("tombstone"),
            parseUtc(root.str("changedAt")),
            readOptionalValue(type, root.field("value"))
        )
    }

    private fun readChange(root: JsonValue.Obj): SyncChange {
        root.requireKeys("sequence", "opId", "record")
        return SyncChange(root.int64("sequence"), root.uuid("opId"), readRecord(root.field("record").obj()))
    }

    private fun readItem(root: JsonValue.Obj): SyncManifestItem {
        root.requireKeys("ordinal", "record")
        return SyncManifestItem(root.int64("ordinal"), readRecord(root.field("record").obj()))
    }

    private fun readOptionalValue(type: String, value: JsonValue): SyncValue? =
        if (value is JsonValue.Null) null else readValue(type, value.obj())

    private fun readValue(type: String, root: JsonValue.Obj): SyncValue = when (type) {
        "homework" -> {
            root.requireKeys("subjectRaw", "subjectKey", "text", "targetNthOccurrence", "createdAtUtc", "legacyCreatedLocalDate")
            HomeworkValue(
                root.str("subjectRaw"),
                root.str("subjectKey"),
                root.str("text"),
                root.int32("targetNthOccurrence"),
                parseUtc(root.str("createdAtUtc")),
                root.dateOrNull("legacyCreatedLocalDate")
            )
        }
        "completion" -> {
            root.requireKeys("done", "doneAtUtc")
            CompletionValue(root.boolean("done"), root.instantOrNull("doneAtUtc"))
        }
        "override" -> {
            root.requireKeys("subjectRaw", "subjectKey", "scope", "displayName", "note", "createdAtUtc")
            OverrideValue(
                root.str("subjectRaw"),
                root.str("subjectKey"),
                root.str("scope"),
                root.str("displayName"),
                root.strOrNull("note"),
                parseUtc(root.str("createdAtUtc"))
            )
        }
        "friend" -> {
            root.requireKeys("groupId", "groupName", "memberNames", "paletteIndex", "enabled")
            FriendValue(
                root.strOrNull("groupId"),
                root.str("groupName"),
                root.str("memberNames"),
                root.int32("paletteIndex"),
                root.boolean("enabled")
            )
        }
        "settings" -> {
            root.requireKeys("selectedGroupId", "parityInvert", "notifyTime1", "notifyTime2", "strictness", "alwaysShow")
            SettingsValue(
                root.strOrNull("selectedGroupId"),
                root.boolean("parityInvert"),
                root.strOrNull("notifyTime1"),
                root.strOrNull("notifyTime2"),
                root.int32("strictness"),
                root.boolean("alwaysShow")
            )
        }
        else -> SyncValidation.invalid()
    }

    private fun root(bytes: ByteArray, maxBytes: Int): JsonValue.Obj {
        if (bytes.size > maxBytes) SyncValidation.invalid()
        return try {
            StrictJson.parse(bytes, 16).obj()
        } catch (_: JsonFail) {
            SyncValidation.invalid()
        }
    }
}

private fun JsonValue.Obj.requireKeys(vararg keys: String) {
    if (fields.size != keys.size) SyncValidation.invalid()
    for (key in keys) if (key !in fields) SyncValidation.invalid()
}

private fun JsonValue.Obj.str(name: String): String {
    val v = field(name) as? JsonValue.Str ?: SyncValidation.invalid()
    return v.value
}

private fun JsonValue.Obj.strOrNull(name: String): String? {
    val v = field(name)
    if (v is JsonValue.Null) return null
    return (v as? JsonValue.Str)?.value ?: SyncValidation.invalid()
}

private fun JsonValue.Obj.boolean(name: String): Boolean {
    val v = field(name) as? JsonValue.Bool ?: SyncValidation.invalid()
    return v.value
}

private fun JsonValue.Obj.int32(name: String): Int {
    val n = field(name) as? JsonValue.Num ?: SyncValidation.invalid()
    val v = n.raw.toIntOrNull() ?: SyncValidation.invalid()
    if (n.raw != v.toString()) SyncValidation.invalid()
    return v
}

private fun JsonValue.Obj.int64(name: String): Long {
    val n = field(name) as? JsonValue.Num ?: SyncValidation.invalid()
    val v = n.raw.toLongOrNull() ?: SyncValidation.invalid()
    if (n.raw != v.toString()) SyncValidation.invalid()
    return v
}

private fun JsonValue.Obj.uuid(name: String): UUID {
    return try {
        val id = UUID.fromString(str(name))
        SyncValidation.id(id)
    } catch (_: Exception) {
        SyncValidation.invalid()
    }
}

private fun JsonValue.Obj.arr(name: String): List<JsonValue> {
    val v = field(name) as? JsonValue.Arr ?: SyncValidation.invalid()
    if (v.items.size > SyncValidation.PAGE_RECORDS) SyncValidation.invalid()
    return v.items
}

private fun JsonValue.Obj.dateOrNull(name: String): LocalDate? {
    val v = field(name)
    if (v is JsonValue.Null) return null
    val text = (v as? JsonValue.Str)?.value ?: SyncValidation.invalid()
    if (!Regex("^\\d{4}-\\d{2}-\\d{2}$").matches(text)) SyncValidation.invalid()
    return try {
        LocalDate.parse(text, DateTimeFormatter.ofPattern("uuuu-MM-dd").withResolverStyle(ResolverStyle.STRICT))
    } catch (_: DateTimeParseException) {
        SyncValidation.invalid()
    }
}

private fun JsonValue.Obj.instantOrNull(name: String): Instant? {
    val v = field(name)
    if (v is JsonValue.Null) return null
    val text = (v as? JsonValue.Str)?.value ?: SyncValidation.invalid()
    return SyncJson.parseUtc(text)
}

private class ObjWriter {
    private val sb = StringBuilder()
    private var first = true
    fun str(name: String, value: String) { key(name); sb.append(jsonString(value)) }
    fun num(name: String, value: Long) { key(name); sb.append(value) }
    fun bool(name: String, value: Boolean) { key(name); sb.append(if (value) "true" else "false") }
    fun raw(name: String, json: String?) {
        key(name)
        sb.append(json ?: "null")
    }
    private fun key(name: String) {
        if (!first) sb.append(',')
        first = false
        sb.append(jsonString(name)).append(':')
    }
    override fun toString(): String = "{$sb}"
}

private fun obj(block: ObjWriter.() -> Unit): String = ObjWriter().apply(block).toString()

private fun <T> arr(items: List<T>, write: (T) -> String): String = buildString {
    append('[')
    items.forEachIndexed { i, item ->
        if (i > 0) append(',')
        append(write(item))
    }
    append(']')
}

private fun jsonString(value: String): String = buildString {
    append('"')
    for (c in value) when (c) {
        '\\' -> append("\\\\")
        '"' -> append("\\\"")
        '\b' -> append("\\b")
        '\u000C' -> append("\\f")
        '\n' -> append("\\n")
        '\r' -> append("\\r")
        '\t' -> append("\\t")
        else -> if (c.code < 0x20) append("\\u").append(c.code.toString(16).padStart(4, '0'))
        else append(c)
    }
    append('"')
}
