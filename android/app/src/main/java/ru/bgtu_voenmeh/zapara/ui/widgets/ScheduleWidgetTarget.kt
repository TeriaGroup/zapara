package ru.bgtu_voenmeh.zapara.ui.widgets

import java.time.LocalDate
import ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchScope

internal data class ScheduleWidgetTarget(val date: LocalDate, val time: String,
    val subjectNorm: String, val groupId: String, val scope: WidgetLaunchScope)

internal fun scheduleWidgetTarget(row: ScheduleWidgetRow, scope: WidgetLaunchScope): ScheduleWidgetTarget? {
    val date = row.date ?: return null
    val time = row.timeStart?.takeIf(String::isNotBlank) ?: return null
    val subject = row.subjectNorm?.takeIf(String::isNotBlank) ?: return null
    val group = row.groupId?.takeIf(String::isNotBlank) ?: return null
    if (scope.profileId.isBlank() || scope.databaseName.isBlank()) return null
    return ScheduleWidgetTarget(date, time, subject, group, scope)
}
