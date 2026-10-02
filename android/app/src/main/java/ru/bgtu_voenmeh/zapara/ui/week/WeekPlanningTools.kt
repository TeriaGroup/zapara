package ru.bgtu_voenmeh.zapara.ui.week

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.LocalDate

@Composable
internal fun WeekPlanningTools(state: WeekUiState, onEvent: (WeekEvent) -> Unit, onHomework: (Long) -> Unit) {
    val context = LocalContext.current
    val dark = Zapara.colors.isDark
    var tasksOpen by remember(state.groupId, state.profileName) { mutableStateOf(false) }
    var changesOpen by remember(state.groupId, state.profileName) { mutableStateOf(false) }
    ZButton(stringResource(R.string.ux300_agenda_changes), { changesOpen = true }, ghost = true, tag = "Week.RefreshChanges")
    if (changesOpen) ZBottomSheet({ changesOpen = false }, "Week.RefreshChangesSheet", scrollable = true) {
        Text(stringResource(R.string.ux300_agenda_changes), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_refresh_scope), style = Zapara.typography.caption)
        val changes = state.refreshChanges
        if (changes == null) Text(stringResource(R.string.ux300_refresh_none))
        else if (changes.isEmpty()) Text(stringResource(R.string.ux300_refresh_same))
        else changes.forEach { change -> ZCard(Modifier.fillMaxWidth()) {
            Text(change.afterDate.toString(), style = Zapara.typography.bodyStrong)
            change.removed.forEach { Text(stringResource(R.string.ux300_ext_week_removed, change.beforeDate.toString(), "${it.time} · ${it.subjectRaw} · ${it.classroomRaw}")) }
            change.added.forEach { Text(stringResource(R.string.ux300_ext_week_added, change.afterDate.toString(), "${it.time} · ${it.subjectRaw} · ${it.classroomRaw}")) }
        } }
    }
    ZButton(stringResource(R.string.ux300_ext_compare_weeks), {
        val date = state.comparisonDays.firstOrNull()?.date ?: state.selectedDate.plusWeeks(1)
        android.app.DatePickerDialog(context, if (dark) R.style.Zapara_DatePicker_Dark else R.style.Zapara_DatePicker_Light,
            { _, year, month, day -> onEvent(WeekEvent.Compare(LocalDate.of(year, month + 1, day))) },
            date.year, date.monthValue - 1, date.dayOfMonth).show()
    }, ghost = true, tag = "Week.Compare")
    ZButton(stringResource(R.string.ux300_ext_week_deadlines, state.deadlines.count { !it.done }), { tasksOpen = true },
        ghost = true, tag = "Week.Homework")
    if (state.comparisonDays.isNotEmpty()) ZBottomSheet({ onEvent(WeekEvent.Compare(null)) }, "Week.Comparison", scrollable = true) {
        Text(stringResource(R.string.ux300_ext_compare_weeks), style = Zapara.typography.section)
        Text("${state.days.firstOrNull()?.date}–${state.days.lastOrNull()?.date} / ${state.comparisonDays.first().date}–${state.comparisonDays.last().date}",
            style = Zapara.typography.caption)
        Text(stringResource(R.string.ux300_ext_week_comparison_hint), style = Zapara.typography.caption)
        compareWeeks(state.days, state.comparisonDays).forEach { change ->
            ZCard(Modifier.fillMaxWidth()) {
                Text(Parity.dayNumberToTitle(change.day), style = Zapara.typography.bodyStrong)
                if (change.removed.isEmpty() && change.added.isEmpty()) Text(stringResource(R.string.ux300_ext_week_same), style = Zapara.typography.caption)
                change.removed.forEach { row -> Text(stringResource(R.string.ux300_ext_week_removed, change.beforeDate.toString(),
                    "${row.time} · ${row.name} · ${row.teacher} · ${row.room}"), style = Zapara.typography.body) }
                change.added.forEach { row -> Text(stringResource(R.string.ux300_ext_week_added, change.afterDate.toString(),
                    "${row.time} · ${row.name} · ${row.teacher} · ${row.room}"), style = Zapara.typography.body) }
            }
        }
    }
    if (tasksOpen) ZBottomSheet({ tasksOpen = false }, "Week.Deadlines", scrollable = true) {
        Text(stringResource(R.string.ux300_ext_week_deadlines, state.deadlines.count { !it.done }), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_ext_week_tasks_hint), style = Zapara.typography.caption)
        state.days.forEach { day ->
            val tasks = state.deadlines.filter { it.date == day.date }
            if (tasks.isNotEmpty()) {
                Text(day.title, style = Zapara.typography.bodyStrong)
                tasks.forEach { task -> ZCard(Modifier.fillMaxWidth()) {
                    Text("${if (task.done) "[x]" else "[ ]"} ${task.subject}", style = Zapara.typography.bodyStrong)
                    Text(task.text, style = Zapara.typography.body)
                    ZButton(stringResource(R.string.ux300_ext_open_task), { onHomework(task.id) }, ghost = true)
                } }
            }
        }
        if (state.deadlines.isEmpty()) Text(stringResource(R.string.ux300_ext_week_no_tasks), style = Zapara.typography.body)
        if (state.undatedHomework > 0) Text(stringResource(R.string.ux300_ext_undated_tasks, state.undatedHomework), style = Zapara.typography.caption)
    }
}
