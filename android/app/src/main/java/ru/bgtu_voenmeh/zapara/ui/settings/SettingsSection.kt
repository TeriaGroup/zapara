package ru.bgtu_voenmeh.zapara.ui.settings

import android.content.Intent
import android.net.Uri
import android.provider.OpenableColumns
import android.provider.Settings
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.annotation.DrawableRes
import androidx.activity.result.contract.ActivityResultContracts
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import androidx.compose.runtime.rememberCoroutineScope
import java.io.ByteArrayOutputStream
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.background
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import ru.bgtu_voenmeh.zapara.ui.components.rememberUiText
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.constrainHeight
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.AutoUpdate
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.account.AccountCard
import ru.bgtu_voenmeh.zapara.ui.legal.LegalDocumentPage
import ru.bgtu_voenmeh.zapara.ui.account.AccountEvent
import ru.bgtu_voenmeh.zapara.ui.account.AccountUiState
import ru.bgtu_voenmeh.zapara.ui.chat.SupportForm
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
private fun GroupActions(refreshing: Boolean, onChangeGroup: () -> Unit, onRefresh: () -> Unit) {
    val uiText = rememberUiText()
    val spacing = Zapara.space.s
    Layout(
        modifier = Modifier.fillMaxWidth(),
        content = {
            ZButton(stringResource(R.string.ux_settings_change), onChangeGroup, ghost = true, tag = "Settings.GroupChange", leadingIcon = R.drawable.ic_calendar)
            ZButton(stringResource(R.string.ux_settings_refresh), onRefresh, enabled = !refreshing, tag = "Settings.Refresh", leadingIcon = R.drawable.ic_refresh)
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
private fun SettingsOverviewRow(
    @DrawableRes icon: Int,
    title: String,
    summary: String,
    tag: String,
    emphasized: Boolean = false,
    divider: Boolean = true,
    onClick: () -> Unit
) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    Column {
        Surface(
            onClick = onClick,
            modifier = Modifier.fillMaxWidth().testTag(tag),
            shape = RoundedCornerShape(if (emphasized) Zapara.radii.card else 0.dp),
            color = c.card,
            border = if (emphasized) BorderStroke(Zapara.space.hairline, c.line) else null
        ) {
            Row(
                Modifier.fillMaxWidth().heightIn(min = 64.dp).padding(horizontal = Zapara.space.m, vertical = Zapara.space.m),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)
            ) {
                Box(Modifier.size(36.dp).background(c.chip, RoundedCornerShape(Zapara.radii.control)),
                    contentAlignment = Alignment.Center) {
                    Icon(painterResource(icon), contentDescription = null, tint = c.text1, modifier = Modifier.size(20.dp))
                }
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    Text(title, style = Zapara.typography.bodyStrong, color = c.text1)
                    Text(summary, style = Zapara.typography.caption, color = c.text2)
                }
                Icon(painterResource(R.drawable.ic_chevron_right), contentDescription = null,
                    tint = c.text2, modifier = Modifier.size(20.dp))
            }
        }
        if (!emphasized && divider) HorizontalDivider(Modifier.padding(start = 56.dp, end = Zapara.space.m), color = c.line, thickness = Zapara.space.hairline)
    }
}

