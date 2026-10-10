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
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
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
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.Surface
import androidx.compose.material3.AlertDialog
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.listSaver
import androidx.compose.ui.Alignment
import ru.bgtu_voenmeh.zapara.ui.components.rememberUiText
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.constrainHeight
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.AutoUpdate
import ru.bgtu_voenmeh.zapara.data.sync.PrivateSyncState
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
import ru.bgtu_voenmeh.zapara.ui.theme.ZActionButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZDisclosureButton
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
private fun SettingsOverview(state: SettingsUiState, account: AccountUiState,
    query: String, onQuery: (String) -> Unit, onOpen: (String) -> Unit) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    val visible = SettingsCategorySearch.visible(query, linkedMapOf(
        "account" to stringResource(R.string.uxnext_settings_terms_account),
        "study" to stringResource(R.string.uxnext_settings_terms_study),
        "appearance" to stringResource(R.string.uxnext_settings_terms_appearance),
        "notifications" to stringResource(R.string.uxnext_settings_terms_notifications),
        "maps" to stringResource(R.string.uxnext_settings_terms_maps),
        "data" to stringResource(R.string.uxnext_settings_terms_data),
        "updates" to stringResource(R.string.uxnext_settings_terms_updates),
        "help" to stringResource(R.string.uxnext_settings_terms_help)
    ))
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        item("search") { ZTextField(query, onQuery, modifier = Modifier.fillMaxWidth().testTag("Settings.Search"),
            placeholder = { Text(stringResource(R.string.uxnext_settings_search)) }, singleLine = true) }
        if (query.isNotBlank()) item("search-clear") {
            ZButton(stringResource(R.string.next_teachers_clear), { onQuery("") }, ghost = true,
                tag = "Settings.SearchClear")
        }
        if (visible.isEmpty()) item("search-empty") {
            ZCard(Modifier.fillMaxWidth(), tag = "Settings.SearchEmpty") {
                Text(stringResource(R.string.uxnext_settings_no_result), style = Zapara.typography.body)
            }
        }
        if ("account" in visible) item {
            SettingsOverviewRow(R.drawable.ic_user, stringResource(R.string.account_title),
                if (account.guest) stringResource(R.string.settings_overview_guest)
                else account.accountName.ifBlank { stringResource(R.string.account_title) },
                "Settings.Overview.Account", emphasized = true) { onOpen("account") }
        }
        if ("data" in visible && (state.syncConflicts.isNotEmpty() || state.syncError != null)) item {
            SettingsOverviewRow(R.drawable.ic_refresh, stringResource(R.string.sync_conflict_title),
                state.syncError ?: stringResource(R.string.sync_conflict_body), "Settings.Overview.Sync", emphasized = true) {
                onOpen("data")
            }
        }
        if (visible.any { it in setOf("study", "appearance", "notifications", "maps") }) item {
            Text(stringResource(R.string.settings_overview_core), style = Zapara.typography.section,
            color = c.text1, modifier = Modifier.padding(top = Zapara.space.l, bottom = Zapara.space.xs)) }
        if (visible.any { it in setOf("study", "appearance", "notifications", "maps") }) item("core") { ZCard(Modifier.fillMaxWidth(), padded = false) {
            Column {
                if ("study" in visible) SettingsOverviewRow(R.drawable.ic_calendar, stringResource(R.string.settings_overview_study),
                    state.groupName.ifBlank { stringResource(R.string.group_pick) }, "Settings.Overview.Study") { onOpen("study") }
                if ("appearance" in visible) SettingsOverviewRow(R.drawable.ic_sun, stringResource(R.string.theme_appearance),
                    listOf(stringResource(R.string.theme_system), stringResource(R.string.theme_light), stringResource(R.string.theme_dark))[state.theme.ordinal],
                    "Settings.Overview.Appearance") { onOpen("appearance") }
                if ("notifications" in visible) SettingsOverviewRow(R.drawable.ic_bell, stringResource(R.string.settings_notify),
                    if (state.notifyEnabled) stringResource(R.string.settings_overview_notify_on,
                        state.savedTime1, state.savedTime2)
                    else stringResource(R.string.settings_overview_notify_off),
                    "Settings.Overview.Notifications") { onOpen("notifications") }
                if ("maps" in visible) SettingsOverviewRow(R.drawable.ic_map, stringResource(R.string.settings_maps),
                    stringResource(R.string.settings_maps_routes), "Settings.Overview.Maps", divider = false) { onOpen("maps") }
            }
        } }
        if (visible.any { it in setOf("data", "updates", "help") }) item {
            Text(stringResource(R.string.settings_overview_service), style = Zapara.typography.section,
            color = c.text1, modifier = Modifier.padding(top = Zapara.space.l, bottom = Zapara.space.xs)) }
        if (visible.any { it in setOf("data", "updates", "help") }) item("service") { ZCard(Modifier.fillMaxWidth(), padded = false) {
            Column {
                if ("data" in visible) SettingsOverviewRow(R.drawable.ic_refresh, uiText(R.string.space_day_153),
                    if (state.syncBusy) uiText(R.string.space_day_154) else if (state.signedIn) uiText(R.string.space_day_155) else uiText(R.string.space_day_156), "Settings.Overview.Data") { onOpen("data") }
                if ("updates" in visible) SettingsOverviewRow(R.drawable.ic_download, stringResource(R.string.settings_updates),
                    stringResource(R.string.settings_version, state.version), "Settings.Overview.Updates") { onOpen("updates") }
                if ("help" in visible) SettingsOverviewRow(R.drawable.ic_file, stringResource(R.string.settings_overview_help),
                    stringResource(R.string.settings_overview_help_summary), "Settings.Overview.Help", divider = false) { onOpen("help") }
            }
        } }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SettingsSection(
    state: SettingsUiState,
    onEvent: (SettingsEvent) -> Unit,
    updates: UpdateUiState,
    onChangeGroup: () -> Unit,
    account: AccountUiState = AccountUiState(),
    onAccount: (AccountEvent) -> Unit = {},
    initialSection: String? = null
) {
    val uiText = rememberUiText()
    val ctx = LocalContext.current
    val c = Zapara.colors
    var legalId by rememberSaveable { mutableStateOf<String?>(null) }
    var section by rememberSaveable(initialSection) { mutableStateOf(initialSection) }
    var returnSection by rememberSaveable { mutableStateOf<String?>(null) }
    var settingsQuery by rememberSaveable { mutableStateOf("") }
    var pendingSection by rememberSaveable { mutableStateOf<String?>(null) }
    var pendingProfileSection by rememberSaveable { mutableStateOf<String?>(null) }
    var conflictFilter by rememberSaveable { mutableStateOf<String?>(null) }
    var supportDrafts by rememberSaveable(state.profileName, stateSaver = SupportDraftsSaver) {
        mutableStateOf<Map<String, SupportLocalDraft>>(emptyMap())
    }
    LaunchedEffect(state.reportSuccessVersion, state.reportSuccessKey, state.reportSuccessDraftRevision) {
        val key = state.reportSuccessKey
        val revision = state.reportSuccessDraftRevision
        if (state.reportSuccessVersion > 0 && key != null && revision != null &&
            supportDrafts[key]?.revision == revision) supportDrafts = supportDrafts - key
    }
    fun backToSettings() {
        if (!SettingsLogic.canLeaveSection(section, state.timeSaving)) return
        if (section == "account" && !account.guest && account.displayName.trim() != account.profileNameBaseline.trim()) {
            pendingProfileSection = returnSection ?: ""
            return
        }
        if (section == "account" && returnSection != null) {
            section = returnSection; returnSection = null
        } else if (section == "notifications" && state.timeDirty) {
            pendingSection = ""
        } else { section = null; returnSection = null }
    }
    fun openSection(target: String) {
        if (!SettingsLogic.canLeaveSection(section, state.timeSaving)) return
        if (section == "account" && target != "account" && !account.guest &&
            account.displayName.trim() != account.profileNameBaseline.trim()) {
            pendingProfileSection = target
            return
        }
        if (section == "notifications" && state.timeDirty && target != "notifications")
            pendingSection = target
        else { returnSection = null; section = target }
    }
    BackHandler(enabled = section != null && legalId == null) { backToSettings() }
    val accountForm = remember { ru.bgtu_voenmeh.zapara.ui.account.AccountFormMemory() } // #109: переживает открытие документа
    if (legalId != null) {
        LegalDocumentPage(legalId!!, onClose = { legalId = null })
        return
    }
    LaunchedEffect(state.timeSaving) { if (state.timeSaving) pendingSection = null }
    if (pendingSection != null && !state.timeSaving) AlertDialog(
        onDismissRequest = { pendingSection = null },
        title = { Text(stringResource(R.string.ux30_platform_unsaved_title)) },
        text = { Text(stringResource(R.string.ux30_platform_unsaved_body)) },
        confirmButton = { Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.ux30_platform_discard), {
                val destination = pendingSection
                pendingSection = null
                onEvent(SettingsEvent.CancelTimes)
                section = destination?.ifEmpty { null }
                returnSection = null
            }, modifier = Modifier.fillMaxWidth(), destructive = true,
                leadingIcon = R.drawable.ic_trash, tag = "Settings.DiscardTimes")
            ZButton(stringResource(R.string.account_cancel), { pendingSection = null },
                modifier = Modifier.fillMaxWidth(), ghost = true, tag = "Settings.KeepTimes")
        } }
    )
    if (pendingProfileSection != null) AlertDialog(
        onDismissRequest = { pendingProfileSection = null },
        title = { Text(stringResource(R.string.ux100_platform_profile_unsaved_title)) },
        text = { Text(stringResource(R.string.ux100_platform_profile_unsaved_body)) },
        confirmButton = {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.ux30_platform_discard), {
                    val destination = pendingProfileSection
                    pendingProfileSection = null
                    onAccount(AccountEvent.CancelProfile)
                    section = destination?.ifEmpty { null }
                    returnSection = null
                }, modifier = Modifier.fillMaxWidth(), destructive = true,
                    leadingIcon = R.drawable.ic_trash, tag = "Settings.DiscardProfile")
                ZButton(stringResource(R.string.account_cancel), { pendingProfileSection = null },
                    modifier = Modifier.fillMaxWidth(), ghost = true, tag = "Settings.KeepProfile")
            }
        }
    )
    Column(Modifier.fillMaxSize()) {
        SettingsStudyExtras(state, onEvent)
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
        if (section == null) SettingsOverview(state, account, settingsQuery,
            { settingsQuery = it }) { openSection(it) }
        else Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.l)) {
            if (wideSettings) Box(Modifier.width(320.dp)) { SettingsOverview(state, account,
                settingsQuery, { settingsQuery = it }) { openSection(it) } }
            LazyColumn(if (wideSettings) Modifier.widthIn(max = 720.dp).fillMaxSize() else Modifier.weight(1f).fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            item { ZActionButton(stringResource(if (section == "account" && returnSection == "data")
                R.string.ux60_sync_back_to_data else R.string.settings_overview_all),
                { backToSettings() }, tag = "Settings.Overview.Back",
                leadingIcon = R.drawable.ic_chevron_left, trailingIcon = null) }
            if (section == "account") {
            item { AccountCard(account, onAccount, onOpenLegal = { id ->
                // #109: из формы регистрации документ открывается без очистки пароля — ввод не теряется.
                if (!LegalReturn.keepsRegistration(account.guest, account.registration)) onAccount(AccountEvent.ClearSensitive)
                legalId = id
            }, memory = accountForm) }
            if (state.signedIn) item { ZCard(Modifier.fillMaxWidth()) {
                CloudSyncSummary(state.cloudSync)
                ZButton(uiText(R.string.space_day_sync_now), { onEvent(SettingsEvent.SyncNow) },
                    enabled = !state.syncBusy && state.cloudSync.attached, busy = state.syncBusy)
            } }
            }
            if (section == "data") {
            if (state.signedIn) item("pending-sync") {
                var expanded by rememberSaveable(state.profileName) { mutableStateOf(false) }
                ZCard(Modifier.fillMaxWidth(), tag = "Settings.PendingSync") {
                    ZDisclosureButton(stringResource(R.string.ux300_ext_pending_sync, state.pendingSync.size),
                        expanded = expanded, onClick = { expanded = !expanded }, tag = "Settings.PendingSyncOpen",
                        leadingIcon = R.drawable.ic_refresh)
                    if (expanded) {
                        Text(stringResource(R.string.ux300_ext_pending_sync_hint), style = Zapara.typography.caption)
                        state.pendingSync.take(50).forEach { pending ->
                            Text(conflictTitle(pending.type), style = Zapara.typography.bodyStrong)
                            Text(if (pending.deleted) stringResource(R.string.ux300_ext_pending_delete)
                                else syncValueDescription(pending.value), style = Zapara.typography.body)
                            if (pending.conflict) Text(stringResource(R.string.sync_conflict_title), style = Zapara.typography.caption)
                        }
                        if (state.pendingSync.size > 50) Text(stringResource(R.string.ux300_ext_pending_more,
                            state.pendingSync.size - 50), style = Zapara.typography.caption)
                        ZButton(stringResource(R.string.ux300_ext_pending_refresh),
                            { onEvent(SettingsEvent.RefreshPendingSync) }, ghost = true, enabled = !state.syncBusy)
                    }
                }
            }
            item { ZCard(Modifier.fillMaxWidth()) {
                if (state.signedIn) CloudSyncSummary(state.cloudSync)
                else Text(uiText(R.string.space_day_159), style = Zapara.typography.body)
                if (!state.signedIn) ZActionButton(stringResource(R.string.ux100_platform_open_account),
                    { returnSection = "data"; section = "account" }, tag = "Settings.DataOpenAccount",
                    leadingIcon = R.drawable.ic_user)
                Text(uiText(R.string.space_day_160), style = Zapara.typography.caption)
                Text(state.groupUpdated, style = Zapara.typography.caption)
                if (state.signedIn) {
                    Text(stringResource(R.string.cloud_sync_scope), style = Zapara.typography.caption, color = c.text2)
                    ZButton(uiText(R.string.space_day_sync_now), { onEvent(SettingsEvent.SyncNow) },
                        enabled = !state.syncBusy && state.cloudSync.attached, busy = state.syncBusy)
                    if (state.cloudSync.failure == PrivateSyncState.NeedsReauthentication)
                        ZActionButton(stringResource(R.string.ux60_sync_open_account),
                            { returnSection = "data"; section = "account" },
                            tag = "Settings.SyncReauthenticate", leadingIcon = R.drawable.ic_user)
                }
            } }
            if (state.syncConflicts.isNotEmpty() || state.syncError != null) item {
                ZCard(Modifier.fillMaxWidth().testTag("Sync.Conflicts")) {
                    Text(stringResource(R.string.sync_conflict_title), style = Zapara.typography.section, color = c.text1)
                    Text(stringResource(R.string.sync_conflict_body), style = Zapara.typography.body, color = c.text2)
                    state.syncError?.let { Text(it, style = Zapara.typography.body, color = c.bad) }
                    if (state.syncError != null && state.signedIn) ZButton(
                        stringResource(R.string.ux100_platform_retry_sync), { onEvent(SettingsEvent.SyncNow) },
                        enabled = !state.syncBusy && state.cloudSync.attached, ghost = true,
                        tag = "Settings.RetrySync")
                }
            }
            if (state.syncConflicts.map { it.operation.entityType }.distinct().size > 1) item {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    ZChip(stringResource(R.string.ux100_platform_all_conflicts), selected = conflictFilter == null,
                        onClick = { conflictFilter = null }, tag = "Settings.ConflictAll")
                    state.syncConflicts.map { it.operation.entityType }.distinct().forEach { type ->
                        ZChip(conflictTitle(type), selected = conflictFilter == type,
                            onClick = { conflictFilter = type }, tag = "Settings.ConflictType.$type")
                    }
                }
            }
            if (conflictFilter != null && state.syncConflicts.none { it.operation.entityType == conflictFilter }) item {
                ZButton(stringResource(R.string.ux100_platform_all_conflicts), { conflictFilter = null },
                    ghost = true, tag = "Settings.ConflictFilterClear")
            }
            if (state.signedIn && state.syncConflicts.isEmpty() && state.syncError == null) item {
                Text(stringResource(R.string.ux100_platform_no_sync_conflicts),
                    style = Zapara.typography.caption, color = c.text2,
                    modifier = Modifier.testTag("Settings.NoSyncConflicts"))
            }
            items(state.syncConflicts.filter { conflictFilter == null || it.operation.entityType == conflictFilter },
                key = { "conflict:${it.operation.opId}" }) { conflict ->
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
                    var advanced by rememberSaveable { mutableStateOf(false) }
                    ZDisclosureButton(uiText(R.string.space_day_advanced), expanded = advanced,
                        onClick = { advanced = !advanced }, leadingIcon = R.drawable.ic_settings)
                    if (advanced) Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(uiText(R.string.space_day_invert), Modifier.weight(1f), style = Zapara.typography.body)
                        ZSwitch(state.parityInvert, { if ("parity" !in state.preferencePending) onEvent(SettingsEvent.Invert(it)) }, "Settings.ParityInvert")
                    }
                    if (advanced) PreferenceFeedback(state, "parity", onEvent)
                    GroupActions(state.refreshing, onChangeGroup) { onEvent(SettingsEvent.Refresh) }
                }
            }
            items(state.subgroupStreams, key = { it.id }) { stream -> ZCard(Modifier.fillMaxWidth()) {
                Text(stream.title, style = Zapara.typography.section)
                Text(uiText(R.string.space_day_subgroup_hint), style = Zapara.typography.caption)
                stream.options.forEach { option ->
                    Row(Modifier.fillMaxWidth().heightIn(min = Zapara.space.minTouch)
                        .selectable(selected = state.subgroupChoices[stream.id] == option.id,
                            role = androidx.compose.ui.semantics.Role.RadioButton,
                            onClick = { onEvent(SettingsEvent.PreviewSubgroup(SettingsEvent.Subgroup(stream.id, option.id, state.groupId, state.profileName))) }),
                        verticalAlignment = Alignment.CenterVertically) {
                        androidx.compose.material3.RadioButton(state.subgroupChoices[stream.id] == option.id,
                            onClick = null)
                        Text(option.label, style = Zapara.typography.body)
                    }
                }
                if (state.undoSubgroup?.streamId == stream.id) ZButton(
                    stringResource(R.string.uxnext_subgroup_undo),
                    { onEvent(SettingsEvent.UndoSubgroup) }, ghost = true,
                    tag = "Settings.SubgroupUndo")
            } }
            if (state.apiConfigured) item {
                ZCard(Modifier.fillMaxWidth().testTag("Settings.Source")) {
                    Text(stringResource(R.string.settings_source), style = Zapara.typography.section, color = c.text1)
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.settings_source_university), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                        ZSwitch(state.useUniversityXml, { if ("source" !in state.preferencePending) onEvent(SettingsEvent.UseUniversityXml(it)) }, "Settings.UniversityXml")
                    }
                    Text(stringResource(R.string.settings_source_hint), style = Zapara.typography.caption, color = c.text2)
                    PreferenceFeedback(state, "source", onEvent)
                }
            }
            }
            if (section == "appearance") {
            item {
                ZCard(Modifier.fillMaxWidth()) {
                    Text(stringResource(R.string.theme_appearance), style = Zapara.typography.section, color = c.text1)
                    val themes = listOf(stringResource(R.string.theme_system),
                        stringResource(R.string.theme_light), stringResource(R.string.theme_dark))
                    if (LocalDensity.current.fontScale >= 1.5f) {
                        FlowRow(Modifier.fillMaxWidth().testTag("Settings.Theme"),
                            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                            themes.forEachIndexed { index, label ->
                                ZChip(label, selected = state.theme.ordinal == index,
                                    onClick = { if ("theme" !in state.preferencePending) onEvent(SettingsEvent.Theme(index)) },
                                    tag = "Settings.Theme.$index")
                            }
                        }
                    } else {
                        ZSegmented(themes, state.theme.ordinal, { if ("theme" !in state.preferencePending) onEvent(SettingsEvent.Theme(it)) },
                            "Settings.Theme")
                    }
                    PreferenceFeedback(state, "theme", onEvent)
                    ZCard(Modifier.fillMaxWidth()) {
                        Text(uiText(R.string.space_day_161), style = Zapara.typography.section)
                        Text(uiText(R.string.space_day_162), style = Zapara.typography.caption, color = c.text2)
                        Text(uiText(R.string.space_day_163), style = Zapara.typography.bodyStrong)
                        ZButton(uiText(R.string.space_day_164), {}, enabled = false)
                    }
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.theme_animations), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                        ZSwitch(state.animations, { if ("animations" !in state.preferencePending) onEvent(SettingsEvent.Animations(it)) }, "Settings.Animations")
                    }
                    PreferenceFeedback(state, "animations", onEvent)
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
                        ZSwitch(state.mapsAlpha, { if ("maps" !in state.preferencePending) onEvent(SettingsEvent.MapsAlpha(it)) }, "Settings.MapsAlpha")
                    }
                    PreferenceFeedback(state, "maps", onEvent)
                    Text(stringResource(R.string.settings_maps_alpha_hint), style = Zapara.typography.caption, color = c.text2)
                }
            }
            }
            if (section == "notifications") {
            item {
                ZCard(Modifier.fillMaxWidth()) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.settings_notify), style = Zapara.typography.section, color = c.text1, modifier = Modifier.weight(1f))
                        ZSwitch(state.notifyEnabled, { if ("notify" !in state.preferencePending) onEvent(SettingsEvent.Notify(it)) }, "Settings.Notify")
                    }
                    PreferenceFeedback(state, "notify", onEvent)
                    TimeField(state.time1, stringResource(R.string.settings_time_evening), "Settings.Time1", !state.timeSaving, state.timeError != null) { onEvent(SettingsEvent.Time1(it)) }
                    TimeField(state.time2, stringResource(R.string.settings_time_morning), "Settings.Time2", !state.timeSaving, state.timeError != null) { onEvent(SettingsEvent.Time2(it)) }
                    state.timeError?.let { Text(it, style = Zapara.typography.caption, color = c.bad,
                        modifier = Modifier.testTag("Settings.TimeError")) }
                    if (state.timeDirty) Text(stringResource(R.string.ux30_notify_unsaved),
                        style = Zapara.typography.caption, color = c.warn)
                    state.timeSaveError?.let { Text(it, style = Zapara.typography.caption, color = c.bad) }
                    if (!state.timeSaving) {
                        Text(stringResource(R.string.ux100_platform_time_presets), style = Zapara.typography.caption,
                            color = c.text2)
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            ZButton(stringResource(R.string.ux100_platform_preset_early), {
                                onEvent(SettingsEvent.Time1("19:00")); onEvent(SettingsEvent.Time2("07:00"))
                            }, ghost = true, tag = "Settings.PresetEarly")
                            ZButton(stringResource(R.string.ux100_platform_preset_late), {
                                onEvent(SettingsEvent.Time1("21:00")); onEvent(SettingsEvent.Time2("09:00"))
                            }, ghost = true, tag = "Settings.PresetLate")
                        }
                    }
                    ZButton(stringResource(R.string.ux30_notify_save), { onEvent(SettingsEvent.SaveTimes) },
                        enabled = state.timeDirty && state.timeError == null && !state.timeSaving,
                        busy = state.timeSaving, tag = "Settings.SaveTimes")
                    if (state.timeSaving) Text(stringResource(R.string.ux30_platform_time_saving_wait),
                        style = Zapara.typography.caption, color = c.text2)
                    if (state.timeDirty && !state.timeSaving) ZButton(stringResource(R.string.ux30_notify_cancel),
                        { onEvent(SettingsEvent.CancelTimes) }, ghost = true, tag = "Settings.CancelTimes")
                    Text(if (state.notifyEnabled) uiText(R.string.space_day_165, state.savedTime1, state.savedTime2)
                        else uiText(R.string.space_day_166), style = Zapara.typography.caption, color = c.text2)
                    if (state.timeDirty) Text(stringResource(R.string.ux30_notify_preview_draft),
                        style = Zapara.typography.caption, color = c.text2)
                    Text(uiText(R.string.space_day_notification_preview), style = Zapara.typography.section)
                    NotificationPreview(uiText(R.string.space_day_preview_tomorrow), state.time1, state.previewEvening)
                    NotificationPreview(uiText(R.string.space_day_preview_today), state.time2, state.previewMorning)
                    ZButton(stringResource(R.string.settings_notify_test), { onEvent(SettingsEvent.TestNotification) }, ghost = true, tag = "Settings.NotifyTest")
                    if (state.permissionMissing) {
                        Text(stringResource(R.string.settings_perm_notify), style = Zapara.typography.caption, color = c.warn)
                        ZActionButton(stringResource(R.string.settings_open_notify), {
                            ctx.startActivity(Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, ctx.packageName))
                        }, leadingIcon = R.drawable.ic_bell)
                    } else if (state.exactAlarmMissing) {
                        Text(stringResource(R.string.settings_perm_alarm), style = Zapara.typography.caption, color = c.warn)
                        ZActionButton(stringResource(R.string.settings_open_alarm), {
                            ctx.startActivity(Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM))
                        }, leadingIcon = R.drawable.ic_bell)
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
            item("technical-diagnostics") { SupportDiagnosticsTool(state) }
            item { AboutCard(state, onEvent, supportDrafts, { supportDrafts = it },
                { legalId = it }) { returnSection = "help"; section = "account" } }
            }
        }
        }
        }
    }
}

