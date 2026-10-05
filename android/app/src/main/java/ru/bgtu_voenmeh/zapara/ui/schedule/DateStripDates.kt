package ru.bgtu_voenmeh.zapara.ui.schedule

import java.time.LocalDate

internal fun dateStripDates(selected: LocalDate, count: Int): List<LocalDate> {
    require(count in 1..31)
    val start = selected.minusDays((count / 2).toLong())
    return (0 until count).map { start.plusDays(it.toLong()) }
}
