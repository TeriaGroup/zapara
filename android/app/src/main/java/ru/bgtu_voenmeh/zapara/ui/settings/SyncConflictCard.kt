package ru.bgtu_voenmeh.zapara.ui.settings

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.sync.*
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun SyncConflictCard(conflict: SyncConflict, busy: Boolean, choose: (Boolean) -> Unit) {
    val c = Zapara.colors
    val tag = "Sync.Conflict.${conflict.operation.opId}"
    ZCard(Modifier.fillMaxWidth().testTag(tag)) {
        Text(conflictTitle(conflict.operation.entityType), style = Zapara.typography.section, color = c.text1)
        Text(stringResource(R.string.sync_local_version), style = Zapara.typography.bodyStrong, color = c.text1)
        Text(syncValueDescription(conflict.localValue), style = Zapara.typography.body, color = c.text1,
            modifier = Modifier.testTag("$tag.Local"))
        Text(stringResource(R.string.sync_server_version), style = Zapara.typography.bodyStrong, color = c.text1)
        Text(if (conflict.parentRecord?.tombstone == true) stringResource(R.string.sync_homework_deleted)
            else syncValueDescription(conflict.serverRecord?.value), style = Zapara.typography.body, color = c.text1,
            modifier = Modifier.testTag("$tag.Server"))
        if (!conflict.canKeepLocal) Text(stringResource(R.string.sync_local_unavailable), style = Zapara.typography.caption, color = c.warn)
        else if (conflict.serverRecord?.tombstone == true || conflict.parentRecord?.tombstone == true)
            Text(stringResource(R.string.sync_restore_copy), style = Zapara.typography.caption, color = c.text2)
        ZButton(stringResource(R.string.sync_keep_local), { choose(true) }, Modifier.fillMaxWidth(),
            enabled = !busy && conflict.canKeepLocal, tag = "$tag.KeepLocal")
        ZButton(stringResource(R.string.sync_keep_server), { choose(false) }, Modifier.fillMaxWidth(),
            enabled = !busy, ghost = true, tag = "$tag.KeepServer")
    }
}

@Composable
internal fun conflictTitle(type: String): String = stringResource(when (type) {
    "homework" -> R.string.sync_type_homework
    "completion" -> R.string.sync_type_completion
    "override" -> R.string.sync_type_override
    "friend" -> R.string.sync_type_friend
    else -> R.string.sync_type_settings
})

@Composable
internal fun syncValueDescription(value: SyncValue?): String = when (value) {
    null -> stringResource(R.string.sync_value_missing)
    is HomeworkValue -> stringResource(R.string.sync_value_homework, value.subjectRaw, value.text, value.targetNthOccurrence)
    is CompletionValue -> stringResource(if (value.done) R.string.sync_value_done else R.string.sync_value_not_done)
    is OverrideValue -> stringResource(R.string.sync_value_override, value.subjectRaw, value.displayName, value.note.orEmpty(),
        if (value.scope == "global") stringResource(R.string.sync_value_all_days)
        else stringResource(R.string.sync_value_weekday, value.scope.removePrefix("weekday:")))
    is FriendValue -> stringResource(R.string.sync_value_friend, value.groupName, value.memberNames, value.paletteIndex,
        stringResource(if (value.enabled) R.string.sync_value_enabled else R.string.sync_value_disabled))
    is SettingsValue -> stringResource(R.string.sync_value_settings,
        value.selectedGroupId ?: stringResource(R.string.sync_value_not_selected),
        stringResource(if (value.parityInvert) R.string.sync_value_yes else R.string.sync_value_no),
        value.notifyTime1 ?: stringResource(R.string.sync_value_time_off),
        value.notifyTime2 ?: stringResource(R.string.sync_value_time_off), value.strictness,
        stringResource(if (value.alwaysShow) R.string.sync_value_yes else R.string.sync_value_no))
}
