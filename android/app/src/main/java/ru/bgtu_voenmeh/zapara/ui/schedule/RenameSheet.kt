package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
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
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun RenameSheet(ui: RenameUi, onEvent: (ScheduleEvent) -> Unit) {
    val c = Zapara.colors
    var confirmReset by remember(ui.lesson.subjectNorm, ui.scope) { mutableStateOf(false) }
    ZBottomSheet({ onEvent(ScheduleEvent.RenameCancel) }, "Sheet.Rename", canDismiss = { !ui.busy }) {
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
        ui.error?.let { Text(it, style = Zapara.typography.caption, color = c.bad) }
        if (ui.busy) Text(stringResource(R.string.ux60_saving), style = Zapara.typography.caption, color = c.text2)
        Spacer(Modifier.height(Zapara.space.m))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            if (ui.hasExisting) ZButton(stringResource(R.string.reset), { confirmReset = true },
                ghost = true, tag = "Editor.Reset", enabled = !ui.busy)
            Spacer(Modifier.weight(1f))
            ZButton(stringResource(R.string.theme_cancel), { onEvent(ScheduleEvent.RenameCancel) }, ghost = true,
                tag = "Editor.Cancel", enabled = !ui.busy)
            ZButton(stringResource(R.string.theme_save), { onEvent(ScheduleEvent.RenameSave(ui)) },
                enabled = !ui.busy && (ui.name.isNotBlank() || ui.note.isNotBlank()), tag = "Editor.Save")
        }
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
            focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
            focusedTextColor = c.text1, unfocusedTextColor = c.text1
        )
    )
}