@Composable
private fun PreferenceFeedback(state: SettingsUiState, key: String, onEvent: (SettingsEvent) -> Unit) {
    if (key in state.preferencePending) Text(stringResource(R.string.ux60_preference_saving),
        style = Zapara.typography.caption, color = Zapara.colors.text2)
    state.preferenceErrors[key]?.let { error ->
        Text(error, style = Zapara.typography.caption, color = Zapara.colors.bad)
        ZButton(stringResource(R.string.ux60_preference_retry),
            { onEvent(SettingsEvent.RetryPreference(key)) }, ghost = true,
            tag = "Settings.RetryPreference.$key")
    }
}

@Composable
private fun TimeField(value: String, label: String, tag: String, enabled: Boolean = true, invalid: Boolean = false, onChange: (String) -> Unit) {
    val context = LocalContext.current
    val isDark = Zapara.colors.isDark
    Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        ZTextField(enabled = enabled,
            value = value, onValueChange = onChange,
            modifier = Modifier.fillMaxWidth().testTag(tag),
            label = { Text(label, style = Zapara.typography.caption) },
            singleLine = true,
            isError = invalid,
            placeholder = { Text(stringResource(R.string.ux30_platform_time_format)) },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Ascii)
        )
        ZButton(stringResource(R.string.ux300_android_pick_time), {
            val parts = value.split(':').mapNotNull(String::toIntOrNull)
            val hour = parts.getOrNull(0)?.takeIf { it in 0..23 } ?: 8
            val minute = parts.getOrNull(1)?.takeIf { it in 0..59 } ?: 0
            android.app.TimePickerDialog(context, if (isDark)
                R.style.Zapara_DatePicker_Dark else R.style.Zapara_DatePicker_Light,
                { _, h, m -> onChange("%02d:%02d".format(java.util.Locale.ROOT, h, m)) },
                hour, minute, true).show()
        }, enabled = enabled, ghost = true, tag = "$tag.Picker")
    }
}

