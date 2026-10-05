package ru.bgtu_voenmeh.zapara.ui.friends

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.LocalDate

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun FriendMeetingPlanner(state: FriendsUiState, onEvent: (FriendsEvent) -> Unit) {
    val context = LocalContext.current
    var selected by rememberSaveable(state.profileName, state.myGroupId) { mutableStateOf("") }
    var minimum by rememberSaveable(state.profileName, state.myGroupId) { mutableStateOf(15) }
    val group = state.meetingWindows.firstOrNull { it.group == selected } ?: state.meetingWindows.firstOrNull()
    val dark = Zapara.colors.isDark
    ZCard(Modifier.fillMaxWidth(), tag = "Friends.MeetingPlanner") {
        Text(stringResource(R.string.ux300_ext_meeting_title), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_ext_meeting_scope), style = Zapara.typography.caption)
        ZButton(state.meetingDate.toString(), {
            val date = state.meetingDate
            android.app.DatePickerDialog(context, if (dark) R.style.Zapara_DatePicker_Dark else R.style.Zapara_DatePicker_Light,
                { _, year, month, day -> onEvent(FriendsEvent.MeetingDate(LocalDate.of(year, month + 1, day))) },
                date.year, date.monthValue - 1, date.dayOfMonth).show()
        }, ghost = true, tag = "Friends.MeetingDate")
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            state.meetingWindows.forEach { row -> ZChip(row.group, selected = row.group == group?.group,
                onClick = { selected = row.group }) }
        }
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            listOf(15, 30, 60).forEach { minutes -> ZChip(stringResource(R.string.ux300_ext_minimum_minutes, minutes),
                selected = minimum == minutes, onClick = { minimum = minutes }) }
        }
        val windows = group?.windows.orEmpty().filter { it.minutes >= minimum }
        if (windows.isEmpty()) Text(stringResource(R.string.ux300_ext_no_verified_window), style = Zapara.typography.body)
        windows.forEach { window -> Text(stringResource(R.string.ux300_ext_meeting_window, window.start, window.end, window.minutes),
            style = Zapara.typography.bodyStrong) }
    }
}
