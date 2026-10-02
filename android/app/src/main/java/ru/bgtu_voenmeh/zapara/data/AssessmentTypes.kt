package ru.bgtu_voenmeh.zapara.data

import java.util.Locale

/** Classification of source timetable tokens, not display labels. */
internal fun isAssessment(row: Lesson): Boolean {
    val type = row.typeRaw.trim().lowercase(Locale.ROOT).replace('ё', 'е').trimEnd('.')
    val compact = type.replace(Regex("[.\\s]+"), "")
    if (compact in setOf("экз", "экзамен", "зач", "зачет", "дифзач", "дифзачет")) return true
    return type.isEmpty() && Regex("^(экз\\.?|экзамен|зач\\.?|зач[её]т|диф\\.?\\s*зач[её]?т?)\\s", RegexOption.IGNORE_CASE)
        .containsMatchIn(row.subjectRaw.trim())
}
