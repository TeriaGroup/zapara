package ru.bgtu_voenmeh.zapara.data.sync

import java.util.Locale
import java.util.UUID

object SyncValidation {
    val SETTINGS_ID: UUID = UUID.fromString("00000000-0000-0000-0000-000000000001")
    const val VALUE_BYTES = 32 * 1024
    const val REQUEST_BYTES = 64 * 1024
    const val RECORD_BYTES = 36 * 1024
    const val PAGE_RECORDS = 200
    const val PAGE_BYTES = 8 * 1024 * 1024

    fun invalid(): Nothing = throw IllegalArgumentException("Недопустимый контракт Sync.")

    fun text(value: String?, maximum: Int): String {
        if (value == null || value.length > maximum * 2) invalid()
        var count = 0
        var i = 0
        while (i < value.length) {
            val c = value[i]
            if (c == '\u0000' || c.isLowSurrogate()) invalid()
            if (c.isHighSurrogate()) {
                i++
                if (i >= value.length || !value[i].isLowSurrogate()) invalid()
            }
            count++
            if (count > maximum) invalid()
            i++
        }
        return value
    }

    fun optional(value: String?, maximum: Int): String? = if (value == null) null else text(value, maximum)

    fun id(id: UUID): UUID = if (id == UUID(0, 0)) invalid() else id

    fun nonnegative(value: Long): Long = if (value >= 0) value else invalid()

    fun range(value: Int, min: Int, max: Int): Int = if (value in min..max) value else invalid()

    fun normalizeSubject(raw: String): String {
        text(raw, 256)
        return raw.trim().lowercase(Locale.ROOT).replace('ё', 'е')
            .split(' ', '\t', '\r', '\n')
            .filter { it.isNotEmpty() }
            .joinToString(" ")
    }

    fun subjectKey(raw: String, key: String): String =
        if (text(key, 256) == normalizeSubject(raw)) key else invalid()

    fun time(value: String?): String? {
        if (value == null) return null
        if (value.length != 5 || value[2] != ':') invalid()
        val hour = value.substring(0, 2).toIntOrNull() ?: invalid()
        val minute = value.substring(3, 5).toIntOrNull() ?: invalid()
        if (hour !in 0..23 || minute !in 0..59) invalid()
        if (value[0] !in '0'..'9' || value[1] !in '0'..'9' || value[3] !in '0'..'9' || value[4] !in '0'..'9') invalid()
        return value
    }

    fun scope(value: String): String {
        if (value == "global") return value
        if (value.length == 9 && value.startsWith("weekday:") && value[8] in '1'..'7') return value
        invalid()
    }

    fun identity(type: String, id: UUID) {
        id(id)
        if (type !in setOf("homework", "completion", "override", "friend", "settings")) invalid()
        if (type == "settings" && id != SETTINGS_ID) invalid()
    }

    fun value(type: String, value: SyncValue?, deleted: Boolean) {
        if (deleted) {
            if (value != null) invalid()
            return
        }
        val matches = when {
            type == "homework" && value is HomeworkValue -> true
            type == "completion" && value is CompletionValue -> true
            type == "override" && value is OverrideValue -> true
            type == "friend" && value is FriendValue -> true
            type == "settings" && value is SettingsValue -> true
            else -> false
        }
        if (!matches || SyncJson.valueUtf8(value!!).size > VALUE_BYTES) invalid()
    }
}
