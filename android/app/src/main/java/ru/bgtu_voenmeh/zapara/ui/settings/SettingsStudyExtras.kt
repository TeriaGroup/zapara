package ru.bgtu_voenmeh.zapara.ui.settings

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.AnnotatedString
import ru.bgtu_voenmeh.zapara.BuildConfig
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.*
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import ru.bgtu_voenmeh.zapara.ui.LocalUiCopy

internal fun safeSupportDiagnostics(version: String, sdk: Int, stale: Boolean, hasGroup: Boolean,
    pending: Int, conflicts: Int, copy: UiCopy): String = listOf(
    copy.get("ux300_diag_header"),
    copy.get("ux300_diag_version", version.takeIf { it.matches(Regex("[0-9]+(\\.[0-9]+){1,3}")) } ?: copy.get("ux300_diag_unknown")),
    copy.get("ux300_diag_platform", sdk.coerceIn(1, 999)),
    copy.get("ux300_diag_group", copy.get(if (hasGroup) "ux300_diag_yes" else "ux300_diag_no")),
    copy.get("ux300_diag_stale", copy.get(if (stale) "ux300_diag_yes" else "ux300_diag_no")),
    copy.get("ux300_diag_pending", pending.coerceAtLeast(0)),
    copy.get("ux300_diag_conflicts", conflicts.coerceAtLeast(0))
).joinToString("\n")

@Composable internal fun SettingsStudyExtras(state: SettingsUiState, event: (SettingsEvent) -> Unit) {
    val impact = state.subgroupImpact
    val context = LocalContext.current
    val dark = Zapara.colors.isDark
    if (state.subgroupImpactLoading) ZBottomSheet({ event(SettingsEvent.CloseSubgroupImpact) }, "Settings.SubgroupImpactLoading", scrollable = true) {
        Text(stringResource(R.string.ux300_subgroup_preview), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_room_loading), style = Zapara.typography.body)
    }
    if (impact != null) ZBottomSheet({ event(SettingsEvent.CloseSubgroupImpact) }, "Settings.SubgroupImpact", scrollable = true) {
        Text(stringResource(R.string.ux300_subgroup_preview), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_subgroup_scope), style = Zapara.typography.caption)
        ZButton("${ru.bgtu_voenmeh.zapara.ui.week.agendaDateLabel(impact.weekStart)} — ${ru.bgtu_voenmeh.zapara.ui.week.agendaDateLabel(impact.weekStart.plusDays(6))}", {
            val date = impact.weekStart
            android.app.DatePickerDialog(context, if (dark) R.style.Zapara_DatePicker_Dark else R.style.Zapara_DatePicker_Light,
                { _, year, month, day -> event(SettingsEvent.PreviewSubgroup(impact.event, java.time.LocalDate.of(year, month + 1, day))) },
                date.year, date.monthValue - 1, date.dayOfMonth).show()
        }, ghost = true, tag = "Settings.SubgroupImpact.Week")
        if (impact.unknownDays > 0) Text(stringResource(R.string.ux300_agenda_unknown_days, impact.unknownDays), style = Zapara.typography.caption)
        if (impact.changes.isEmpty()) Text(stringResource(R.string.ux300_refresh_same))
        impact.changes.forEach { change -> ZCard(Modifier.fillMaxWidth()) {
            Text(change.afterDate.toString(), style = Zapara.typography.bodyStrong)
            change.removed.forEach { Text(stringResource(R.string.ux300_ext_week_removed, change.beforeDate.toString(), "${it.time} · ${it.subjectRaw} · ${it.classroomRaw}")) }
            change.added.forEach { Text(stringResource(R.string.ux300_ext_week_added, change.afterDate.toString(), "${it.time} · ${it.subjectRaw} · ${it.classroomRaw}")) }
        } }
        ZButton(stringResource(R.string.ux300_subgroup_apply), { event(SettingsEvent.ConfirmSubgroupImpact) }, tag = "Settings.SubgroupImpact.Apply")
        ZButton(stringResource(R.string.account_cancel), { event(SettingsEvent.CloseSubgroupImpact) }, ghost = true)
    }
}

@Composable internal fun SupportDiagnosticsTool(state: SettingsUiState) {
    var preview by remember(state.profileName, state.groupId) { mutableStateOf<String?>(null) }
    val clipboard = LocalClipboardManager.current
    val copy = LocalUiCopy.current
    ZButton(stringResource(R.string.ux300_diagnostics_title), {
        preview = safeSupportDiagnostics(BuildConfig.VERSION_NAME, android.os.Build.VERSION.SDK_INT, state.stale,
            state.groupId.isNotBlank(), state.pendingSync.size, state.syncConflicts.size, copy)
    }, ghost = true, tag = "Settings.Diagnostics")
    if (preview != null) ZBottomSheet({ preview = null }, "Settings.DiagnosticsSheet", scrollable = true) {
        Text(stringResource(R.string.ux300_diagnostics_title), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_diagnostics_scope), style = Zapara.typography.caption)
        Text(preview!!, style = Zapara.typography.body)
        ZButton(stringResource(R.string.ux300_diagnostics_copy), { clipboard.setText(AnnotatedString(preview!!)) }, ghost = true)
    }
}