@Composable
private fun SettingsOverview(state: SettingsUiState, account: AccountUiState, onOpen: (String) -> Unit) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        item {
            SettingsOverviewRow(R.drawable.ic_users, stringResource(R.string.account_title),
                if (account.guest) stringResource(R.string.settings_overview_guest)
                else account.accountName.ifBlank { stringResource(R.string.account_title) },
                "Settings.Overview.Account", emphasized = true) { onOpen("account") }
        }
        if (state.syncConflicts.isNotEmpty() || state.syncError != null) item {
            SettingsOverviewRow(R.drawable.ic_refresh, stringResource(R.string.sync_conflict_title),
                state.syncError ?: stringResource(R.string.sync_conflict_body), "Settings.Overview.Sync", emphasized = true) {
                onOpen("data")
            }
        }
        item { Text(stringResource(R.string.settings_overview_core), style = Zapara.typography.section,
            color = c.text1, modifier = Modifier.padding(top = Zapara.space.l, bottom = Zapara.space.xs)) }
        item("core") { ZCard(Modifier.fillMaxWidth(), padded = false) {
            Column {
                SettingsOverviewRow(R.drawable.ic_calendar, stringResource(R.string.settings_overview_study),
                    state.groupName.ifBlank { stringResource(R.string.group_pick) }, "Settings.Overview.Study") { onOpen("study") }
                SettingsOverviewRow(R.drawable.ic_sun, stringResource(R.string.theme_appearance),
                    listOf(stringResource(R.string.theme_system), stringResource(R.string.theme_light), stringResource(R.string.theme_dark))[state.theme.ordinal],
                    "Settings.Overview.Appearance") { onOpen("appearance") }
                SettingsOverviewRow(R.drawable.ic_notification, stringResource(R.string.settings_notify),
                    if (state.notifyEnabled) stringResource(R.string.settings_overview_notify_on, state.time1, state.time2)
                    else stringResource(R.string.settings_overview_notify_off),
                    "Settings.Overview.Notifications") { onOpen("notifications") }
                SettingsOverviewRow(R.drawable.ic_map, stringResource(R.string.settings_maps),
                    stringResource(R.string.settings_maps_routes), "Settings.Overview.Maps", divider = false) { onOpen("maps") }
            }
        } }
        item { Text(stringResource(R.string.settings_overview_service), style = Zapara.typography.section,
            color = c.text1, modifier = Modifier.padding(top = Zapara.space.l, bottom = Zapara.space.xs)) }
        item("service") { ZCard(Modifier.fillMaxWidth(), padded = false) {
            Column {
                SettingsOverviewRow(R.drawable.ic_refresh, uiText(R.string.space_day_153),
                    if (state.syncBusy) uiText(R.string.space_day_154) else if (state.signedIn) uiText(R.string.space_day_155) else uiText(R.string.space_day_156), "Settings.Overview.Data") { onOpen("data") }
                SettingsOverviewRow(R.drawable.ic_download, stringResource(R.string.settings_updates),
                    stringResource(R.string.settings_version, state.version), "Settings.Overview.Updates") { onOpen("updates") }
                SettingsOverviewRow(R.drawable.ic_file, stringResource(R.string.settings_overview_help),
                    stringResource(R.string.settings_overview_help_summary), "Settings.Overview.Help", divider = false) { onOpen("help") }
            }
        } }
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
    val uiText = rememberUiText()
    val ctx = LocalContext.current
    val c = Zapara.colors
    var legalId by remember { mutableStateOf<String?>(null) }
    var section by rememberSaveable { mutableStateOf<String?>(null) }
    BackHandler(enabled = section != null && legalId == null) { section = null }
    if (legalId != null) {
        LegalDocumentPage(legalId!!, onClose = { legalId = null })
        return
    }
    Column(Modifier.fillMaxSize()) {
        ZTopBar(when (section) {
            "data" -> uiText(R.string.space_day_157)
            "account" -> stringResource(R.string.account_title)
            "study" -> stringResource(R.string.settings_overview_study)
            "appearance" -> stringResource(R.string.theme_appearance)
            "maps" -> stringResource(R.string.settings_maps)
            "notifications" -> stringResource(R.string.settings_notify)
            "updates" -> stringResource(R.string.settings_updates)
            "help" -> stringResource(R.string.settings_overview_help)
            else -> stringResource(R.string.nav_settings)
        })
        BoxWithConstraints(Modifier.fillMaxSize()) {
        val wideSettings = maxWidth >= 840.dp
        if (section == null) SettingsOverview(state, account) { section = it }
        else Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.l)) {
            if (wideSettings) Box(Modifier.width(320.dp)) { SettingsOverview(state, account) { section = it } }
            LazyColumn(if (wideSettings) Modifier.widthIn(max = 720.dp).fillMaxSize() else Modifier.weight(1f).fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            item { ZButton(stringResource(R.string.settings_overview_all), { section = null }, ghost = true, quiet = true, tag = "Settings.Overview.Back", leadingIcon = R.drawable.ic_chevron_left) }
            if (section == "account") {
            item { AccountCard(account, onAccount) { legalId = it } }
            if (state.signedIn) item { ZCard(Modifier.fillMaxWidth()) {
                CloudSyncSummary(state.cloudSync)
                ZButton(uiText(R.string.space_day_sync_now), { onEvent(SettingsEvent.SyncNow) },
                    enabled = !state.syncBusy && state.cloudSync.attached, busy = state.syncBusy)
            } }
            }
            if (section == "data") {
            item { ZCard(Modifier.fillMaxWidth()) {
                if (state.signedIn) CloudSyncSummary(state.cloudSync)
                else Text(uiText(R.string.space_day_159), style = Zapara.typography.body)
                Text(uiText(R.string.space_day_160), style = Zapara.typography.caption)
                Text(state.groupUpdated, style = Zapara.typography.caption)
                if (state.signedIn) {
                    Text(stringResource(R.string.cloud_sync_scope), style = Zapara.typography.caption, color = c.text2)
                    ZButton(uiText(R.string.space_day_sync_now), { onEvent(SettingsEvent.SyncNow) },
                        enabled = !state.syncBusy && state.cloudSync.attached, busy = state.syncBusy)
                }
            } }
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
            }
            if (section == "study") {
            item {
                ZCard(Modifier.fillMaxWidth().testTag("Settings.Group")) {
                    Text(stringResource(R.string.settings_group), style = Zapara.typography.caption, color = c.text2)
                    Text(state.groupName.ifBlank { stringResource(R.string.group_pick) }, style = Zapara.typography.section, color = c.text1)
                    Text(state.groupUpdated, style = Zapara.typography.caption, color = if (state.stale) c.warn else c.text2)
                    Text(uiText(R.string.space_day_study_selection), style = Zapara.typography.caption)
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(uiText(R.string.space_day_16), Modifier.weight(1f), style = Zapara.typography.body)
                        ZSwitch(state.showFreeTime, { onEvent(SettingsEvent.FreeTime(it)) }, "Settings.FreeTime")
                    }
                    var advanced by rememberSaveable { mutableStateOf(false) }
                    ZButton(uiText(R.string.space_day_advanced), { advanced = !advanced }, ghost = true, quiet = true)
                    if (advanced) Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(uiText(R.string.space_day_invert), Modifier.weight(1f), style = Zapara.typography.body)
                        ZSwitch(state.parityInvert, { onEvent(SettingsEvent.Invert(it)) }, "Settings.ParityInvert")
                    }
                    GroupActions(state.refreshing, onChangeGroup) { onEvent(SettingsEvent.Refresh) }
                }
            }
            items(state.subgroupStreams, key = { it.id }) { stream -> ZCard(Modifier.fillMaxWidth()) {
                Text(stream.title, style = Zapara.typography.section)
                Text(uiText(R.string.space_day_subgroup_hint), style = Zapara.typography.caption)
                stream.options.forEach { option ->
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        androidx.compose.material3.RadioButton(state.subgroupChoices[stream.id] == option.id, { onEvent(SettingsEvent.Subgroup(stream.id, option.id)) })
                        Text(option.label, style = Zapara.typography.body)
                    }
                }
            } }
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
            }
            if (section == "appearance") {
            item {
                ZCard(Modifier.fillMaxWidth()) {
                    Text(stringResource(R.string.theme_appearance), style = Zapara.typography.section, color = c.text1)
                    ZSegmented(
                        listOf(stringResource(R.string.theme_system), stringResource(R.string.theme_light), stringResource(R.string.theme_dark)),
                        state.theme.ordinal, { onEvent(SettingsEvent.Theme(it)) }, "Settings.Theme"
                    )
                    ZCard(Modifier.fillMaxWidth()) {
                        Text(uiText(R.string.space_day_161), style = Zapara.typography.section)
                        Text(uiText(R.string.space_day_162), style = Zapara.typography.caption, color = c.text2)
                        Text(uiText(R.string.space_day_163), style = Zapara.typography.bodyStrong)
                        ZButton(uiText(R.string.space_day_164), {}, enabled = false)
                    }
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.theme_animations), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                        ZSwitch(state.animations, { onEvent(SettingsEvent.Animations(it)) }, "Settings.Animations")
                    }
                }
            }
            }
            if (section == "maps") {
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
            }
            if (section == "notifications") {
            item {
                ZCard(Modifier.fillMaxWidth()) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.settings_notify), style = Zapara.typography.section, color = c.text1, modifier = Modifier.weight(1f))
                        ZSwitch(state.notifyEnabled, { onEvent(SettingsEvent.Notify(it)) }, "Settings.Notify")
                    }
                    TimeField(state.time1, stringResource(R.string.settings_time_evening), "Settings.Time1", state.notifyEnabled) { onEvent(SettingsEvent.Time1(it)) }
                    TimeField(state.time2, stringResource(R.string.settings_time_morning), "Settings.Time2", state.notifyEnabled) { onEvent(SettingsEvent.Time2(it)) }
                    Text(if (state.notifyEnabled) uiText(R.string.space_day_165, (state.time1).toString(), (state.time2).toString()) else uiText(R.string.space_day_166), style = Zapara.typography.caption, color = c.text2)
                    Text(uiText(R.string.space_day_notification_preview), style = Zapara.typography.section)
                    NotificationPreview(uiText(R.string.space_day_preview_tomorrow), state.time1, state.previewEvening)
                    NotificationPreview(uiText(R.string.space_day_preview_today), state.time2, state.previewMorning)
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
            }
            if (section == "updates") {
            item {
                if (state.selfUpdate) UpdatesCard(state, updates, onEvent)
                else RustoreUpdatesCard()
            }
            }
            if (section == "help") {
            item { AboutCard(state, onEvent) }
            }
        }
        }
        }
    }
}