@Composable
private fun NotificationPreview(title: String, time: String, text: String) {
    val c = Zapara.colors
    Column(Modifier.fillMaxWidth().background(c.canvas, RoundedCornerShape(Zapara.radii.control)).padding(Zapara.space.m),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Icon(painterResource(R.drawable.ic_bell), contentDescription = null, tint = c.text1, modifier = Modifier.size(20.dp))
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
        ZActionButton(stringResource(R.string.settings_rustore_github), {
            ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(AutoUpdate.RELEASES_PAGE)))
        }, tag = "Settings.RustoreGithub", leadingIcon = R.drawable.ic_external_link,
            trailingIcon = null)
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun UpdatesCard(state: SettingsUiState, updates: UpdateUiState, onEvent: (SettingsEvent) -> Unit) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    val ctx = LocalContext.current
    var alpha by rememberSaveable { mutableStateOf(AutoUpdate.channel(ctx) == AutoUpdate.CHANNEL_ALPHA) }
    var token by rememberSaveable { mutableStateOf(AutoUpdate.token(ctx)) }
    ZCard(Modifier.fillMaxWidth()) {
        Text(stringResource(R.string.settings_updates), style = Zapara.typography.section, color = c.text1)
        Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.settings_upd_release), {
                alpha = false
                AutoUpdate.setChannel(ctx, AutoUpdate.CHANNEL_RELEASE)
            }, ghost = alpha, tag = "Settings.UpdRelease")
            ZButton(stringResource(R.string.settings_upd_alpha), {
                alpha = true
                AutoUpdate.setChannel(ctx, AutoUpdate.CHANNEL_ALPHA)
            }, ghost = !alpha, tag = "Settings.UpdAlpha")
        }
        if (alpha) {
            Text(stringResource(R.string.settings_upd_token_hint), style = Zapara.typography.caption, color = c.text2)
            ZTextField(
                value = token,
                onValueChange = {
                    token = it
                    AutoUpdate.setToken(ctx, it)
                },
                modifier = Modifier.fillMaxWidth().testTag("Settings.UpdToken"),
                label = { Text(stringResource(R.string.settings_upd_token), style = Zapara.typography.caption) },
                singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password)
            )
        }
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
        if (updates.updateStale) Text(stringResource(R.string.ux60_update_stale, updates.tag),
            style = Zapara.typography.caption, color = c.warn)
        if (updates.downloading) {
            LinearProgressIndicator(progress = { updates.progress.coerceAtLeast(0f) }, modifier = Modifier.fillMaxWidth(), color = c.accent, trackColor = c.chip)
        }
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.settings_upd_check), { onEvent(SettingsEvent.CheckUpdate) }, ghost = true, tag = "Settings.UpdCheck")
            if (updates.hasUpdate) ZButton(stringResource(R.string.settings_upd_download), { onEvent(SettingsEvent.DownloadUpdate) }, tag = "Settings.UpdDownload")
            if (updates.canInstall) ZButton(stringResource(R.string.ux60_update_install_tag, updates.readyTag ?: updates.tag),
                { onEvent(SettingsEvent.InstallUpdate) }, tag = "Settings.UpdInstall")
            if (updates.downloading) ZButton(stringResource(R.string.theme_cancel), { onEvent(SettingsEvent.CancelUpdate) }, ghost = true)
        }
    }
}

