package ru.bgtu_voenmeh.zapara.ui.homework

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear
import ru.bgtu_voenmeh.zapara.ui.theme.breath

@Composable
fun HomeworkSection(state: HomeworkUiState, onEvent: (HomeworkEvent) -> Unit) {
    val chrome = LocalShellChrome.current
    val c = Zapara.colors
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_homework)) {
            if (state.hasGroup) ZIconButton(R.drawable.ic_plus, stringResource(R.string.add), { onEvent(HomeworkEvent.Add) }, "Homework.Add")
        }
        when {
            !state.hasGroup -> EmptyState(R.drawable.ic_homework, stringResource(R.string.empty_no_group), stringResource(R.string.empty_no_group_hint), stringResource(R.string.group_pick), chrome.onGroupChip, "Empty.NoGroup")
            state.groups.isEmpty() -> EmptyState(R.drawable.ic_homework, stringResource(R.string.hw_empty_title), stringResource(R.string.hw_empty_hint), stringResource(R.string.add), { onEvent(HomeworkEvent.Add) }, "Empty.Homework")
            else -> {
                val cascade = HashMap<String, Int>()
                var n = 0
                state.groups.forEach { group ->
                    cascade["g-${group.status}"] = n++
                    if (!group.collapsed) group.items.forEach { cascade["i-${it.id}"] = n++ }
                }
                LazyColumn(
                Modifier.fillMaxSize(),
                contentPadding = PaddingValues(Zapara.space.l),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
            ) {
                state.groups.forEach { group ->
                    item("g-${group.status}") {
                        ZCard(onClick = { onEvent(HomeworkEvent.ToggleGroup(group.status)) }, tag = "Homework.Group.${group.status}", modifier = Modifier.fillMaxWidth().appear(cascade["g-${group.status}"] ?: 0)) {
                            Text("${group.title} · ${group.items.size}", style = Zapara.typography.caption, color = c.text2)
                        }
                    }
                    if (!group.collapsed) {
                        items(group.items, key = { it.id }) { item ->
                            val dueColor = when (group.status) {
                                GroupStatus.Overdue -> c.bad
                                GroupStatus.Burning -> c.warn
                                else -> c.text2
                            }
                            val burning = group.status == GroupStatus.Burning && !item.done
                            ZCard(
                                onClick = { onEvent(HomeworkEvent.Edit(item.id)) },
                                onLongClick = { onEvent(HomeworkEvent.AskDelete(item.id)) },
                                tag = "Homework.Row.${item.id}",
                                modifier = Modifier.fillMaxWidth().appear(cascade["i-${item.id}"] ?: 0)
                            ) {
                                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                                    if (burning) {
                                        Box(Modifier.size(8.dp).clip(CircleShape).background(c.warn).breath(true))
                                    }
                                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                                        Text(item.subject, style = Zapara.typography.bodyStrong, color = c.text1)
                                        Text(item.text, style = Zapara.typography.body, color = if (item.done) c.text2 else c.text1, textDecoration = if (item.done) TextDecoration.LineThrough else null)
                                        Text(item.dueLabel, style = Zapara.typography.caption, color = dueColor)
                                    }
                                    ZSwitch(item.done, { onEvent(HomeworkEvent.ToggleDone(item.id)) }, "Homework.Done.${item.id}")
                                }
                            }
                        }
                    }
                }
            }
            }
        }
    }
    state.subjectPicker?.let {
        SubjectPickerSheet(it, { onEvent(HomeworkEvent.Query(it)) }, { onEvent(HomeworkEvent.PickSubject(it)) }, { onEvent(HomeworkEvent.ClosePicker) })
    }
    state.editor?.let { editor ->
        HomeworkEditorSheet(editor, { onEvent(HomeworkEvent.EditorText(it)) }, { onEvent(HomeworkEvent.Inc) }, { onEvent(HomeworkEvent.Dec) }, { onEvent(HomeworkEvent.Save) }, { onEvent(HomeworkEvent.Cancel) })
    }
    state.confirmDelete?.let {
        AlertDialog(
            onDismissRequest = { onEvent(HomeworkEvent.CancelDelete) },
            title = { Text(stringResource(R.string.hw_delete_title), style = Zapara.typography.section) },
            confirmButton = { ZButton(stringResource(R.string.delete), { onEvent(HomeworkEvent.ConfirmDelete) }) },
            dismissButton = { ZButton(stringResource(R.string.theme_cancel), { onEvent(HomeworkEvent.CancelDelete) }, ghost = true) },
            containerColor = Zapara.colors.card
        )
    }
}
