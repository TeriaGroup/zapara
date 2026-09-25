package ru.bgtu_voenmeh.zapara.ui.teachers

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
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
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.LocalUiCopy
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
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
    val onlyMineLabel = stringResource(R.string.teachers_only_mine)
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
            ZSwitch(state.onlyMine, { onEvent(TeachersEvent.OnlyMine(it)) }, "Teachers.OnlyMine",
                Modifier.semantics { contentDescription = onlyMineLabel })
        }
        if (!state.loaded) {
            Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
            return
        }
        Row(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Text(stringResource(R.string.teachers_found, state.list.size, state.total),
                style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
            if (state.query.isNotBlank()) ZButton(stringResource(R.string.next_teachers_clear),
                { onEvent(TeachersEvent.Query("")) }, ghost = true, tag = "Teachers.ClearSearch")
        }
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            if (state.list.isEmpty() && state.query.isNotBlank()) item("no-results") {
                ZCard(Modifier.fillMaxWidth(), tag = "Empty.TeacherSearch") {
                    Text(stringResource(R.string.next_teachers_no_results),
                        style = Zapara.typography.body, color = c.text2)
                    ZButton(stringResource(R.string.next_teachers_clear),
                        { onEvent(TeachersEvent.Query("")) }, ghost = true)
                }
            }
            if (state.list.isEmpty() && state.query.isBlank()) item("empty") {
                ZCard(Modifier.fillMaxWidth(), tag = "Empty.Teachers") {
                    Text(stringResource(if (state.onlyMine) R.string.next_teachers_my_empty else R.string.next_teachers_empty),
                        style = Zapara.typography.body, color = c.text2)
                    if (state.onlyMine) ZButton(stringResource(R.string.next_teachers_show_all),
                        { onEvent(TeachersEvent.OnlyMine(false)) }, ghost = true)
                }
            }
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
    val copy = LocalUiCopy.current
    BackHandler { onEvent(TeachersEvent.Back) }
    LazyColumn(Modifier.fillMaxSize().testTag("Teacher.List"),
        contentPadding = PaddingValues(vertical = Zapara.space.s),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item(key = "header") {
            if (LocalDensity.current.fontScale >= 1.5f) {
                Column(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l)) {
                    ZIconButton(R.drawable.ic_chevron_left, stringResource(R.string.teachers_back), { onEvent(TeachersEvent.Back) }, "Teacher.Back")
                    Text(selected.name, style = Zapara.typography.title, color = c.text1)
                }
            } else {
                Row(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.s), verticalAlignment = Alignment.CenterVertically) {
                    ZIconButton(R.drawable.ic_chevron_left, stringResource(R.string.teachers_back), { onEvent(TeachersEvent.Back) }, "Teacher.Back")
                    Text(selected.name, style = Zapara.typography.title, color = c.text1, modifier = Modifier.weight(1f))
                }
            }
        }
        item(key = "filters") {
        ZSegmented(
            listOf(stringResource(R.string.parity_both), stringResource(R.string.teacher_filter_odd), stringResource(R.string.teacher_filter_even)),
            state.parityFilter, { onEvent(TeachersEvent.Parity(it)) }, "Teacher.Segment",
            Modifier.padding(horizontal = Zapara.space.l)
        )
        }
        item(key = "count") {
            Text(stringResource(R.string.teacher_detail_lessons, state.details.sumOf { it.rows.size }),
                style = Zapara.typography.caption, color = c.text2,
                modifier = Modifier.padding(horizontal = Zapara.space.l))
        }
            itemsIndexed(state.details, key = { _, it -> it.dow }) { index, day ->
                ZCard(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l).appear(index), tag = "Teacher.Day.${day.dow}") {
                    Text(day.title, style = Zapara.typography.section, color = c.text1)
                    day.rows.forEachIndexed { rowIndex, row ->
                        Column(Modifier.fillMaxWidth().testTag("Teacher.Row.${day.dow}.$rowIndex")
                            .padding(vertical = Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                            Text(row.time, style = Zapara.typography.caption, color = c.text2)
                                Text(row.subject, style = Zapara.typography.body, color = c.text1)
                                Text(row.groups, style = Zapara.typography.caption, color = c.text2)
                            Text(row.room, style = Zapara.typography.caption, color = c.text2)
                            Text(TeacherDetailsComposer.parityLabel(row.parity, copy),
                                style = Zapara.typography.caption, color = c.text1,
                                modifier = Modifier.testTag("Teacher.Parity.${day.dow}.$rowIndex"))
                            if (row.isMyGroup) Text(stringResource(R.string.teacher_my_group),
                                style = Zapara.typography.caption, color = c.text1,
                                modifier = Modifier.testTag("Teacher.MyGroup.${day.dow}.$rowIndex"))
                        }
                    }
                }
            }
    }
}
