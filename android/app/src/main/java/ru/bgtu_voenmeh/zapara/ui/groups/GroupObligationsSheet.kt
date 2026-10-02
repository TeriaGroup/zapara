package ru.bgtu_voenmeh.zapara.ui.groups

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable internal fun GroupObligationsSheet(state: GroupUiState, event: (GroupEvent) -> Unit) {
    if (!state.obligationsOpen || state.preview != null || state.accessRevoked) return
    var needsMe by remember(state.ownerId, state.communityId) { mutableStateOf(false) }
    var now by remember(state.ownerId, state.communityId) { mutableStateOf(Instant.now()) }
    LaunchedEffect(state.ownerId, state.communityId) {
        while (true) { kotlinx.coroutines.delay(30_000); now = Instant.now() }
    }
    ZBottomSheet({ event(GroupEvent.CloseObligations) }, "Group.ObligationsSheet", scrollable = true) {
        Text(stringResource(R.string.ux300_group_obligations), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_group_obligations_scope), style = Zapara.typography.caption)
        ZButton(stringResource(if (needsMe) R.string.ux300_group_obligations_all else R.string.ux300_group_obligations_mine),
            { needsMe = !needsMe }, ghost = true)
        if (state.obligationsLoading) Text(stringResource(R.string.ux300_room_loading))
        else {
            val data = state.obligations
            if (data == null) {
                Text(stringResource(R.string.ux300_group_obligations_failed))
                ZButton(stringResource(R.string.repeat), { event(GroupEvent.Obligations) }, ghost = true)
            } else {
                Text(stringResource(R.string.ux300_group_obligations_coverage, data.loaded, data.requested, data.failed, data.skipped), style = Zapara.typography.caption)
                if (data.completionUnknown > 0) Text(stringResource(R.string.ux300_group_completion_unknown, data.completionUnknown), style = Zapara.typography.caption)
                val rows = browseObligations(data.rows, needsMe, now)
                if (rows.isEmpty()) Text(stringResource(R.string.ux300_group_obligations_empty))
                rows.forEach { row -> ZCard(Modifier.fillMaxWidth()) {
                    Text(row.title, style = Zapara.typography.bodyStrong)
                    Text(row.deadline?.atZone(ZoneId.systemDefault())?.format(DateTimeFormatter.ofPattern("dd.MM HH:mm"))
                        ?: stringResource(R.string.ux300_group_obligations_undated), style = Zapara.typography.caption)
                    ZButton(stringResource(R.string.ux300_group_obligations_open), { event(GroupEvent.OpenObligation(row)) }, ghost = true)
                } }
                if (data.unread.isNotEmpty()) {
                    Text(stringResource(R.string.ux300_group_obligations_unread), style = Zapara.typography.bodyStrong)
                    data.unread.forEach { topic -> ZButton("${topic.title} · ${topic.unread}", {
                        event(GroupEvent.CloseObligations); event(GroupEvent.OpenChannel(topic.topicId))
                    }, ghost = true) }
                }
            }
        }
    }
}
