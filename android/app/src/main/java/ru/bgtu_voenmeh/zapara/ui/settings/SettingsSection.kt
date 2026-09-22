package ru.bgtu_voenmeh.zapara.ui.settings

import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.constrainHeight
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.AutoUpdate
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.account.AccountCard
import ru.bgtu_voenmeh.zapara.ui.legal.LegalDocumentPage
import ru.bgtu_voenmeh.zapara.ui.account.AccountEvent
import ru.bgtu_voenmeh.zapara.ui.account.AccountUiState
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
private fun GroupActions(refreshing: Boolean, onChangeGroup: () -> Unit, onRefresh: () -> Unit) {
    val spacing = Zapara.space.s
    Layout(
        modifier = Modifier.fillMaxWidth(),
        content = {
            ZButton(stringResource(R.string.ux_settings_change), onChangeGroup, ghost = true, tag = "Settings.GroupChange")
            ZButton(stringResource(R.string.ux_settings_refresh), onRefresh, enabled = !refreshing, tag = "Settings.Refresh")
        }
    ) { measurables, constraints ->
        val gap = spacing.roundToPx()
        // Intrinsics include the actual font, full label, button padding and touch minimum.
        val widths = measurables.map { it.maxIntrinsicWidth(Constraints.Infinity) }
        val stacked = widths.sumOf { it.toLong() } + gap > constraints.maxWidth
        val buttons = measurables.mapIndexed { index, measurable ->
            val width = if (stacked) constraints.maxWidth else widths[index]
            measurable.measure(constraints.copy(minWidth = width, maxWidth = width, minHeight = 0))
        }
        val height = if (stacked) buttons.sumOf { it.height } + gap else buttons.maxOf { it.height }
        layout(constraints.maxWidth, constraints.constrainHeight(height)) {
            if (stacked) {
                buttons[0].placeRelative(0, 0)
                buttons[1].placeRelative(0, buttons[0].height + gap)
            } else {
                buttons[0].placeRelative(0, (height - buttons[0].height) / 2)
                buttons[1].placeRelative(buttons[0].width + gap, (height - buttons[1].height) / 2)
            }
        }
    }
}

