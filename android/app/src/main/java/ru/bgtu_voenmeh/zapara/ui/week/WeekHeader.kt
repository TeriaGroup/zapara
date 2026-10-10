package ru.bgtu_voenmeh.zapara.ui.week

import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

/** #102 / AN-05: одна строка шапки «‹ 5–11 окт. · чётная › Сегодня ⋯» и неразрывные «С++», «ВЦ-3». */
object WeekHeader {
    private const val NBSP = '\u00A0'
    private val month = DateTimeFormatter.ofPattern("MMM", Locale("ru"))

    /** «5–11 окт.»; через границу месяца — «28 сент. – 4 окт.». Число и месяц не разрываются. */
    fun range(first: LocalDate?, last: LocalDate?): String {
        if (first == null || last == null) return ""
        val m1 = first.format(month); val m2 = last.format(month)
        return if (first.month == last.month && first.year == last.year) "${first.dayOfMonth}–${last.dayOfMonth}$NBSP$m2"
        else "${first.dayOfMonth}$NBSP$m1 – ${last.dayOfMonth}$NBSP$m2"
    }

    /** «5–11 окт. · чётная» — чётность строчными; если не влезает, переносится «· чётная» целиком. */
    fun caption(range: String, parity: String): String =
        if (range.isBlank()) parity.lowercase(Locale("ru")) else "$range ·$NBSP${parity.lowercase(Locale("ru"))}"

    /**
     * Название пары: только «С++»/«C++» без разрыва между буквой и плюсами (word joiner). Дефис в названии
     * обычный — «Научно-исследовательская» должна переноситься при крупном шрифте (#113, follow-up).
     */
    fun keepCpp(text: String): String =
        // Латинская и кириллическая «С» (\u0421, \u0441) перед «++».
        text.replace(Regex("([Cc\u0421\u0441])\\+\\+")) { "${it.groupValues[1]}\u2060+\u2060+" }

    /** Аудитория: дефис внутри «ВЦ-3», «А-101» — неразрывный. Текст на экране тот же, меняется только место переноса. */
    fun noBreakRoom(text: String): String =
        keepCpp(text).replace(Regex("(?<=[\\p{L}\\d])-(?=[\\p{L}\\d])"), "\u2011")
}
