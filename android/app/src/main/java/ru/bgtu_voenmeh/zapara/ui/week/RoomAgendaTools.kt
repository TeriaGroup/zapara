package ru.bgtu_voenmeh.zapara.ui.week

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.*
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.LocalDate

@Composable internal fun RoomAgendaTools(state: WeekUiState, event: (WeekEvent) -> Unit, map: (String) -> Unit) {
    var open by remember(state.profileName, state.groupId) { mutableStateOf(false) }
    var query by remember(state.profileName, state.groupId) { mutableStateOf("") }
    var roomId by remember(state.profileName, state.groupId) { mutableStateOf<String?>(null) }
    val context = LocalContext.current
    val dark = Zapara.colors.isDark
    ZActionButton(stringResource(R.string.ux300_agenda_room), {
        open = true; event(WeekEvent.RoomDate(state.roomAgenda?.date ?: state.selectedDate))
    }, tag = "Week.RoomAgenda")
    if (open) ZBottomSheet({ open = false }, "Week.RoomAgendaSheet", scrollable = true) {
        Text(stringResource(R.string.ux300_agenda_room), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_room_scope), style = Zapara.typography.caption)
        val agenda = state.roomAgenda
        val date = agenda?.date ?: state.selectedDate
        ZActionButton(agendaDateLabel(date), {
            android.app.DatePickerDialog(context, if (dark) R.style.Zapara_DatePicker_Dark else R.style.Zapara_DatePicker_Light,
                { _, y, m, d -> event(WeekEvent.RoomDate(LocalDate.of(y, m + 1, d))) }, date.year, date.monthValue - 1, date.dayOfMonth).show()
        }, tag = "Week.RoomAgendaDate", leadingIcon = R.drawable.ic_calendar)
        if (agenda == null) Text(stringResource(R.string.ux300_room_loading))
        else {
            Text(stringResource(R.string.ux300_room_coverage, agenda.groups.size, agenda.unmatched), style = Zapara.typography.caption)
            Text(agenda.groups.joinToString(", "), style = Zapara.typography.caption)
            ZTextField(query, { query = it }, Modifier.fillMaxWidth(), placeholder = { Text(stringResource(R.string.ux300_room_search)) }, singleLine = true)
            if (roomId == null) agenda.places.filter { it.label.contains(query.trim(), true) }.take(80).forEach { place ->
                ZActionButton(place.label, { roomId = place.id })
            } else {
                Text(agenda.places.firstOrNull { it.id == roomId }?.label ?: stringResource(R.string.ux300_room_missing), style = Zapara.typography.bodyStrong)
                ZActionButton(stringResource(R.string.ux300_room_choose), { roomId = null })
                val rows = agenda.rows.filter { it.nodeId == roomId }
                if (agenda.unknown) Text(stringResource(R.string.ux300_room_unknown))
                else if (rows.isEmpty()) Text(stringResource(R.string.ux300_room_empty))
                rows.forEach { row -> ZCard(Modifier.fillMaxWidth()) {
                    Text("${row.lesson.timeStart}–${row.lesson.timeEnd} · ${row.groupName}", style = Zapara.typography.bodyStrong)
                    Text(row.lesson.subjectRaw, style = Zapara.typography.body)
                    ZActionButton(stringResource(R.string.ux300_room_map), { open = false; map(row.lesson.classroomRaw) })
                } }
            }
        }
    }
}