@Composable
fun SettingsSection(
    state: SettingsUiState,
    onEvent: (SettingsEvent) -> Unit,
    updates: UpdateUiState,
    onChangeGroup: () -> Unit,
    account: AccountUiState = AccountUiState(),
    onAccount: (AccountEvent) -> Unit = {}
) {
    val ctx = LocalContext.current
    val c = Zapara.colors
    var legalId by remember { mutableStateOf<String?>(null) }
    if (legalId != null) {
        LegalDocumentPage(legalId!!, onClose = { legalId = null })
        return
    }
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_settings))
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            item { AccountCard(account, onAccount) { legalId = it } }
            if (state.syncConflicts.isNotEmpty() || state.syncError != null) item {
                ZCard(Modifier.fillMaxWidth().testTag("Sync.Conflicts")) {
                    Text(stringResource(R.string.sync_conflict_title), style = Zapara.typography.section, color = c.text1)
                    Text(stringResource(R.string.sync_conflict_body), style = Zapara.typography.body, color = c.text2)
                    state.syncError?.let { Text(it, style = Zapara.typography.body, color = c.bad) }
                }
            }
            items(state.syncConflicts, key = { "conflict:${it.operation.opId}" }) { conflict ->
                SyncConflictCard(conflict, state.syncBusy) { keepLocal -> onEvent(SettingsEvent.ResolveSync(conflict, keepLocal)) }
            }
            item {
                ZCard(Modifier.fillMaxWidth().testTag("Settings.Group")) {
                    Text(stringResource(R.string.settings_group), style = Zapara.typography.caption, color = c.text2)
                    Text(state.groupName.ifBlank { stringResource(R.string.group_pick) }, style = Zapara.typography.section, color = c.text1)
                    Text(state.groupUpdated, style = Zapara.typography.caption, color = if (state.stale) c.warn else c.text2)
                    GroupActions(state.refreshing, onChangeGroup) { onEvent(SettingsEvent.Refresh) }
                }
            }
            if (state.apiConfigured) item {
                ZCard(Modifier.fillMaxWidth().testTag("Settings.Source")) {
                    Text(stringResource(R.string.settings_source), style = Zapara.typography.section, color = c.text1)
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.settings_source_university), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                        ZSwitch(state.useUniversityXml, { onEvent(SettingsEvent.UseUniversityXml(it)) }, "Settings.UniversityXml")
                    }
                    Text(stringResource(R.string.settings_source_hint), style = Zapara.typography.caption, color = c.text2)
                }
            }
            item {
                ZCard(Modifier.fillMaxWidth()) {
                    Text(stringResource(R.string.theme_appearance), style = Zapara.typography.section, color = c.text1)
                    ZSegmented(
                        listOf(stringResource(R.string.theme_system), stringResource(R.string.theme_light), stringResource(R.string.theme_dark)),
                        state.theme.ordinal, { onEvent(SettingsEvent.Theme(it)) }, "Settings.Theme"
                    )
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.theme_animations), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                        ZSwitch(state.animations, { onEvent(SettingsEvent.Animations(it)) }, "Settings.Animations")
                    }
                }
            }
            item {
                ZCard(Modifier.fillMaxWidth().testTag("Settings.Maps")) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        Text(stringResource(R.string.settings_maps), style = Zapara.typography.section, color = c.text1, modifier = Modifier.weight(1f))
                        ZChip(stringResource(R.string.settings_maps_alpha), selected = true, tag = "Settings.MapsAlphaBadge")
                    }
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.settings_maps_routes), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                        ZSwitch(state.mapsAlpha, { onEvent(SettingsEvent.MapsAlpha(it)) }, "Settings.MapsAlpha")
                    }
                    Text(stringResource(R.string.settings_maps_alpha_hint), style = Zapara.typography.caption, color = c.text2)
                }
            }
            item {
                ZCard(Modifier.fillMaxWidth()) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.settings_notify), style = Zapara.typography.section, color = c.text1, modifier = Modifier.weight(1f))
                        ZSwitch(state.notifyEnabled, { onEvent(SettingsEvent.Notify(it)) }, "Settings.Notify")
                    }
                    TimeField(state.time1, stringResource(R.string.settings_time_evening), "Settings.Time1") { onEvent(SettingsEvent.Time1(it)) }
                    TimeField(state.time2, stringResource(R.string.settings_time_morning), "Settings.Time2") { onEvent(SettingsEvent.Time2(it)) }
                    state.timeError?.let { Text(it, style = Zapara.typography.caption, color = c.bad) }
                    ZButton(stringResource(R.string.settings_notify_test), { onEvent(SettingsEvent.TestNotification) }, ghost = true, tag = "Settings.NotifyTest")
                    if (state.permissionMissing) {
                        Text(stringResource(R.string.settings_perm_notify), style = Zapara.typography.caption, color = c.warn)
                        ZButton(stringResource(R.string.settings_open_notify), {
                            ctx.startActivity(Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, ctx.packageName))
                        }, ghost = true)
                    } else if (state.exactAlarmMissing) {
                        Text(stringResource(R.string.settings_perm_alarm), style = Zapara.typography.caption, color = c.warn)
                        ZButton(stringResource(R.string.settings_open_alarm), {
                            ctx.startActivity(Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM))
                        }, ghost = true)
                    }
                }
            }
            item {
                if (state.selfUpdate) UpdatesCard(state, updates, onEvent)
                else RustoreUpdatesCard()
            }
            item { AboutCard(state.version) }
        }
    }
}

