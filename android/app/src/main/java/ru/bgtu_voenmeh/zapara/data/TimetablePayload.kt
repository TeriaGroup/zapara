package ru.bgtu_voenmeh.zapara.data

object TimetablePayload {
    const val NOT_XML =
        "сайт университета отдал страницу вместо файла расписания. Показано последнее сохранённое"

    fun requireXml(text: String, contentType: String? = null): String {
        val type = contentType.orEmpty().lowercase()
        if (type.contains("html") && !type.contains("xml")) {
            throw IllegalStateException(NOT_XML)
        }
        val trimmed = text.trimStart('\uFEFF', ' ', '\n', '\r', '\t')
        val head = trimmed.take(64).lowercase()
        if (head.startsWith("<!doctype html") || head.startsWith("<html") || head.startsWith("<!doctype")) {
            throw IllegalStateException(NOT_XML)
        }
        if (trimmed.startsWith("<?xml") || trimmed.startsWith("<Timetable")) return text
        if (trimmed.startsWith("<!")) throw IllegalStateException(NOT_XML)
        if (!trimmed.startsWith("<")) throw IllegalStateException(NOT_XML)
        return text
    }
}
