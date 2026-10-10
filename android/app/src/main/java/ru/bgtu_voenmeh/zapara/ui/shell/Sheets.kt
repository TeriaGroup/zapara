package ru.bgtu_voenmeh.zapara.ui.shell

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import ru.bgtu_voenmeh.zapara.ui.components.rememberUiText
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.Role
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.ui.components.HighlightText
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIcon

@Composable
fun SectionsSheet(current: Section, onPick: (Section) -> Unit, onDismiss: () -> Unit) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    var query by rememberSaveable { mutableStateOf("") }
    val sectionGroups = listOf(
        uiText(R.string.space_day_167) to listOf(Section.Schedule, Section.Week, Section.Homework, Section.Maps, Section.Teachers, Section.Summary),
        uiText(R.string.space_day_168) to listOf(Section.Group, Section.Chat, Section.Friends, Section.Community),
        uiText(R.string.space_day_169) to listOf(Section.Settings)
    )
    val titles = Section.entries.associateWith { stringResource(it.title) }
    val descriptions = mapOf(Section.Friends to stringResource(R.string.ux100_common_description_friends),
        Section.Group to stringResource(R.string.ux100_common_description_group),
        Section.Community to stringResource(R.string.ux100_common_description_community),
        Section.Chat to stringResource(R.string.ux100_common_description_chat))
    val words = query.trim().lowercase().replace('ё', 'е').split(Regex("\\s+")).filter(String::isNotBlank)
    val visibleGroups = sectionGroups.map { (name, sections) ->
        name to sections.filter { item -> words.all { word ->
            (titles.getValue(item) + " " + descriptions[item].orEmpty()).lowercase().replace('ё', 'е').contains(word) } }
    }.filter { it.second.isNotEmpty() }
    ZBottomSheet(onDismiss = onDismiss, tag = "Sheet.Sections") {
        val focus = LocalFocusManager.current
        Text(stringResource(R.string.sections_title), style = Zapara.typography.section, color = c.text1)
        Spacer(Modifier.height(Zapara.space.s))
        ZTextField(query, { query = it }, modifier = Modifier.fillMaxWidth().testTag("Sections.Search"),
            placeholder = { Text(stringResource(R.string.ux100_common_section_search)) }, singleLine = true,
            leadingIcon = { ZIcon(R.drawable.ic_search, null) },
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { focus.clearFocus() }),
            trailingIcon = if (query.isNotEmpty()) {{ ZIconButton(R.drawable.ic_x,
                stringResource(R.string.ux100_common_clear_search), { query = "" }, "Sections.ClearSearch") }} else null)
        Spacer(Modifier.height(Zapara.space.s))
        LazyColumn(Modifier.fillMaxWidth().weight(1f, fill = false).heightIn(max = 600.dp).clipToBounds(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            if (visibleGroups.isEmpty()) item {
                Text(stringResource(R.string.ux100_common_section_empty), color = c.text2)
                ZButton(stringResource(R.string.ux100_common_clear_search), { query = "" }, ghost = true)
            }
            visibleGroups.forEach { (title, sections) ->
                item { Text(title, style = Zapara.typography.caption, color = c.text2, modifier = Modifier.padding(top = Zapara.space.m)) }
                items(sections, key = { it.route }) { section -> SectionCard(section, current == section) { onPick(section) } }
            }
        }
    }
}

@Composable
private fun SectionCard(section: Section, active: Boolean, onClick: () -> Unit) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    ZCard(
        Modifier
            .fillMaxWidth()
            .testTag(section.tag)
            .heightIn(min = Zapara.space.minTouch) // #105 / AN-18
            .semantics { selected = active }
            .clickable(role = Role.Button, onClick = onClick)
    ) {
        Row(
            Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)
        ) {
            ZIcon(section.icon, null, Modifier.size(20.dp))
            Column(Modifier.weight(1f)) {
                Text(stringResource(section.title), style = Zapara.typography.bodyStrong, color = c.text1)
                val description = when(section) {
                    Section.Friends -> R.string.ux100_common_description_friends
                    Section.Group -> R.string.ux100_common_description_group
                    Section.Community -> R.string.ux100_common_description_community
                    Section.Chat -> R.string.ux100_common_description_chat
                    else -> null
                }
                description?.let { Text(stringResource(it), style = Zapara.typography.caption, color = c.text2) }
            }
            ZIcon(if (active) R.drawable.ic_check else R.drawable.ic_chevron_right, null,
                Modifier.size(20.dp))
        }
    }
}