@Composable
private fun TimeField(value: String, label: String, tag: String, onChange: (String) -> Unit) {
    val c = Zapara.colors
    OutlinedTextField(
        value = value, onValueChange = onChange,
        modifier = Modifier.fillMaxWidth().testTag(tag),
        label = { Text(label, style = Zapara.typography.caption) },
        singleLine = true,
        shape = RoundedCornerShape(Zapara.radii.control),
        colors = OutlinedTextFieldDefaults.colors(
            focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
            focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
            focusedTextColor = c.text1, unfocusedTextColor = c.text1
        )
    )
}

@Composable
private fun RustoreUpdatesCard() {
    val ctx = LocalContext.current
    val c = Zapara.colors
    ZCard(Modifier.fillMaxWidth().testTag("Settings.RustoreUpdates")) {
        Text(stringResource(R.string.settings_updates), style = Zapara.typography.section, color = c.text1)
        Text(stringResource(R.string.settings_rustore), style = Zapara.typography.body, color = c.text1)
        Text(stringResource(R.string.settings_rustore_early), style = Zapara.typography.body, color = c.text1)
        ZButton(stringResource(R.string.settings_rustore_github), {
            ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(AutoUpdate.RELEASES_PAGE)))
        }, ghost = true, tag = "Settings.RustoreGithub")
    }
}

@Composable
fun UpdatesCard(state: SettingsUiState, updates: UpdateUiState, onEvent: (SettingsEvent) -> Unit) {
    val c = Zapara.colors
    ZCard(Modifier.fillMaxWidth()) {
        Text(stringResource(R.string.settings_updates), style = Zapara.typography.section, color = c.text1)
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.settings_auto_update), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
            ZSwitch(state.autoUpdate, { onEvent(SettingsEvent.AutoUpdate(it)) }, "Settings.AutoUpdate")
        }
        val status = when {
            updates.error != null -> updates.error
            updates.hasUpdate -> stringResource(R.string.settings_upd_available, updates.tag)
            updates.upToDate -> stringResource(R.string.settings_upd_current_at, state.version, updates.checkedAt)
            else -> updates.log.ifBlank { stringResource(R.string.settings_upd_current, state.version) }
        }
        Text(status, style = Zapara.typography.body, color = c.text1, modifier = Modifier.testTag("Settings.UpdStatus"))
        if (updates.log.isNotBlank()) Text(updates.log, style = Zapara.typography.caption, color = c.text3)
        if (updates.downloading) {
            LinearProgressIndicator(progress = { updates.progress.coerceAtLeast(0f) }, modifier = Modifier.fillMaxWidth(), color = c.accent, trackColor = c.chip)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.settings_upd_check), { onEvent(SettingsEvent.CheckUpdate) }, ghost = true, tag = "Settings.UpdCheck")
            if (updates.hasUpdate) ZButton(stringResource(R.string.settings_upd_download), { onEvent(SettingsEvent.DownloadUpdate) }, tag = "Settings.UpdDownload")
            if (updates.readyFile != null) ZButton(stringResource(R.string.settings_upd_install), { onEvent(SettingsEvent.InstallUpdate) }, tag = "Settings.UpdInstall")
            if (updates.downloading) ZButton(stringResource(R.string.theme_cancel), { onEvent(SettingsEvent.CancelUpdate) }, ghost = true)
        }
    }
}

@Composable
fun AboutCard(version: String) {
    val ctx = LocalContext.current
    val c = Zapara.colors
    ZCard(Modifier.fillMaxWidth().testTag("Settings.About")) {
        Text(stringResource(R.string.settings_about_title), style = Zapara.typography.section, color = c.text1)
        Text(stringResource(R.string.settings_version, version), style = Zapara.typography.caption, color = c.text2)
        Text(stringResource(R.string.settings_unofficial), style = Zapara.typography.body, color = c.text2)
        ZButton(stringResource(R.string.settings_releases), {
            ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(AutoUpdate.RELEASES_PAGE)))
        }, ghost = true, tag = "Settings.Releases")
        Text(stringResource(R.string.settings_licenses), style = Zapara.typography.caption, color = c.text2)
    }
}
