package ru.bgtu_voenmeh.zapara.ui.shell

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.ui.components.HighlightText
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIcon

@Composable
fun SectionsSheet(current: Section, onPick: (Section) -> Unit, onDismiss: () -> Unit) {
    val c = Zapara.colors
    val grid = Section.sheet.filter { it != Section.Settings }
    ZBottomSheet(onDismiss = onDismiss, tag = "Sheet.Sections") {
        Text(stringResource(R.string.sections_title), style = Zapara.typography.caption, color = c.text2)
        Spacer(Modifier.height(Zapara.space.s))
        LazyVerticalGrid(
            columns = GridCells.Fixed(2),
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s),
            modifier = Modifier.heightIn(max = 280.dp)
        ) {
            items(grid, key = { it.route }) { section ->
                SectionCard(section, current == section) { onPick(section) }
            }
        }
        Spacer(Modifier.height(Zapara.space.s))
        SectionCard(Section.Settings, current == Section.Settings) {
            onPick(Section.Settings)
        }
    }
}

@Composable
private fun SectionCard(section: Section, active: Boolean, onClick: () -> Unit) {
    val c = Zapara.colors
    ZCard(
        Modifier
            .fillMaxWidth()
            .testTag(section.tag)
            .clickable(onClick = onClick)
    ) {
        Row(
            Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)
        ) {
            ZIcon(section.icon, stringResource(section.title), Modifier.size(20.dp))
            Text(
                stringResource(section.title),
                style = Zapara.typography.bodyStrong,
                color = c.text1,
                modifier = Modifier.weight(1f)
            )
            if (active) {
                Box(Modifier.size(6.dp).clip(CircleShape).background(c.text1))
            }
        }
    }
}

@Composable
fun GroupPickerSheet(
    groups: List<GroupInfo>,
    currentId: String?,
    onPick: (String) -> Unit,
    onDismiss: () -> Unit
) {
    val c = Zapara.colors
    var query by remember { mutableStateOf("") }
    val focus = remember { FocusRequester() }
    LaunchedEffect(Unit) { focus.requestFocus() }
    val filtered = remember(groups, query) {
        val q = query.trim()
        if (q.isEmpty()) groups else groups.filter { it.name.contains(q, ignoreCase = true) || it.id.contains(q, ignoreCase = true) }
    }
    ZBottomSheet(onDismiss = onDismiss, tag = "Sheet.GroupPicker") {
        Text(stringResource(R.string.group_pick), style = Zapara.typography.section, color = c.text1)
        Spacer(Modifier.height(Zapara.space.s))
        OutlinedTextField(
            value = query,
            onValueChange = { query = it },
            modifier = Modifier.fillMaxWidth().testTag("Picker.Search").focusRequester(focus),
            placeholder = { Text(stringResource(R.string.group_search), style = Zapara.typography.caption, color = c.text3) },
            singleLine = true,
            shape = RoundedCornerShape(Zapara.radii.control),
            colors = OutlinedTextFieldDefaults.colors(
                focusedContainerColor = c.chip,
                unfocusedContainerColor = c.chip,
                focusedBorderColor = c.lineStrong,
                unfocusedBorderColor = c.chip,
                focusedTextColor = c.text1,
                unfocusedTextColor = c.text1
            )
        )
        Spacer(Modifier.height(Zapara.space.s))
        LazyColumn(
            modifier = Modifier.fillMaxWidth().heightIn(min = 160.dp, max = 420.dp),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
        ) {
            items(filtered, key = { it.id }) { group ->
                ZCard(
                    Modifier
                        .fillMaxWidth()
                        .testTag("Picker.Row.${group.id}")
                        .clickable { onPick(group.id) }
                ) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        HighlightText(group.name, query, Zapara.typography.bodyStrong, Modifier.weight(1f))
                        if (group.id == currentId) {
                            ZIcon(R.drawable.ic_check, group.name, Modifier.size(20.dp))
                        }
                    }
                }
            }
        }
    }
}
