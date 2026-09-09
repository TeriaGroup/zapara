package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
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
    ZBottomSheet({ onEvent(ScheduleEvent.RenameCancel) }, "Sheet.Rename") {
        Text(stringResource(R.string.rename_title), style = Zapara.typography.section, color = c.text1)
        Spacer(Modifier.height(Zapara.space.s))
        Field(ui.name, { onEvent(ScheduleEvent.RenameChanged(it, ui.note, ui.scope)) }, "Editor.Text")
        Text(stringResource(R.string.rename_original, ui.original), style = Zapara.typography.caption, color = c.text2)
        Field(ui.note, { onEvent(ScheduleEvent.RenameChanged(ui.name, it, ui.scope)) }, "Editor.Note")
        ZSegmented(
            listOf(stringResource(R.string.scope_everywhere), stringResource(R.string.scope_day_only, ui.dayName)),
            ui.scope, { onEvent(ScheduleEvent.RenameChanged(ui.name, ui.note, it)) }, "Editor.Scope"
        )
        Text(stringResource(R.string.rename_preview, ui.name.ifBlank { ui.original }), style = Zapara.typography.caption, color = c.text2)
        Spacer(Modifier.height(Zapara.space.m))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            if (ui.hasExisting) ZButton(stringResource(R.string.reset), { onEvent(ScheduleEvent.RenameReset) }, ghost = true, tag = "Editor.Reset")
            Spacer(Modifier.weight(1f))
            ZButton(stringResource(R.string.theme_cancel), { onEvent(ScheduleEvent.RenameCancel) }, ghost = true, tag = "Editor.Cancel")
            ZButton(stringResource(R.string.theme_save), { onEvent(ScheduleEvent.RenameSave) }, enabled = ui.name.isNotBlank() || ui.note.isNotBlank(), tag = "Editor.Save")
        }
    }
}

@Composable
private fun Field(value: String, onChange: (String) -> Unit, tag: String) {
    val c = Zapara.colors
    OutlinedTextField(
        value = value, onValueChange = onChange,
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
