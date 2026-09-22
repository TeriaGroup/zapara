package ru.bgtu_voenmeh.zapara.ui.homework

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
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

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun HomeworkEditorSheet(
    state: HomeworkEditorState,
    onText: (String) -> Unit,
    onInc: () -> Unit,
    onDec: () -> Unit,
    onSave: () -> Unit,
    onCancel: () -> Unit,
    onPick: (String, Uri) -> Unit = { _, _ -> },
    onRemove: (String) -> Unit = {},
    onShare: (Boolean) -> Unit = {}
) {
    val photo = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) onPick("photo", uri)
    }
    val document = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) onPick("document", uri)
    }
    val c = Zapara.colors
    val countLabel = when (state.n) {
        1 -> R.string.ux_homework_due_one
        in 2..4 -> R.string.ux_homework_due_few
        else -> R.string.ux_homework_due_many
    }
    ZBottomSheet(onCancel, "Sheet.Homework", scrollable = true) {
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
            ZIconButton(R.drawable.ic_minus, stringResource(R.string.hw_due_decrease), onDec, "Editor.Dec")
            Text(stringResource(countLabel, state.n),
                style = Zapara.typography.caption, color = c.text1,
                modifier = Modifier.weight(1f).testTag("Editor.Count"))
            ZIconButton(R.drawable.ic_plus, stringResource(R.string.hw_due_increase), onInc, "Editor.Inc")
        }
        Text(state.dueText(LocalUiCopy.current), style = Zapara.typography.body, color = c.text1,
            modifier = Modifier.fillMaxWidth().testTag("Editor.Due"))
        Spacer(Modifier.height(Zapara.space.s))
        FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.hw_attach_photo), { photo.launch(arrayOf("image/jpeg", "image/png", "image/webp", "image/gif")) }, ghost = true, tag = "Editor.Photo")
            ZButton(stringResource(R.string.hw_attach_document), {
                document.launch(arrayOf(
                    "application/pdf", "text/plain", "text/csv", "application/rtf",
                    "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    "application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "application/vnd.ms-powerpoint", "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                    "application/vnd.oasis.opendocument.text", "application/vnd.oasis.opendocument.spreadsheet",
                    "application/vnd.oasis.opendocument.presentation", "application/zip"
                ))
            }, ghost = true, tag = "Editor.Document")
        }
        state.files.forEach { file ->
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Text(file.name, style = Zapara.typography.caption, color = c.text1, modifier = Modifier.weight(1f))
                ZButton(stringResource(R.string.hw_attach_remove), { onRemove(file.id) }, ghost = true, tag = "Editor.Remove.${file.id}")
            }
        }
        if (!state.isEdit) {
            Spacer(Modifier.height(Zapara.space.s))
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Checkbox(
                    checked = state.share,
                    onCheckedChange = onShare,
                    modifier = Modifier.testTag("Editor.Share"),
                    colors = CheckboxDefaults.colors(
                        checkedColor = c.text1,
                        uncheckedColor = c.text3,
                        checkmarkColor = c.canvas,
                    ),
                )
                Text(
                    "Дублировать всей группе — одна и та же домашка появится у всех участников",
                    style = Zapara.typography.body,
                    color = c.text1,
                    modifier = Modifier.weight(1f),
                )
            }
        }
        Spacer(Modifier.height(Zapara.space.m))
        FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.theme_cancel), onCancel, ghost = true, tag = "Editor.Cancel")
            ZButton(stringResource(R.string.theme_save), onSave, enabled = state.canSave, tag = "Editor.Save")
        }
    }
}
