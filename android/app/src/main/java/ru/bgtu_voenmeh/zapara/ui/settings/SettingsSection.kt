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
private fun SettingsOverviewRow(
    @DrawableRes icon: Int,
    title: String,
    summary: String,
    tag: String,
    emphasized: Boolean = false,
    onClick: () -> Unit
) {
    val c = Zapara.colors
    Column {
        Surface(
            onClick = onClick,
            modifier = Modifier.fillMaxWidth().testTag(tag),
            shape = RoundedCornerShape(if (emphasized) Zapara.radii.card else 0.dp),
            color = if (emphasized) c.card else c.canvas,
            border = if (emphasized) BorderStroke(Zapara.space.hairline, c.line) else null
        ) {
            Row(
                Modifier.fillMaxWidth().heightIn(min = 64.dp).padding(horizontal = Zapara.space.s, vertical = Zapara.space.s),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)
            ) {
                Box(Modifier.size(36.dp).background(c.chip, RoundedCornerShape(Zapara.radii.control)),
                    contentAlignment = Alignment.Center) {
                    Icon(painterResource(icon), contentDescription = null, tint = c.text1, modifier = Modifier.size(20.dp))
                }
                Column(Modifier.weight(1f)) {
                    Text(title, style = Zapara.typography.bodyStrong, color = c.text1)
                    Text(summary, style = Zapara.typography.caption, color = c.text2)
                }
                Icon(painterResource(R.drawable.ic_chevron_right), contentDescription = null,
                    tint = c.text2, modifier = Modifier.size(20.dp))
            }
        }
        if (!emphasized) HorizontalDivider(color = c.line, thickness = Zapara.space.hairline)
    }
}

