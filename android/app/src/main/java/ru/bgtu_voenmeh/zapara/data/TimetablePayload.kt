package ru.bgtu_voenmeh.zapara.data

import java.time.LocalDate

object TimetablePayload {
    const val NOT_XML =
        "сайт университета отдал страницу вместо файла расписания. Показано последнее сохранённое"

    const val OLDER =
        "сайт университета отдал более старое расписание. Показано последнее сохранённое"

    fun decodeXml(bytes: ByteArray): String {
        if (bytes.size >= 2 && bytes[0] == 0xFF.toByte() && bytes[1] == 0xFE.toByte()) {
            return String(bytes, 2, bytes.size - 2, Charsets.UTF_16LE)
        }
        if (bytes.size >= 3 && bytes[0] == 0xEF.toByte() && bytes[1] == 0xBB.toByte() && bytes[2] == 0xBF.toByte()) {
            return String(bytes, 3, bytes.size - 3, Charsets.UTF_8)
        }
        val utf8 = String(bytes, Charsets.UTF_8)
        return if (utf8.contains('\u0000')) String(bytes, Charsets.UTF_16LE) else utf8
    }

    fun isOlderPeriod(incoming: LocalDate, stored: LocalDate, lastFetchedAt: String?): Boolean {
        if (lastFetchedAt.isNullOrBlank()) return false
        return incoming.isBefore(stored)
    }

    fun requireXml(text: String, contentType: String? = null): String {
        val type = contentType.orEmpty().lowercase()
        if (type.contains("html") && !type.contains("xml")) {
            throw IllegalStateException(NOT_XML)
        }
        val trimmed = text.trimStart('\uFEFF', ' ', '\n', '\r', '\t')
        val head = trimmed.take(64).lowercase()
        if (head.startsWith("<!doctype html") || head.startsWith("<html") || head.startsWith("<!doctype") ||
            head.startsWith("<!--")
        ) {
            throw IllegalStateException(NOT_XML)
        }
        if (trimmed.startsWith("<?xml") || trimmed.startsWith("<Timetable")) return text
        if (trimmed.startsWith("<!")) throw IllegalStateException(NOT_XML)
        if (!trimmed.startsWith("<")) throw IllegalStateException(NOT_XML)
        return text
    }
}
