package ru.bgtu_voenmeh.zapara.ui.homework

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun HomeworkSection(state: HomeworkUiState, onEvent: (HomeworkEvent) -> Unit) {
    val chrome = LocalShellChrome.current
    val c = Zapara.colors
    val keyboard = LocalSoftwareKeyboardController.current
    val browse = HomeworkBrowse.filter(state.groups, state.browseQuery, state.browseFilter)
    val visibleShared = HomeworkBrowse.shared(state.sharedRows, state.browseQuery, state.browseFilter)
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_homework)) {
            if (state.hasGroup) ZIconButton(R.drawable.ic_plus, stringResource(R.string.add), { onEvent(HomeworkEvent.Add) }, "Homework.Add")
        }
        Box(Modifier.weight(1f)) {
        when {
            !state.loaded -> Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
            state.loadError != null && !state.hasGroup -> EmptyState(R.drawable.ic_homework,
                stringResource(R.string.uxnext_homework_load_failed),
                actionText = stringResource(R.string.repeat),
                onAction = { onEvent(HomeworkEvent.RetryLoad) }, tag = "Homework.LoadFail")
            !state.hasGroup -> EmptyState(R.drawable.ic_homework, stringResource(R.string.empty_no_group), stringResource(R.string.empty_no_group_hint), stringResource(R.string.group_pick), chrome.onGroupChip, "Empty.NoGroup")
            else -> {
                val cascade = HashMap<String, Int>()
                var n = 0
                browse.groups.forEach { group ->
                    cascade["g-${group.status}"] = n++
                    if (!group.collapsed) group.items.forEach { cascade["i-${it.id}"] = n++ }
                }
                LazyColumn(
                Modifier.fillMaxSize(),
                contentPadding = PaddingValues(start = Zapara.space.l, top = Zapara.space.l,
                    end = Zapara.space.l, bottom = if (state.undoDone != null)
                        Zapara.space.l + Zapara.space.minTouch + Zapara.space.l + Zapara.space.s else Zapara.space.l),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
            ) {
                state.loadError?.let { error -> item("load-error") {
                    ZCard(Modifier.fillMaxWidth(), tag = "Homework.LoadError") {
                        Text(error, style = Zapara.typography.body, color = c.text1)
                        ZButton(stringResource(R.string.repeat), { onEvent(HomeworkEvent.RetryLoad) }, ghost = true)
                    }
                } }
                item("browse") {
                    Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        ZTextField(state.browseQuery, { onEvent(HomeworkEvent.BrowseQuery(it)) },
                            modifier = Modifier.fillMaxWidth().testTag("Homework.Search"),
                            placeholder = { Text(stringResource(R.string.homework_browse_search)) }, singleLine = true,
                            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                            keyboardActions = KeyboardActions(onSearch = { keyboard?.hide() }),
                            trailingIcon = if (state.browseQuery.isNotEmpty()) {{
                                ZIconButton(R.drawable.ic_x, stringResource(R.string.ux30_study_clear_homework_search),
                                    { onEvent(HomeworkEvent.BrowseQuery("")) }, "Homework.ClearSearch")
                            }} else null)
                        FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                            HomeworkCompletionFilter.entries.forEach { filter ->
                                val title = when (filter) {
                                    HomeworkCompletionFilter.Active -> R.string.homework_browse_active
                                    HomeworkCompletionFilter.Done -> R.string.homework_browse_done
                                    HomeworkCompletionFilter.All -> R.string.homework_browse_all
                                }
                                ZChip(stringResource(title), selected = state.browseFilter == filter,
                                    onClick = { onEvent(HomeworkEvent.BrowseFilter(filter)) }, tag = "Homework.Filter.$filter")
                            }
                        }
                    }
                }
                item("summary") {
                    Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        ZChip(stringResource(R.string.other_homework_open,
                            browse.totalActive + state.sharedRows.count { !it.completed }), tag = "Homework.OpenCount")
                        ZChip(stringResource(R.string.other_homework_done,
                            browse.totalDone + state.sharedRows.count { it.completed }), tag = "Homework.DoneCount")
                    }
                    Text(stringResource(R.string.ux30_study_browse_count, browse.visibleCount + visibleShared.size),
                        style = Zapara.typography.caption, color = c.text2)
                    }
                }
                if (browse.visibleCount + visibleShared.size == 0 && state.loadError == null && !state.sharedLoading && state.sharedError == null) {
                    item("empty") {
                        if (state.groups.isEmpty() && state.sharedRows.isEmpty()) {
                            EmptyState(R.drawable.ic_homework, stringResource(R.string.hw_empty_title),
                                stringResource(R.string.hw_empty_hint), stringResource(R.string.add),
                                { onEvent(HomeworkEvent.Add) }, "Empty.Homework")
                        } else {
                            EmptyState(R.drawable.ic_homework, stringResource(R.string.homework_browse_no_results),
                                stringResource(R.string.homework_browse_no_results_hint),
                                stringResource(R.string.homework_browse_reset),
                                { onEvent(HomeworkEvent.BrowseReset) }, "Empty.HomeworkFiltered")
                        }
                    }
                }
                browse.groups.forEach { group ->
                    item("g-${group.status}") {
                        val expandedLabel = stringResource(if (group.collapsed)
                            R.string.other_homework_group_collapsed else R.string.other_homework_group_expanded)
                        ZCard(onClick = { onEvent(HomeworkEvent.ToggleGroup(group.status)) }, tag = "Homework.Group.${group.status}",
                            modifier = Modifier.fillMaxWidth().appear(cascade["g-${group.status}"] ?: 0)
                                .semantics { stateDescription = expandedLabel }) {
                            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                                Text(group.title, style = Zapara.typography.bodyStrong,
                                    color = c.text1, modifier = Modifier.weight(1f))
                                Text(group.items.size.toString(), style = Zapara.typography.caption, color = c.text2)
                                Icon(painterResource(R.drawable.ic_chevron_right), contentDescription = null,
                                    tint = c.text2, modifier = Modifier.size(20.dp).rotate(if (group.collapsed) 0f else 90f))
                            }
                        }
                    }
                    if (!group.collapsed) {
                        items(group.items, key = { it.id }) { item ->
                            val burning = group.status == GroupStatus.Burning && !item.done
                            ZCard(
                                onClick = { onEvent(HomeworkEvent.Edit(item.id)) },
                                onLongClick = { onEvent(HomeworkEvent.AskDelete(item.id)) },
                                tag = "Homework.Row.${item.id}",
                                modifier = Modifier.fillMaxWidth().appear(cascade["i-${item.id}"] ?: 0)
                            ) {
                                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                                        Text(item.subject, style = Zapara.typography.bodyStrong, color = c.text1)
                                        Text(item.text, style = Zapara.typography.body, color = if (item.done) c.text2 else c.text1, textDecoration = if (item.done) TextDecoration.LineThrough else null)
                                        Text(item.dueLabel, style = Zapara.typography.body, color = c.text1, modifier = Modifier.fillMaxWidth().testTag("Homework.Due.${item.id}"))
                                        Text(item.statusLabel, style = Zapara.typography.caption, color = if (burning) c.warn else c.text2, modifier = Modifier.fillMaxWidth().testTag("Homework.Status.${item.id}"))
                                        if (item.id in state.personalBusyIds) Text(stringResource(R.string.ux60_saving),
                                            style = Zapara.typography.caption, color = c.text2)
                                        if (item.files.isNotEmpty()) {
                                            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                                                verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                                                item.files.forEach { file ->
                                                    ZChip(file.name, onClick = { onEvent(HomeworkEvent.OpenFile(item.id, file.id)) }, tag = "Homework.File.${item.id}.${file.id}")
                                                }
                                            }
                                        }
                                    }
                                }
                                HorizontalDivider(color = c.line)
                                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                                    Text(stringResource(R.string.polish_homework_completion),
                                        style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
                                    ZIconButton(R.drawable.ic_pencil,
                                        stringResource(R.string.ux_homework_edit_label, item.subject, item.text),
                                        { onEvent(HomeworkEvent.Edit(item.id)) }, "Homework.Edit.${item.id}")
                                    val completionLabel = stringResource(R.string.hw_completion_label, item.subject, item.text)
                                    ZSwitch(item.done, { if (item.id !in state.personalBusyIds)
                                        onEvent(HomeworkEvent.ToggleDone(item.id)) }, "Homework.Done.${item.id}",
                                        Modifier.semantics { contentDescription = completionLabel })
                                }
                            }
                        }
                    }
                }
                if (!state.guest) {
                    if (state.sharedLoading) item("shared-loading") {
                        Text(stringResource(R.string.uxnext_homework_shared_loading),
                            style = Zapara.typography.caption, color = c.text2)
                    }
                    state.sharedError?.let { error -> item("shared-error") {
                        ZCard(Modifier.fillMaxWidth(), tag = "Homework.SharedError") {
                            Text(error, style = Zapara.typography.body, color = c.text1)
                            ZButton(stringResource(R.string.repeat), { onEvent(HomeworkEvent.RetryShared) }, ghost = true)
                        }
                    } }
                    if (visibleShared.isNotEmpty()) item("shared-heading") {
                        Text(stringResource(R.string.uxnext_homework_shared_title),
                            style = Zapara.typography.section, color = c.text1)
                    }
                    items(visibleShared, key = { "shared-${it.id}" }) { row ->
                        ZCard(Modifier.fillMaxWidth(), tag = "Homework.Shared.${row.id}") {
                            Text(row.title, style = Zapara.typography.bodyStrong, color = c.text1)
                            Text(row.body, style = Zapara.typography.body, color = if (row.completed) c.text2 else c.text1,
                                textDecoration = if (row.completed) TextDecoration.LineThrough else null)
                            Text(row.deadlineLabel, style = Zapara.typography.caption, color = c.text2)
                            Text(stringResource(if (row.audienceSelected) R.string.homework_audience_selected
                                else R.string.homework_audience_all), style = Zapara.typography.caption, color = c.text2)
                            if (row.canComplete) Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                                Text(stringResource(R.string.polish_homework_completion),
                                    style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
                                ZSwitch(row.completed, { if (row.id !in state.sharedBusyIds)
                                    onEvent(HomeworkEvent.ToggleShared(row.id)) },
                                    "Homework.SharedDone.${row.id}",
                                    Modifier.semantics { contentDescription = row.title })
                            }
                        }
                    }
                }
            }
            }
        }
        if (state.undoDone != null && state.hasGroup) {
            ZCard(Modifier.align(Alignment.BottomCenter).fillMaxWidth()
                .padding(horizontal = Zapara.space.l, vertical = Zapara.space.s), tag = "Homework.UndoBar") {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    Text(stringResource(R.string.homework_browse_undo_hint), style = Zapara.typography.caption,
                        color = c.text2, modifier = Modifier.weight(1f))
                    ZButton(stringResource(R.string.homework_browse_undo), { onEvent(HomeworkEvent.UndoDone) },
                        ghost = true, enabled = !state.undoDoneBusy, tag = "Homework.Undo")
                }
            }
        }
        }
    }
    state.subjectPicker?.let {
        SubjectPickerSheet(it, { onEvent(HomeworkEvent.Query(it)) }, { onEvent(HomeworkEvent.PickSubject(it)) },
            { onEvent(HomeworkEvent.ClosePicker) }, { raw -> onEvent(HomeworkEvent.PickManualSubject(raw)) })
    }
    state.editor?.let { editor ->
        HomeworkEditorSheet(
            editor,
            { onEvent(HomeworkEvent.EditorText(it)) },
            { onEvent(HomeworkEvent.Inc) },
            { onEvent(HomeworkEvent.Dec) },
            { onEvent(HomeworkEvent.Save) },
            { onEvent(HomeworkEvent.Cancel) },
            { kind, uri -> onEvent(HomeworkEvent.Attach(kind, uri)) },
            { onEvent(HomeworkEvent.RemoveFile(it)) },
            { onEvent(HomeworkEvent.EditorShare(it)) },
            onAudience = { onEvent(HomeworkEvent.EditorAudience(it)) },
            onRetryShare = { onEvent(HomeworkEvent.RetryShare) },
            isGuest = state.guest, onRecalculate = { onEvent(HomeworkEvent.Recalculate) },
            onRetryShareOptions = { onEvent(HomeworkEvent.RetryShareOptions) }
        )
    }
    state.confirmDelete?.let {
        AlertDialog(
            onDismissRequest = { onEvent(HomeworkEvent.CancelDelete) },
            title = { Text(stringResource(R.string.hw_delete_title), style = Zapara.typography.section) },
            text = { state.deleteError?.let { Text(it, color = c.bad, style = Zapara.typography.body) } },
            confirmButton = { ZButton(stringResource(R.string.delete), { onEvent(HomeworkEvent.ConfirmDelete) },
                enabled = !state.deleteBusy, busy = state.deleteBusy) },
            dismissButton = { ZButton(stringResource(R.string.theme_cancel), { onEvent(HomeworkEvent.CancelDelete) },
                ghost = true, enabled = !state.deleteBusy) },
            containerColor = Zapara.colors.card
        )
    }
}
