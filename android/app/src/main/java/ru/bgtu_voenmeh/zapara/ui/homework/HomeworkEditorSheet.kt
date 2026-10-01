package ru.bgtu_voenmeh.zapara.ui.homework

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.LocalUiCopy
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkAudience

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
    onShare: (Boolean) -> Unit = {},
    onAudience: (HomeworkAudience) -> Unit = {},
    onRetryShare: () -> Unit = {},
    isGuest: Boolean = false,
    onRecalculate: () -> Unit = {},
    onRetryShareOptions: () -> Unit = {}
) {
    val photo = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) onPick("photo", uri)
    }
    val document = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) onPick("document", uri)
    }
    val c = Zapara.colors
    var confirmDiscard by rememberSaveable(state.draft) { mutableStateOf(false) }
    val canClose = {
        when {
            state.busy -> false
            state.hasDraftChanges -> { confirmDiscard = true; false }
            else -> true
        }
    }
    val requestCancel = { if (canClose()) onCancel() }
    val countLabel = when (state.n) {
        1 -> R.string.ux_homework_due_one
        in 2..4 -> R.string.ux_homework_due_few
        else -> R.string.ux_homework_due_many
    }
    val largeText = LocalDensity.current.fontScale >= 1.5f
    val saveLabel = stringResource(if (state.work == HomeworkEditorWork.Saving)
        R.string.homework_ux_saving else R.string.theme_save)
    val status = when (state.work) {
        HomeworkEditorWork.Saving -> stringResource(R.string.homework_ux_saving)
        HomeworkEditorWork.Attachment -> stringResource(R.string.homework_ux_attachment)
        HomeworkEditorWork.Recalculating -> stringResource(R.string.homework_ux_recalculating)
        HomeworkEditorWork.Idle -> state.error
    }
    ZBottomSheet(onCancel, "Sheet.Homework", scrollable = true, canDismiss = canClose, footer = {
        if (largeText) {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(saveLabel, onSave, enabled = state.canSave, tag = "Editor.Save",
                    modifier = Modifier.fillMaxWidth())
                ZButton(stringResource(R.string.theme_cancel), requestCancel, ghost = true,
                    enabled = !state.busy, tag = "Editor.Cancel", modifier = Modifier.fillMaxWidth())
            }
        } else {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.theme_cancel), requestCancel, ghost = true,
                    enabled = !state.busy, tag = "Editor.Cancel")
                ZButton(saveLabel, onSave, enabled = state.canSave, tag = "Editor.Save",
                    modifier = Modifier.weight(1f))
            }
        }
    }) {
        Text(state.subjectDisplay, style = Zapara.typography.section, color = c.text1)
        Spacer(Modifier.height(Zapara.space.s))
        ZTextField(
            value = state.text, onValueChange = onText,
            modifier = Modifier.fillMaxWidth().testTag("Editor.Text"),
            placeholder = { Text(stringResource(R.string.hw_editor_hint), style = Zapara.typography.caption, color = c.text3) },
            label = { Text(stringResource(R.string.polish_homework_task)) },
            enabled = !state.busy,
            isError = state.text.isNotBlank() && !HomeworkTextRules.valid(state.text),
            supportingText = { Text(when {
                state.text.isBlank() -> stringResource(R.string.homework_ux_empty_text)
                HomeworkTextRules.scalars(state.text) > HomeworkTextRules.limit ->
                    stringResource(R.string.ux60_homework_text_too_long, HomeworkTextRules.scalars(state.text))
                !HomeworkTextRules.valid(state.text) -> stringResource(R.string.ux60_homework_text_invalid)
                else -> stringResource(R.string.ux60_homework_text_count, HomeworkTextRules.scalars(state.text))
            }) },
            minLines = 3
        )
        Spacer(Modifier.height(Zapara.space.m))
        ZCard(Modifier.fillMaxWidth()) {
            Text(stringResource(R.string.polish_homework_deadline), style = Zapara.typography.bodyStrong, color = c.text1)
            Text(state.dueText(LocalUiCopy.current), style = Zapara.typography.body, color = c.text1,
                modifier = Modifier.fillMaxWidth().testTag("Editor.Due"))
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZIconButton(R.drawable.ic_minus, stringResource(R.string.hw_due_decrease), onDec, "Editor.Dec", enabled = !state.busy && state.n > 1)
                Text(stringResource(countLabel, state.n),
                    style = Zapara.typography.body, color = c.text1,
                    modifier = Modifier.weight(1f).testTag("Editor.Count"))
                ZIconButton(R.drawable.ic_plus, stringResource(R.string.hw_due_increase), onInc, "Editor.Inc", enabled = !state.busy && state.n < 10)
            }
            if (state.sourceChanged) {
                Text(stringResource(R.string.review_homework_source_changed), style = Zapara.typography.caption, color = c.warn)
                ZButton(stringResource(R.string.review_recalculate_due), onRecalculate, ghost = true, enabled = !state.busy)
            }
        }
        Spacer(Modifier.height(Zapara.space.m))
        Text(stringResource(R.string.polish_homework_attachments), style = Zapara.typography.bodyStrong, color = c.text1)
        Text(stringResource(R.string.homework_ux_files_count, state.files.size), style = Zapara.typography.caption, color = c.text2)
        Spacer(Modifier.height(Zapara.space.s))
        FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.hw_attach_photo), { photo.launch(arrayOf("image/jpeg", "image/png", "image/webp", "image/gif")) }, ghost = true, enabled = !state.busy && state.files.size < 6, tag = "Editor.Photo", leadingIcon = R.drawable.ic_plus)
            ZButton(stringResource(R.string.hw_attach_document), {
                document.launch(arrayOf(
                    "application/pdf", "text/plain", "text/csv", "application/rtf",
                    "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    "application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "application/vnd.ms-powerpoint", "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                    "application/vnd.oasis.opendocument.text", "application/vnd.oasis.opendocument.spreadsheet",
                    "application/vnd.oasis.opendocument.presentation", "application/zip"
                ))
            }, ghost = true, enabled = !state.busy && state.files.size < 6, tag = "Editor.Document", leadingIcon = R.drawable.ic_paperclip)
        }
        state.files.forEach { file ->
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Text(file.name, style = Zapara.typography.caption, color = c.text1, modifier = Modifier.weight(1f))
                ZButton(stringResource(R.string.hw_attach_remove), { onRemove(file.id) }, ghost = true, enabled = !state.busy, tag = "Editor.Remove.${file.id}")
            }
        }
        if (!state.isEdit && isGuest) {
            Spacer(Modifier.height(Zapara.space.s))
            Text(
                stringResource(R.string.homework_guest_share_hint),
                style = Zapara.typography.caption,
                color = c.text2,
                modifier = Modifier.fillMaxWidth()
            )
        } else if (!state.isEdit) {
            Spacer(Modifier.height(Zapara.space.s))
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Checkbox(
                    checked = state.share,
                    onCheckedChange = onShare,
                    enabled = !state.busy,
                    modifier = Modifier.testTag("Editor.Share"),
                    colors = CheckboxDefaults.colors(
                        checkedColor = c.text1,
                        uncheckedColor = c.text3,
                        checkmarkColor = c.canvas,
                    ),
                )
                Text(
                    stringResource(R.string.face_share_group),
                    style = Zapara.typography.body,
                    color = c.text1,
                    modifier = Modifier.weight(1f),
                )
            }
            if (state.share) {
                val context = state.shareContext
                Text(stringResource(R.string.homework_share_group, context?.groupName ?: stringResource(R.string.homework_share_group_loading)), style = Zapara.typography.bodyStrong, color = c.text1)
                Text(stringResource(R.string.homework_share_context, state.dueText(LocalUiCopy.current)), style = Zapara.typography.caption, color = c.text2)
                if (state.shareLoading) Text(stringResource(R.string.homework_share_loading), style = Zapara.typography.caption, color = c.text2)
                else if (context == null) {
                    Text(stringResource(R.string.homework_share_unavailable), style = Zapara.typography.caption, color = c.warn)
                    ZButton(stringResource(R.string.ux60_share_options_retry), onRetryShareOptions,
                        ghost = true, enabled = !state.busy, tag = "Editor.ShareOptionsRetry")
                }
                else if (context.supported) HomeworkAudiencePicker(state.draft, state.audience,
                    context.desk.roles.map { AudienceChoice(it.roleId, it.name) },
                    context.people.filterNot { it.self }.map { AudienceChoice(it.userId, it.displayName ?: it.username) },
                    !state.busy, onAudience)
                else Text(stringResource(R.string.homework_share_old_server), style = Zapara.typography.caption, color = c.text2)
                Text(stringResource(R.string.homework_share_files_local), style = Zapara.typography.caption, color = c.text2)
            }
        }
        Spacer(Modifier.height(Zapara.space.m))
        if (status != null) {
            Text(status, style = Zapara.typography.caption,
                color = if (state.error != null && !state.busy) c.bad else c.text2,
                modifier = Modifier.fillMaxWidth().testTag("Editor.Status")
                    .semantics { liveRegion = LiveRegionMode.Polite })
            Spacer(Modifier.height(Zapara.space.s))
        }
        if (state.shareRequest != null) {
            Text(stringResource(R.string.homework_share_uncertain), style = Zapara.typography.caption, color = c.warn)
            if (state.shareRequest.operationId != null) ZButton(stringResource(R.string.homework_share_retry), onRetryShare, enabled = !state.busy, ghost = true)
            else Text(stringResource(R.string.homework_share_retry_old_server), style = Zapara.typography.caption, color = c.warn)
        }
    }
    if (confirmDiscard) AlertDialog(
        onDismissRequest = { confirmDiscard = false },
        title = { Text(stringResource(R.string.homework_ux_discard_title), style = Zapara.typography.section) },
        text = { Text(stringResource(if (state.persistedId == null) R.string.homework_ux_discard_hint else R.string.homework_ux_partial_discard_hint)) },
        confirmButton = {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.homework_ux_keep_editing), { confirmDiscard = false },
                    tag = "Editor.KeepEditing", modifier = Modifier.fillMaxWidth())
                ZButton(stringResource(R.string.homework_ux_discard), { confirmDiscard = false; onCancel() },
                    ghost = true, enabled = !state.busy, tag = "Editor.Discard", modifier = Modifier.fillMaxWidth())
            }
        },
        containerColor = c.card
    )
}
