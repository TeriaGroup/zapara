package ru.bgtu_voenmeh.zapara.ui.settings

import androidx.compose.foundation.layout.*
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.sync.CloudSyncStatus
import ru.bgtu_voenmeh.zapara.data.sync.PrivateSyncState
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
internal fun CloudSyncSummary(status: CloudSyncStatus) {
    Column(Modifier.fillMaxWidth().testTag("Sync.Status"), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(stringResource(R.string.cloud_sync_title), style = Zapara.typography.bodyStrong)
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            if (status.running) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
            Text(stringResource(when {
                status.running -> R.string.cloud_sync_running
                status.failure == PrivateSyncState.NeedsReauthentication -> R.string.cloud_sync_login
                !status.attached || status.failure != null -> R.string.cloud_sync_unavailable
                status.conflicts > 0 -> R.string.cloud_sync_needs_choice
                status.upToDate -> R.string.cloud_sync_ready
                else -> R.string.cloud_sync_waiting
            }), style = Zapara.typography.body, color = Zapara.colors.text1)
        }
        if (status.pending > 0) Text(stringResource(R.string.cloud_sync_pending, status.pending),
            style = Zapara.typography.caption, color = Zapara.colors.text2)
        if (status.conflicts > 0) Text(stringResource(R.string.cloud_sync_conflicts, status.conflicts),
            style = Zapara.typography.caption, color = Zapara.colors.bad)
        status.lastSuccess?.let {
            Text(stringResource(R.string.cloud_sync_last, it.atZone(ZoneId.systemDefault())
                .format(DateTimeFormatter.ofPattern("dd.MM HH:mm"))), style = Zapara.typography.caption, color = Zapara.colors.text2)
        }
    }
}