internal data class SupportAttachmentDraft(val uri: String, val name: String, val bytes: ByteArray? = null)

internal data class SupportLocalDraft(
    val subject: String = "",
    val body: String = "",
    val photos: List<SupportAttachmentDraft> = emptyList(),
    val logs: List<SupportAttachmentDraft> = emptyList(),
    val fileNote: String = "",
    val revision: Long = 0
)

internal object SupportDraftCodec {
    fun encode(drafts: Map<String, SupportLocalDraft>): List<String> = buildList {
        add(drafts.size.toString())
        drafts.toSortedMap().forEach { (key, draft) ->
            add(key); add(draft.subject); add(draft.body); add(draft.fileNote); add(draft.revision.toString())
            add(draft.photos.size.toString())
            draft.photos.forEach { add(it.uri); add(it.name) }
            add(draft.logs.size.toString())
            draft.logs.forEach { add(it.uri); add(it.name) }
        }
    }

    fun decode(saved: List<String>): Map<String, SupportLocalDraft> = runCatching {
        var cursor = 0
        fun next() = saved[cursor++]
        buildMap {
            repeat(next().toInt().coerceIn(0, 100)) {
                val key = next(); val subject = next(); val body = next(); val note = next(); val revision = next().toLong()
                val photos = List(next().toInt().coerceIn(0, 3)) { SupportAttachmentDraft(next(), next()) }
                val logs = List(next().toInt().coerceIn(0, 3)) { SupportAttachmentDraft(next(), next()) }
                put(key, SupportLocalDraft(subject, body, photos, logs, note, revision))
            }
        }
    }.getOrDefault(emptyMap())
}

