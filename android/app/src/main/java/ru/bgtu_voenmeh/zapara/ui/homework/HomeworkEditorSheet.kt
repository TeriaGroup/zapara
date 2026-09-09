package ru.bgtu_voenmeh.zapara.ui.homework

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
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.LocalUiCopy
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun HomeworkEditorSheet(
    state: HomeworkEditorState,
    onText: (String) -> Unit,
    onInc: () -> Unit,
    onDec: () -> Unit,
    onSave: () -> Unit,
    onCancel: () -> Unit
) {
    val c = Zapara.colors
    ZBottomSheet(onCancel, "Sheet.Homework") {
        Text(state.subjectDisplay, style = Zapara.typography.section, color = c.text1)
        Spacer(Modifier.height(Zapara.space.s))
        OutlinedTextField(
            value = state.text, onValueChange = onText,
            modifier = Modifier.fillMaxWidth().testTag("Editor.Text"),
            placeholder = { Text(stringResource(R.string.hw_editor_hint), style = Zapara.typography.caption, color = c.text3) },
            minLines = 3,
            shape = RoundedCornerShape(Zapara.radii.control),
            colors = OutlinedTextFieldDefaults.colors(
                focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
                focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
                focusedTextColor = c.text1, unfocusedTextColor = c.text1
            )
        )
        Spacer(Modifier.height(Zapara.space.s))
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZIconButton(R.drawable.ic_minus, stringResource(R.string.hw_due_prefix, ""), onDec, "Editor.Dec")
            Text(state.dueText(LocalUiCopy.current), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f).testTag("Editor.Due"))
            ZIconButton(R.drawable.ic_plus, stringResource(R.string.add), onInc, "Editor.Inc")
        }
        Spacer(Modifier.height(Zapara.space.m))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.theme_cancel), onCancel, ghost = true, tag = "Editor.Cancel")
            Spacer(Modifier.weight(1f))
            ZButton(stringResource(R.string.theme_save), onSave, enabled = state.canSave, tag = "Editor.Save")
        }
    }
}
