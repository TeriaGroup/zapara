package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun RenameSheet(ui: RenameUi, onEvent: (ScheduleEvent) -> Unit) {
    val c = Zapara.colors
    var confirmReset by remember(ui.lesson.subjectNorm, ui.scope) { mutableStateOf(false) }
    ZBottomSheet({ onEvent(ScheduleEvent.RenameCancel) }, "Sheet.Rename", scrollable = true,
        canDismiss = { !ui.busy }, footer = {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.theme_save), { onEvent(ScheduleEvent.RenameSave(ui)) },
                    modifier = Modifier.fillMaxWidth(), enabled = !ui.busy && (ui.name.isNotBlank() || ui.note.isNotBlank()),
                    busy = ui.busy, tag = "Editor.Save", leadingIcon = R.drawable.ic_check)
                FlowRow(Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(Zapara.space.s, Alignment.End),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    if (ui.hasExisting) ZButton(stringResource(R.string.reset), { confirmReset = true },
                        ghost = true, tag = "Editor.Reset", enabled = !ui.busy,
                        leadingIcon = R.drawable.ic_refresh)
                    ZButton(stringResource(R.string.theme_cancel), { onEvent(ScheduleEvent.RenameCancel) },
                        ghost = true, tag = "Editor.Cancel", enabled = !ui.busy, leadingIcon = R.drawable.ic_x)
                }
            }
        }) {
        Text(stringResource(R.string.rename_title), style = Zapara.typography.section, color = c.text1)
        Spacer(Modifier.height(Zapara.space.s))
        Field(ui.name, { onEvent(ScheduleEvent.RenameChanged(it, ui.note, ui.scope)) }, "Editor.Text", !ui.busy)
        Text(stringResource(R.string.rename_original, ui.original), style = Zapara.typography.caption, color = c.text2)
        Text(stringResource(R.string.rename_note), style = Zapara.typography.caption, color = c.text2)
        Field(ui.note, { onEvent(ScheduleEvent.RenameChanged(ui.name, it, ui.scope)) }, "Editor.Note", !ui.busy)
        Spacer(Modifier.height(Zapara.space.m))
        ZSegmented(
            listOf(stringResource(R.string.scope_everywhere), stringResource(R.string.scope_day_only, ui.dayName)),
            ui.scope, { onEvent(ScheduleEvent.RenameChanged(ui.name, ui.note, it)) }, "Editor.Scope"
        )
        Text(stringResource(R.string.rename_preview, ui.name.ifBlank { ui.original }), style = Zapara.typography.caption, color = c.text2)
        val affected = if (ui.scope == 0) ui.affectedGlobal else ui.affectedWeekday
        Text(stringResource(R.string.ux300_android_rename_affected, affected.size),
            style = Zapara.typography.caption, color = c.text2)
        affected.take(5).forEach { row -> Text(row, style = Zapara.typography.caption,
            color = c.text1) }
        if (affected.size > 5) Text(pluralStringResource(R.plurals.ux300_android_rename_more,
            affected.size - 5, affected.size - 5), style = Zapara.typography.caption, color = c.text2)
        ui.error?.let { Text(it, style = Zapara.typography.caption, color = c.bad) }
        if (ui.busy) Text(stringResource(R.string.ux60_saving), style = Zapara.typography.caption, color = c.text2)
    }
    if (confirmReset) AlertDialog(onDismissRequest = { confirmReset = false },
        title = { Text(stringResource(R.string.ux60_rename_reset_title)) },
        text = { Text(stringResource(R.string.ux60_rename_reset_scope,
            if (ui.scope == 0) stringResource(R.string.scope_everywhere)
            else stringResource(R.string.scope_day_only, ui.dayName))) },
        confirmButton = { ZButton(stringResource(R.string.reset), {
            confirmReset = false; onEvent(ScheduleEvent.RenameReset(ui))
        }, enabled = !ui.busy) },
        dismissButton = { ZButton(stringResource(R.string.theme_cancel), { confirmReset = false }, ghost = true) })
}

@Composable
private fun Field(value: String, onChange: (String) -> Unit, tag: String, enabled: Boolean) {
    val c = Zapara.colors
    OutlinedTextField(
        value = value, onValueChange = onChange,
        enabled = enabled,
        modifier = Modifier.fillMaxWidth().testTag(tag),
        singleLine = true,
        shape = RoundedCornerShape(Zapara.radii.control),
        colors = OutlinedTextFieldDefaults.colors(
            focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
            focusedBorderColor = c.text1, unfocusedBorderColor = c.lineStrong, // #109 / AN-12
            focusedTextColor = c.text1, unfocusedTextColor = c.text1
        )
    )
}
