package ru.bgtu_voenmeh.zapara.ui.teachers

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear

@Composable
fun TeachersSection(state: TeachersUiState, onEvent: (TeachersEvent) -> Unit) {
    if (state.selected != null) {
        TeacherScreen(state, onEvent)
        return
    }
    val c = Zapara.colors
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_teachers))
        OutlinedTextField(
            value = state.query, onValueChange = { onEvent(TeachersEvent.Query(it)) },
            modifier = Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l).testTag("Teachers.Search"),
            placeholder = { Text(stringResource(R.string.teachers_search_hint), style = Zapara.typography.caption, color = c.text3) },
            singleLine = true,
            shape = RoundedCornerShape(Zapara.radii.control),
            colors = OutlinedTextFieldDefaults.colors(
                focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
                focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
                focusedTextColor = c.text1, unfocusedTextColor = c.text1
            )
        )
        Row(Modifier.fillMaxWidth().padding(Zapara.space.l), verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.teachers_only_mine), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
            ZSwitch(state.onlyMine, { onEvent(TeachersEvent.OnlyMine(it)) }, "Teachers.OnlyMine")
        }
        Text(stringResource(R.string.teachers_found, state.list.size, state.total), style = Zapara.typography.caption, color = c.text2, modifier = Modifier.padding(horizontal = Zapara.space.l))
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            itemsIndexed(state.list, key = { _, it -> it.id }) { index, row ->
                ZCard(onClick = { onEvent(TeachersEvent.Open(row.id)) }, tag = "Teachers.Row.${row.id}", modifier = Modifier.fillMaxWidth().appear(index)) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        if (row.isMine) Box(Modifier.size(6.dp).clip(CircleShape).background(c.text1))
                        Column(Modifier.weight(1f)) {
                            Text(row.name, style = Zapara.typography.bodyStrong, color = c.text1)
                            Text(row.subjects, style = Zapara.typography.caption, color = c.text2)
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun TeacherScreen(state: TeachersUiState, onEvent: (TeachersEvent) -> Unit) {
    val selected = state.selected ?: return
    val c = Zapara.colors
    BackHandler { onEvent(TeachersEvent.Back) }
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.s), verticalAlignment = Alignment.CenterVertically) {
            ZIconButton(R.drawable.ic_chevron_left, stringResource(R.string.teachers_back), { onEvent(TeachersEvent.Back) }, "Teacher.Back")
            Text(selected.name, style = Zapara.typography.title, color = c.text1, modifier = Modifier.weight(1f))
        }
        ZSegmented(
            listOf(stringResource(R.string.parity_both), stringResource(R.string.parity_odd_short), stringResource(R.string.parity_even_short)),
            state.parityFilter, { onEvent(TeachersEvent.Parity(it)) }, "Teacher.Segment",
            Modifier.padding(horizontal = Zapara.space.l)
        )
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            itemsIndexed(state.details, key = { _, it -> it.dow }) { index, day ->
                ZCard(Modifier.fillMaxWidth().appear(index)) {
                    Text(day.title, style = Zapara.typography.section, color = c.text1)
                    day.rows.forEach { row ->
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            if (row.isMyGroup) Box(Modifier.size(6.dp).clip(CircleShape).background(c.text1)) else Spacer(Modifier.size(6.dp))
                            Text(row.time, style = Zapara.typography.caption, color = c.text2)
                            Column(Modifier.weight(1f)) {
                                Text(row.subject, style = Zapara.typography.body, color = c.text1)
                                Text(row.groups, style = Zapara.typography.caption, color = c.text2)
                            }
                            Text(row.room, style = Zapara.typography.caption, color = c.text2)
                        }
                    }
                }
            }
        }
    }
}