@Composable
private fun TimeField(value: String, label: String, tag: String, enabled: Boolean = true, onChange: (String) -> Unit) {
    ZTextField(enabled = enabled,
        value = value, onValueChange = onChange,
        modifier = Modifier.fillMaxWidth().testTag(tag),
        label = { Text(label, style = Zapara.typography.caption) },
        singleLine = true
    )
}

@Composable
private fun NotificationPreview(title: String, time: String, text: String) {
    val c = Zapara.colors
    Column(Modifier.fillMaxWidth().background(c.canvas, RoundedCornerShape(Zapara.radii.control)).padding(Zapara.space.m),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Icon(painterResource(R.drawable.ic_notification), contentDescription = null, tint = c.text1, modifier = Modifier.size(20.dp))
            Text(title, modifier = Modifier.weight(1f), style = Zapara.typography.bodyStrong, color = c.text1)
            Text(time, style = Zapara.typography.caption, color = c.text2)
        }
        Text(text, style = Zapara.typography.caption, color = c.text2)
    }
}

@Composable
private fun RustoreUpdatesCard() {
    val uiText = rememberUiText()
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
    val uiText = rememberUiText()
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
fun AboutCard(state: SettingsUiState, onEvent: (SettingsEvent) -> Unit) {
    val uiText = rememberUiText()
    val ctx = LocalContext.current
    val c = Zapara.colors
    var open by remember { mutableStateOf(false) }
    var subject by remember { mutableStateOf("") }
    var body by remember { mutableStateOf("") }
    var photos by remember { mutableStateOf(listOf<Pair<String, ByteArray>>()) }
    var logs by remember { mutableStateOf(listOf<Pair<String, ByteArray>>()) }
    var fileNote by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()
    fun take(kind: String, uri: Uri?) {
        if (uri == null) return
        scope.launch {
            val read = withContext(Dispatchers.IO) { readSupportFile(ctx, uri, if (kind == "photo") 4 * 1024 * 1024 else 512 * 1024) }
            if (read == null) {
                fileNote = ctx.getString(if (kind == "photo") R.string.face_photo_too_big else R.string.face_log_too_big)
                return@launch
            }
            val name = read.first
            val ext = name.substringAfterLast('.', "").lowercase()
            if (kind == "photo" && ext !in setOf("jpg", "jpeg", "png", "webp")) {
                fileNote = ctx.getString(R.string.face_photo_type)
                return@launch
            }
            if (kind == "log" && ext !in setOf("txt", "log")) {
                fileNote = ctx.getString(R.string.face_log_type)
                return@launch
            }
            if (kind == "photo" && photos.size >= 3) { fileNote = ctx.getString(R.string.face_photo_limit); return@launch }
            if (kind == "log" && logs.size >= 3) { fileNote = ctx.getString(R.string.face_log_limit); return@launch }
            fileNote = ""
            if (kind == "photo") photos = photos + read else logs = logs + read
        }
    }
    val photo = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { take("photo", it) }
    val log = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { take("log", it) }
    ZCard(Modifier.fillMaxWidth().testTag("Settings.About")) {
        Text(stringResource(R.string.settings_about_title), style = Zapara.typography.section, color = c.text1)
        Text(stringResource(R.string.settings_version, state.version), style = Zapara.typography.caption, color = c.text2)
        Text(stringResource(R.string.settings_unofficial), style = Zapara.typography.body, color = c.text2)
        ZButton(stringResource(R.string.settings_releases), {
            ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(AutoUpdate.RELEASES_PAGE)))
        }, ghost = true, tag = "Settings.Releases")
        ZButton(stringResource(R.string.settings_report), { open = !open }, ghost = true, tag = "Settings.Report")
        Text(stringResource(R.string.settings_report_hint), style = Zapara.typography.caption, color = c.text2)
        if (open) {
            OutlinedTextField(subject, { subject = it.take(120) }, modifier = Modifier.fillMaxWidth(), label = { Text(stringResource(R.string.face_subject)) }, singleLine = true)
            OutlinedTextField(body, { body = it.take(4000) }, modifier = Modifier.fillMaxWidth(), label = { Text(stringResource(R.string.face_what_happened)) })
            Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.face_photo), { photo.launch(arrayOf("image/*")) }, ghost = true, tag = "Settings.ReportPhoto")
                ZButton(stringResource(R.string.face_log_button), { log.launch(arrayOf("*/*")) }, ghost = true, tag = "Settings.ReportLog")
            }
            Text(stringResource(R.string.face_attach_limits), style = Zapara.typography.caption, color = c.text2)
            (photos.map { stringResource(R.string.face_photo_named, it.first) } + logs.map { stringResource(R.string.face_log_named, it.first) }).forEach { Text(it, style = Zapara.typography.body, color = c.text1) }
            if (fileNote.isNotBlank()) Text(fileNote, style = Zapara.typography.body, color = c.text1)
            ZButton(stringResource(R.string.face_send), {
                onEvent(SettingsEvent.Report(subject, body, photos, logs))
                if (state.signedIn && subject.trim().length >= 3 && body.trim().length >= 3) {
                    subject = ""; body = ""; photos = emptyList(); logs = emptyList(); fileNote = ""
                }
            }, tag = "Settings.ReportSend")
            if (state.reportNote.isNotBlank()) Text(state.reportNote, style = Zapara.typography.body, color = c.text1)
            state.reportThread.forEach { line ->
                Text((if (line.author == "operator") stringResource(R.string.face_support_line) else stringResource(R.string.face_you_line)) + line.body, style = Zapara.typography.body, color = c.text1)
            }
        }
        Text(stringResource(R.string.settings_licenses), style = Zapara.typography.caption, color = c.text2)
    }
}

private fun readSupportFile(context: android.content.Context, uri: Uri, max: Int): Pair<String, ByteArray>? {
    val name = context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
        if (cursor.moveToFirst()) cursor.getString(0) else null
    }?.substringAfterLast('/')?.substringAfterLast('\\') ?: context.getString(R.string.face_file)
    val bytes = context.contentResolver.openInputStream(uri)?.use { input ->
        val out = ByteArrayOutputStream()
        val buf = ByteArray(8192)
        var total = 0
        while (true) {
            val n = input.read(buf)
            if (n < 0) break
            total += n
            if (total > max) return null
            out.write(buf, 0, n)
        }
        out.toByteArray()
    } ?: return null
    if (bytes.isEmpty()) return null
    return name to bytes
}
