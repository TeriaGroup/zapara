package ru.bgtu_voenmeh.zapara.ui.week

import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

private val agendaDateFormat = DateTimeFormatter.ofPattern("d MMM yyyy", Locale.forLanguageTag("ru"))
internal fun agendaDateLabel(date: LocalDate): String = date.format(agendaDateFormat)