@Composable
private fun SettingsOverview(state: SettingsUiState, account: AccountUiState, onOpen: (String) -> Unit) {
    val c = Zapara.colors
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        if (state.syncConflicts.isNotEmpty() || state.syncError != null) item {
            SettingsOverviewRow(R.drawable.ic_refresh, stringResource(R.string.sync_conflict_title),
                state.syncError ?: stringResource(R.string.sync_conflict_body), "Settings.Overview.Sync", emphasized = true) {
                onOpen("account")
            }
        }
        item {
            SettingsOverviewRow(R.drawable.ic_users, stringResource(R.string.account_title),
                if (account.guest) stringResource(R.string.settings_overview_guest)
                else account.accountName.ifBlank { stringResource(R.string.account_title) },
                "Settings.Overview.Account", emphasized = true) { onOpen("account") }
        }
        item { Text(stringResource(R.string.settings_overview_core), style = Zapara.typography.section,
            color = c.text1, modifier = Modifier.padding(top = Zapara.space.l, bottom = Zapara.space.xs)) }
        item { SettingsOverviewRow(R.drawable.ic_calendar, stringResource(R.string.settings_overview_study),
            state.groupName.ifBlank { stringResource(R.string.group_pick) }, "Settings.Overview.Study") { onOpen("study") } }
        item { SettingsOverviewRow(R.drawable.ic_sun, stringResource(R.string.theme_appearance),
            listOf(stringResource(R.string.theme_system), stringResource(R.string.theme_light), stringResource(R.string.theme_dark))[state.theme.ordinal],
            "Settings.Overview.Appearance") { onOpen("appearance") } }
        item { SettingsOverviewRow(R.drawable.ic_notification, stringResource(R.string.settings_notify),
            if (state.notifyEnabled) stringResource(R.string.settings_overview_notify_on, state.time1, state.time2)
            else stringResource(R.string.settings_overview_notify_off),
            "Settings.Overview.Notifications") { onOpen("notifications") } }
        item { SettingsOverviewRow(R.drawable.ic_map, stringResource(R.string.settings_maps),
            stringResource(R.string.settings_maps_routes), "Settings.Overview.Maps") { onOpen("maps") } }
        item { Text(stringResource(R.string.settings_overview_service), style = Zapara.typography.section,
            color = c.text1, modifier = Modifier.padding(top = Zapara.space.l, bottom = Zapara.space.xs)) }
        item { SettingsOverviewRow(R.drawable.ic_refresh, stringResource(R.string.settings_updates),
            stringResource(R.string.settings_version, state.version), "Settings.Overview.Updates") { onOpen("updates") } }
        item { SettingsOverviewRow(R.drawable.ic_file, stringResource(R.string.settings_overview_help),
            stringResource(R.string.settings_overview_help_summary), "Settings.Overview.Help") { onOpen("help") } }
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
    var section by rememberSaveable { mutableStateOf<String?>(null) }
    BackHandler(enabled = section != null && legalId == null) { section = null }
    if (legalId != null) {
        LegalDocumentPage(legalId!!, onClose = { legalId = null })
        return
    }
    Column(Modifier.fillMaxSize()) {
        ZTopBar(when (section) {
            "account" -> stringResource(R.string.account_title)
            "study" -> stringResource(R.string.settings_overview_study)
            "appearance" -> stringResource(R.string.theme_appearance)
            "maps" -> stringResource(R.string.settings_maps)
            "notifications" -> stringResource(R.string.settings_notify)
            "updates" -> stringResource(R.string.settings_updates)
            "help" -> stringResource(R.string.settings_overview_help)
            else -> stringResource(R.string.nav_settings)
        })
        if (section == null) SettingsOverview(state, account) { section = it }
        else LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            item { ZButton(stringResource(R.string.settings_overview_all), { section = null }, ghost = true, tag = "Settings.Overview.Back") }
            if (section == "account") {
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
            }
            if (section == "study") {
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
            }
            if (section == "appearance") {
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
fun AboutCard(state: SettingsUiState, onEvent: (SettingsEvent) -> Unit) {
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
                fileNote = if (kind == "photo") "Фото больше 4 МиБ." else "Лог больше 512 КиБ."
                return@launch
            }
            val name = read.first
            val ext = name.substringAfterLast('.', "").lowercase()
            if (kind == "photo" && ext !in setOf("jpg", "jpeg", "png", "webp")) {
                fileNote = "Нужна фотография JPEG, PNG или WebP."
                return@launch
            }
            if (kind == "log" && ext !in setOf("txt", "log")) {
                fileNote = "Лог должен быть текстовым файлом .txt или .log."
                return@launch
            }
            if (kind == "photo" && photos.size >= 3) { fileNote = "Можно приложить не больше трёх фотографий."; return@launch }
            if (kind == "log" && logs.size >= 3) { fileNote = "Можно приложить не больше трёх логов."; return@launch }
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
            OutlinedTextField(subject, { subject = it.take(120) }, modifier = Modifier.fillMaxWidth(), label = { Text("Тема") }, singleLine = true)
            OutlinedTextField(body, { body = it.take(4000) }, modifier = Modifier.fillMaxWidth(), label = { Text("Что случилось") })
            Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton("Фото", { photo.launch(arrayOf("image/*")) }, ghost = true, tag = "Settings.ReportPhoto")
                ZButton("Лог", { log.launch(arrayOf("*/*")) }, ghost = true, tag = "Settings.ReportLog")
            }
            Text("До трёх фотографий JPEG, PNG или WebP, до 4 МиБ. До трёх логов .txt или .log, до 512 КиБ.", style = Zapara.typography.caption, color = c.text2)
            (photos.map { "Фото: ${it.first}" } + logs.map { "Лог: ${it.first}" }).forEach { Text(it, style = Zapara.typography.body, color = c.text1) }
            if (fileNote.isNotBlank()) Text(fileNote, style = Zapara.typography.body, color = c.text1)
            ZButton("Отправить", {
                onEvent(SettingsEvent.Report(subject, body, photos, logs))
                if (state.signedIn && subject.trim().length >= 3 && body.trim().length >= 3) {
                    subject = ""; body = ""; photos = emptyList(); logs = emptyList(); fileNote = ""
                }
            }, tag = "Settings.ReportSend")
            if (state.reportNote.isNotBlank()) Text(state.reportNote, style = Zapara.typography.body, color = c.text1)
            state.reportThread.forEach { line ->
                Text((if (line.author == "operator") "Поддержка. " else "Вы. ") + line.body, style = Zapara.typography.body, color = c.text1)
            }
        }
        Text(stringResource(R.string.settings_licenses), style = Zapara.typography.caption, color = c.text2)
    }
}

private fun readSupportFile(context: android.content.Context, uri: Uri, max: Int): Pair<String, ByteArray>? {
    val name = context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
        if (cursor.moveToFirst()) cursor.getString(0) else null
    }?.substringAfterLast('/')?.substringAfterLast('\\') ?: "Файл"
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