private val SupportDraftsSaver = listSaver<Map<String, SupportLocalDraft>, String>(
    save = { SupportDraftCodec.encode(it) },
    restore = { SupportDraftCodec.decode(it) }
)

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun AboutCard(state: SettingsUiState, onEvent: (SettingsEvent) -> Unit,
    savedDrafts: Map<String, SupportLocalDraft> = emptyMap(),
    onDraftsChanged: (Map<String, SupportLocalDraft>) -> Unit = {},
    onOpenLegal: (String) -> Unit = {},
    onOpenAccount: () -> Unit = {}) {
    val uiText = rememberUiText()
    val ctx = LocalContext.current
    val c = Zapara.colors
    val initial = savedDrafts[state.selectedSupportThreadId ?: "new"] ?: SupportLocalDraft()
    var open by rememberSaveable { mutableStateOf(false) }
    var threadQuery by rememberSaveable { mutableStateOf("") }
    var confirmDiscardDraft by rememberSaveable { mutableStateOf(false) }
    var subject by remember { mutableStateOf(initial.subject) }
    var body by remember { mutableStateOf(initial.body) }
    var photos by remember { mutableStateOf(initial.photos) }
    var logs by remember { mutableStateOf(emptyList<SupportAttachmentDraft>()) }
    var fileNote by remember { mutableStateOf(initial.fileNote) }
    var draftRevision by remember { mutableLongStateOf(initial.revision) }
    var draftKey by remember { mutableStateOf(state.selectedSupportThreadId ?: "new") }
    var drafts by remember { mutableStateOf(savedDrafts) }
    var submitted by remember { mutableStateOf<Pair<String, SupportLocalDraft>?>(null) }
    var attachmentReads by remember { androidx.compose.runtime.mutableIntStateOf(0) }
    var selectingAttachment by remember { mutableStateOf(false) }
    fun currentDraft() = SupportLocalDraft(subject, body, photos, logs, fileNote, draftRevision)
    fun persistDraft(changed: Boolean = false) {
        if (changed) draftRevision++
        drafts = if (subject.isBlank() && body.isBlank() && photos.isEmpty() && logs.isEmpty())
            drafts - draftKey else drafts + (draftKey to currentDraft())
        onDraftsChanged(drafts)
    }
    LaunchedEffect(draftKey, subject, body, photos, logs, fileNote) {
        persistDraft()
    }
    LaunchedEffect(state.selectedSupportThreadId) {
        val nextKey = state.selectedSupportThreadId ?: "new"
        if (nextKey != draftKey) {
            drafts = drafts + (draftKey to currentDraft())
            val next = drafts[nextKey] ?: SupportLocalDraft()
            draftKey = nextKey
            subject = next.subject; body = next.body; photos = next.photos; logs = emptyList(); fileNote = next.fileNote
            draftRevision = next.revision
        }
    }
    LaunchedEffect(state.reportSuccessVersion) {
        if (state.reportSuccessVersion > 0) {
            val successKey = state.reportSuccessKey
            if (successKey != null && submitted?.first == successKey) {
                drafts = drafts - successKey
                onDraftsChanged(drafts)
                if (draftKey == successKey && currentDraft() == submitted?.second) {
                    subject = ""; body = ""; photos = emptyList(); logs = emptyList(); fileNote = ""
                }
                submitted = null
            }
        }
    }
    val scope = rememberCoroutineScope()
    LaunchedEffect(draftKey) {
        val existingPhotos = photos
        if (existingPhotos.none { it.bytes == null }) return@LaunchedEffect
        attachmentReads++
        try {
            val restoredPhotos = withContext(Dispatchers.IO) { existingPhotos.map { ref ->
                if (ref.bytes != null) ref else readSupportFile(ctx, Uri.parse(ref.uri), 4 * 1024 * 1024)
                    ?.let { ref.copy(name = it.first, bytes = it.second) } ?: ref
            } }
            if (photos == existingPhotos) {
                photos = restoredPhotos
                if (restoredPhotos.any { it.bytes == null })
                    fileNote = ctx.getString(R.string.ux60_support_reselect_file)
            }
        } catch (e: kotlinx.coroutines.CancellationException) { throw e }
        catch (_: Exception) { fileNote = ctx.getString(R.string.ux60_support_reselect_file) }
        finally { attachmentReads-- }
    }
    fun take(uri: Uri?) {
        if (uri == null) return
        attachmentReads++
        scope.launch {
            try {
            val read = withContext(Dispatchers.IO) { readSupportFile(ctx, uri, 4 * 1024 * 1024) }
            if (read == null) {
                fileNote = ctx.getString(R.string.face_photo_too_big)
                return@launch
            }
            val ext = read.first.substringAfterLast('.', "").lowercase()
            if (ext !in setOf("jpg", "jpeg", "png", "webp")) {
                fileNote = ctx.getString(R.string.face_photo_type)
                return@launch
            }
            if (photos.size >= 3) { fileNote = ctx.getString(R.string.face_photo_limit); return@launch }
            fileNote = ""
            runCatching { ctx.contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION) }
            photos = photos + SupportAttachmentDraft(uri.toString(), read.first, read.second)
            persistDraft(changed = true)
            } catch (e: kotlinx.coroutines.CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSupport", "attachment read", e)
                fileNote = ctx.getString(R.string.face_file_unavailable)
            } finally { attachmentReads-- }
        }
    }
    val photo = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) {
        selectingAttachment = false; take(it)
    }
    ZCard(Modifier.fillMaxWidth().testTag("Settings.About")) {
        Text(stringResource(R.string.settings_about_title), style = Zapara.typography.section, color = c.text1)
        Text(stringResource(R.string.settings_version, state.version), style = Zapara.typography.caption, color = c.text2)
        Text(stringResource(R.string.settings_unofficial), style = Zapara.typography.body, color = c.text2)
        ZActionButton(stringResource(R.string.face_agreement), { onOpenLegal("agreement") },
            tag = "Settings.LegalAgreement", leadingIcon = R.drawable.ic_file)
        ZActionButton(stringResource(R.string.face_policy), { onOpenLegal("policy") },
            tag = "Settings.LegalPolicy", leadingIcon = R.drawable.ic_shield)
        ZActionButton(stringResource(R.string.settings_releases), {
            ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(AutoUpdate.RELEASES_PAGE)))
        }, tag = "Settings.Releases", leadingIcon = R.drawable.ic_external_link,
            trailingIcon = null)
        ZDisclosureButton(stringResource(R.string.settings_report), expanded = open,
            onClick = { open = !open }, tag = "Settings.Report", leadingIcon = R.drawable.ic_chat)
        Text(stringResource(if (state.signedIn) R.string.ux100_platform_support_signed_in_hint
            else R.string.settings_report_hint), style = Zapara.typography.caption, color = c.text2)
        if (open && !state.signedIn) ZActionButton(stringResource(R.string.ux100_platform_open_account),
            onOpenAccount, tag = "Settings.SupportOpenAccount", leadingIcon = R.drawable.ic_user)
        if (open && state.signedIn) {
            val inputStatus = SupportInputLimits.evaluate(subject, body, state.selectedSupportThreadId != null)
            if (confirmDiscardDraft) AlertDialog(
                onDismissRequest = { confirmDiscardDraft = false },
                title = { Text(stringResource(R.string.ux100_platform_discard_support_title)) },
                text = { Text(stringResource(R.string.ux100_platform_discard_support_body)) },
                confirmButton = {
                    ZButton(stringResource(R.string.ux30_platform_discard), {
                        subject = ""; body = ""; photos = emptyList(); logs = emptyList(); fileNote = ""
                        draftRevision++
                        drafts = drafts - draftKey
                        onDraftsChanged(drafts)
                        confirmDiscardDraft = false
                    }, destructive = true, leadingIcon = R.drawable.ic_trash,
                        tag = "Settings.ConfirmDiscardSupport")
                },
                dismissButton = { ZButton(stringResource(R.string.account_cancel),
                    { confirmDiscardDraft = false }, ghost = true, tag = "Settings.KeepSupportDraft") }
            )
            Text(stringResource(R.string.uxnext_support_threads, state.supportThreads.size),
                style = Zapara.typography.section, color = c.text1)
            if (state.supportLoading) Text(stringResource(R.string.uxnext_support_loading),
                style = Zapara.typography.caption, color = c.text2)
            state.supportError?.let { Text(it, style = Zapara.typography.body, color = c.bad) }
            ZButton(stringResource(R.string.repeat), { onEvent(SettingsEvent.RetrySupport) },
                ghost = true, enabled = !state.supportLoading && !state.reportSending,
                tag = "Settings.SupportRetry")
            val canSwitchThread = !state.reportSending && attachmentReads == 0 && !selectingAttachment
            ZActionButton(stringResource(R.string.uxnext_support_new),
                { onEvent(SettingsEvent.SelectSupportThread(null)) },
                enabled = canSwitchThread, tag = "Settings.SupportNew", leadingIcon = R.drawable.ic_plus)
            if (state.supportThreads.size > 4) ZTextField(threadQuery, { threadQuery = it },
                modifier = Modifier.fillMaxWidth().testTag("Settings.SupportThreadSearch"),
                placeholder = { Text(stringResource(R.string.ux100_platform_search_threads)) }, singleLine = true)
            val shownThreads = state.supportThreads.filter { thread ->
                threadQuery.isBlank() || thread.subject.contains(threadQuery.trim(), ignoreCase = true)
            }
            if (state.supportThreads.isNotEmpty() && shownThreads.isEmpty())
                Text(stringResource(R.string.ux100_platform_no_threads_found),
                    style = Zapara.typography.caption, color = c.text2)
            shownThreads.forEach { thread ->
                val current = state.selectedSupportThreadId == thread.id
                ZButton("${thread.subject} · ${thread.messageCount}",
                    { onEvent(SettingsEvent.SelectSupportThread(thread.id)) },
                    modifier = Modifier.fillMaxWidth().semantics { selected = current },
                    ghost = true, enabled = canSwitchThread,
                    tag = "Settings.SupportThread.${thread.id}", leadingIcon = R.drawable.ic_chat,
                    startAligned = true,
                    trailingIcon = if (current) null else R.drawable.ic_chevron_right)
            }
            if (state.selectedSupportThreadId == null) OutlinedTextField(subject,
                { subject = it; persistDraft(changed = true) }, modifier = Modifier.fillMaxWidth(),
                label = { Text(stringResource(R.string.face_subject)) }, singleLine = true,
                enabled = !state.reportSending)
            else Text(state.supportThreads.firstOrNull { it.id == state.selectedSupportThreadId }?.subject.orEmpty(),
                style = Zapara.typography.bodyStrong, color = c.text1)
            if (state.selectedSupportThreadId == null) Text(stringResource(R.string.ux60_support_subject_count,
                inputStatus.subjectScalars, inputStatus.subjectWireUnits), style = Zapara.typography.caption,
                color = if (inputStatus.subjectOverLimit) c.bad else c.text2)
            OutlinedTextField(body, { body = it; persistDraft(changed = true) }, modifier = Modifier.fillMaxWidth(),
                label = { Text(stringResource(if (state.selectedSupportThreadId == null)
                    R.string.face_what_happened else R.string.uxnext_support_reply)) }, enabled = !state.reportSending)
            Text(stringResource(R.string.ux60_support_body_count, inputStatus.bodyScalars, inputStatus.bodyWireUnits),
                style = Zapara.typography.caption, color = if (inputStatus.bodyOverLimit) c.bad else c.text2)
            if ((subject.isNotBlank() || body.isNotBlank() || photos.isNotEmpty() || logs.isNotEmpty()) &&
                !state.reportSending && attachmentReads == 0) ZButton(
                stringResource(R.string.ux100_platform_discard_support),
                { confirmDiscardDraft = true }, ghost = true, tag = "Settings.DiscardSupportDraft")
            if (inputStatus.subjectOverLimit || inputStatus.bodyOverLimit || inputStatus.invalidUnicode)
                Text(stringResource(if (inputStatus.invalidUnicode) R.string.ux60_support_invalid_unicode
                    else R.string.ux60_support_over_limit), style = Zapara.typography.caption, color = c.bad)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.face_photo), {
                    selectingAttachment = true
                    try { photo.launch(arrayOf("image/*")) } catch (e: Exception) {
                        selectingAttachment = false; fileNote = ctx.getString(R.string.face_file_unavailable)
                    }
                }, ghost = true, tag = "Settings.ReportPhoto",
                    enabled = !state.reportSending && !selectingAttachment && attachmentReads == 0)
            }
            Text(stringResource(R.string.face_attach_limits), style = Zapara.typography.caption, color = c.text2)
            if (attachmentReads > 0 || selectingAttachment) Text(stringResource(R.string.ux30_support_reading),
                style = Zapara.typography.caption, color = c.text2)
            photos.forEachIndexed { index, file ->
                ZButton(stringResource(R.string.ux30_support_remove_file, file.name),
                    { photos = photos.filterIndexed { i, _ -> i != index }; persistDraft(changed = true) }, ghost = true,
                    enabled = !state.reportSending, tag = "Settings.RemovePhoto.$index")
            }
            if (fileNote.isNotBlank()) Text(fileNote, style = Zapara.typography.body, color = c.text1)
            ZButton(stringResource(R.string.face_send), {
                if (photos.any { it.bytes == null }) {
                    fileNote = ctx.getString(R.string.ux60_support_reselect_file)
                    return@ZButton
                }
                submitted = draftKey to currentDraft()
                onEvent(SettingsEvent.Report(subject, body, photos.map { it.name to it.bytes!! }, draftRevision))
            }, tag = "Settings.ReportSend", enabled = SettingsLogic.supportSendReady(
                state.reportSending, selectingAttachment, attachmentReads) && inputStatus.canSend &&
                photos.all { it.bytes != null },
                busy = state.reportSending)
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

/** #109: открытие документа из формы регистрации не сбрасывает пароль, иначе после возврата ввод потерян. */
object LegalReturn {
    fun keepsRegistration(guest: Boolean, registration: Boolean): Boolean = guest && registration
}