@Composable
fun GroupPickerSheet(
    groups: List<GroupInfo>,
    currentId: String?,
    onPick: (String) -> Unit,
    onDismiss: () -> Unit,
    busy: Boolean = false,
    error: String? = null,
    onRetry: () -> Unit = {}
) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    var query by rememberSaveable(currentId) { mutableStateOf("") }
    val filtered = remember(groups, query) {
        searchGroups(groups, query)
    }
    val current = groups.firstOrNull { it.id == currentId }
    ZBottomSheet(onDismiss = onDismiss, tag = "Sheet.GroupPicker", canDismiss = { !busy }) {
        val focus = LocalFocusManager.current
        Text(stringResource(R.string.group_pick), style = Zapara.typography.section, color = c.text1)
        LazyColumn(Modifier.fillMaxWidth().weight(1f, fill = false),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item("controls") { Column(Modifier.fillMaxWidth()) {
        current?.let { Text(stringResource(R.string.ux100_common_current_group, it.name),
            style = Zapara.typography.caption, color = c.text2) }
        if (busy) Text(stringResource(R.string.ux60_group_pick_saving),
            style = Zapara.typography.caption, color = c.text2)
        if (error != null) {
            Text(error, style = Zapara.typography.caption, color = c.bad)
            ZButton(stringResource(R.string.repeat), onRetry, ghost = true,
                enabled = !busy, tag = "Picker.RetryGroup")
        }
        Spacer(Modifier.height(Zapara.space.s))
        ZTextField(
            value = query,
            onValueChange = { query = it },
            modifier = Modifier.fillMaxWidth().testTag("Picker.Search"),
            placeholder = { Text(stringResource(R.string.group_search)) },
            leadingIcon = { ZIcon(R.drawable.ic_search, null) },
            singleLine = true,
            enabled = !busy,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { focus.clearFocus() }),
            trailingIcon = if (query.isNotEmpty()) {{ ZIconButton(R.drawable.ic_x,
                stringResource(R.string.ux100_common_clear_search), { query = "" }, "Picker.ClearQuery", enabled = !busy) }} else null
        )
        Spacer(Modifier.height(Zapara.space.s))
        Text(stringResource(R.string.ux100_common_groups_count, filtered.size),
            style = Zapara.typography.caption, color = c.text2)
        } }
            if (filtered.isEmpty()) {
                item {
                    Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        Text(
                            when {
                                groups.isEmpty() -> stringResource(R.string.group_empty)
                                query.isNotBlank() -> stringResource(R.string.group_search_empty)
                                else -> stringResource(R.string.group_empty_hint)
                            },
                            style = Zapara.typography.body,
                            color = c.text2
                        )
                        if (groups.isNotEmpty() && query.isNotBlank()) {
                            ZButton(
                                stringResource(R.string.group_search_clear),
                                { query = "" },
                                ghost = true,
                                tag = "Picker.ClearSearch"
                            )
                        }
                    }
                }
            }
            items(if (query.isBlank() && current != null) listOf(current) + filtered.filterNot { it.id == currentId } else filtered,
                key = { it.id }) { group ->
                ZCard(
                    Modifier
                        .fillMaxWidth()
                        .testTag("Picker.Row.${group.id}")
                        .heightIn(min = Zapara.space.minTouch) // #105 / AN-19
                        .semantics { selected = group.id == currentId }
                        .clickable(enabled = !busy, role = Role.Button) { onPick(group.id) }
                ) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        HighlightText(group.name, query, Zapara.typography.bodyStrong, Modifier.weight(1f))
                        if (group.id == currentId) {
                            ZIcon(R.drawable.ic_check, null, Modifier.size(20.dp))
                        }
                    }
                }
            }
        }
    }
}
