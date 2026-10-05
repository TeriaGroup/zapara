package ru.bgtu_voenmeh.zapara.ui.teachers

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Icon
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.LocalUiCopy
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear

@Composable
fun TeachersSection(state: TeachersUiState, onEvent: (TeachersEvent) -> Unit) =
    TeachersSection(state, onEvent, { _, _, _ -> })

@Composable
fun TeachersSection(state: TeachersUiState, onEvent: (TeachersEvent) -> Unit,
    onOpenOwnDay: (LocalDate, String, String) -> Unit,
    onOpenMap: (String, String, String) -> Unit = { _, _, _ -> }) {
    if (state.selected != null) {
        TeacherScreen(state, onEvent, onOpenOwnDay, onOpenMap)
        return
    }
    val c = Zapara.colors
    val keyboard = LocalSoftwareKeyboardController.current
    val onlyMineLabel = stringResource(R.string.teachers_only_mine)
    var department by rememberSaveable(state.groupId, state.profileName) { mutableStateOf("") }
    var departmentMenu by rememberSaveable(state.groupId, state.profileName) { mutableStateOf(false) }
    val departments = state.list.map { it.department.trim() }.filter { it.isNotEmpty() }.distinct().sorted()
    val activeDepartment = department.takeIf { it in departments }.orEmpty()
    val visibleTeachers = if (activeDepartment.isEmpty()) state.list
        else state.list.filter { it.department.trim() == activeDepartment }
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_teachers))
        Column(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        ZTextField(
            value = state.query, onValueChange = { onEvent(TeachersEvent.Query(it)) },
            modifier = Modifier.fillMaxWidth().testTag("Teachers.Search"),
            placeholder = { Text(stringResource(R.string.teachers_search_hint), style = Zapara.typography.caption, color = c.text3) },
            singleLine = true,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { keyboard?.hide() }),
            leadingIcon = { Icon(painterResource(R.drawable.ic_search), null, Modifier.size(24.dp), tint = c.text2) }
        )
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.teachers_only_mine), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
            ZSwitch(state.onlyMine, { onEvent(TeachersEvent.OnlyMine(it)) }, "Teachers.OnlyMine",
                Modifier.semantics { contentDescription = onlyMineLabel })
        }
        if (departments.size > 1) Box(Modifier.fillMaxWidth()) {
            ZButton(if (activeDepartment.isEmpty()) stringResource(R.string.ux300_android_all_departments)
                else activeDepartment, { departmentMenu = true }, ghost = activeDepartment.isEmpty(),
                modifier = Modifier.fillMaxWidth(), tag = "Teachers.Department", startAligned = true,
                trailingIcon = R.drawable.ic_chevron_right, trailingIconRotation = 90f)
            androidx.compose.material3.DropdownMenu(expanded = departmentMenu,
                onDismissRequest = { departmentMenu = false }) {
                androidx.compose.material3.DropdownMenuItem(
                    text = { Text(stringResource(R.string.ux300_android_all_departments),
                        style = Zapara.typography.body, color = c.text1) },
                    onClick = { department = ""; departmentMenu = false })
                departments.forEach { name -> androidx.compose.material3.DropdownMenuItem(
                    text = { Text(name, style = Zapara.typography.body, color = c.text1) },
                    onClick = { department = name; departmentMenu = false }) }
            }
        }
        }
        if (!state.loaded) {
            Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
            return
        }
        state.loadError?.let { error ->
            ZCard(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l), tag = "Teachers.LoadError") {
                Text(error, style = Zapara.typography.body, color = c.text1)
                ZButton(stringResource(R.string.repeat), { onEvent(TeachersEvent.Retry) }, ghost = true)
            }
        }
        val currentResults = state.appliedQuery == state.query && state.appliedOnlyMine == state.onlyMine
        val resultsLabel = if (state.searching) stringResource(R.string.ux60_teacher_searching)
            else if (currentResults) stringResource(R.string.teachers_found, visibleTeachers.size, state.total)
            else stringResource(R.string.ux60_teacher_search_failed)
        if (LocalDensity.current.fontScale >= 1.5f) {
            Column(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Text(resultsLabel, style = Zapara.typography.caption, color = c.text2,
                    modifier = Modifier.fillMaxWidth())
                if (state.query.isNotBlank()) ZButton(stringResource(R.string.next_teachers_clear),
                    { onEvent(TeachersEvent.Query("")) }, ghost = true, tag = "Teachers.ClearSearch")
            }
        } else {
        Row(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Text(resultsLabel,
                style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
            if (state.query.isNotBlank()) ZButton(stringResource(R.string.next_teachers_clear),
                { onEvent(TeachersEvent.Query("")) }, ghost = true, tag = "Teachers.ClearSearch")
        }
        }
        if (state.searching) {
            Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
            return@Column
        }
        if (!currentResults) return@Column
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            if (visibleTeachers.isEmpty() && (state.query.isNotBlank() || activeDepartment.isNotEmpty()) && state.loadError == null) item("no-results") {
                ZCard(Modifier.fillMaxWidth(), tag = "Empty.TeacherSearch") {
                    Text(stringResource(R.string.next_teachers_no_results),
                        style = Zapara.typography.body, color = c.text2)
                    if (state.onlyMine) ZButton(stringResource(R.string.ux30_teachers_search_all),
                        { onEvent(TeachersEvent.OnlyMine(false)) }, ghost = true, tag = "Teachers.SearchAll")
                    if (activeDepartment.isNotEmpty()) ZButton(stringResource(R.string.ux300_android_all_departments),
                        { department = "" }, ghost = true, tag = "Teachers.ClearDepartment")
                    ZButton(stringResource(R.string.next_teachers_clear),
                        { onEvent(TeachersEvent.Query("")) }, ghost = true)
                }
            }
            if (visibleTeachers.isEmpty() && state.query.isBlank() && activeDepartment.isEmpty() && state.loadError == null) item("empty") {
                ZCard(Modifier.fillMaxWidth(), tag = "Empty.Teachers") {
                    Text(stringResource(if (state.onlyMine) R.string.next_teachers_my_empty else R.string.next_teachers_empty),
                        style = Zapara.typography.body, color = c.text2)
                    if (state.onlyMine) ZButton(stringResource(R.string.next_teachers_show_all),
                        { onEvent(TeachersEvent.OnlyMine(false)) }, ghost = true)
                }
            }
            itemsIndexed(visibleTeachers, key = { _, it -> it.id }) { index, row ->
                ZCard(onClick = { onEvent(TeachersEvent.Open(row.id)) }, tag = "Teachers.Row.${row.id}", modifier = Modifier.fillMaxWidth().appear(index)) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                            Text(row.name, style = Zapara.typography.bodyStrong, color = c.text1)
                            if (row.isMine) ZChip(stringResource(R.string.teacher_my_group),
                                tag = "Teachers.Mine.${row.id}")
                            Text(row.subjects, style = Zapara.typography.caption, color = c.text2)
                            if (row.department.isNotBlank()) Text(stringResource(R.string.ux60_teacher_department,
                                row.department), style = Zapara.typography.caption, color = c.text2)
                        }
                        Icon(painterResource(R.drawable.ic_chevron_right), null,
                            Modifier.size(24.dp), tint = c.text2)
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun TeacherScreen(state: TeachersUiState, onEvent: (TeachersEvent) -> Unit,
    onOpenOwnDay: (LocalDate, String, String) -> Unit = { _, _, _ -> },
    onOpenMap: (String, String, String) -> Unit = { _, _, _ -> }) {
    val selected = state.selected ?: return
    val c = Zapara.colors
    val copy = LocalUiCopy.current
    var myGroupOnly by rememberSaveable(selected.id, state.groupId, state.profileName) { mutableStateOf(false) }
    val visibleDays = if (myGroupOnly) state.details.mapNotNull { day ->
        day.copy(rows = day.rows.filter { it.isMyGroup }).takeIf { it.rows.isNotEmpty() }
    } else state.details
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
            if (selected.department.isNotBlank()) Text(stringResource(R.string.ux60_teacher_department,
                selected.department), modifier = Modifier.padding(horizontal = Zapara.space.l),
                style = Zapara.typography.caption, color = c.text2)
        }
        item(key = "filters") {
            val parityLabels = listOf(stringResource(R.string.parity_both),
                stringResource(R.string.teacher_filter_odd), stringResource(R.string.teacher_filter_even))
            if (LocalDensity.current.fontScale >= 1.5f) {
                FlowRow(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l).testTag("Teacher.Segment"),
                    horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    parityLabels.forEachIndexed { index, label ->
                        ZChip(label, selected = index == state.parityFilter,
                            onClick = { onEvent(TeachersEvent.Parity(index)) }, tag = "Teacher.Segment.$index")
                    }
                }
            } else ZSegmented(parityLabels, state.parityFilter, { onEvent(TeachersEvent.Parity(it)) },
                "Teacher.Segment", Modifier.padding(horizontal = Zapara.space.l))
        }
        item(key = "count") {
            FlowRow(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l),
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                Text(stringResource(R.string.teacher_detail_lessons, visibleDays.sumOf { it.rows.size }),
                style = Zapara.typography.caption, color = c.text2,
                    modifier = Modifier.align(Alignment.CenterVertically))
                if (state.groupId.isNotBlank()) ZChip(stringResource(R.string.ux100_study_teacher_my_group_only),
                    selected = myGroupOnly, onClick = { myGroupOnly = !myGroupOnly },
                    tag = "Teacher.MyGroupOnly")
            }
        }
        if (state.loadError != null) item(key = "error") {
            ZCard(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l), tag = "Teacher.LoadError") {
                Text(state.loadError, style = Zapara.typography.body, color = c.text1)
                ZButton(stringResource(R.string.repeat), { onEvent(TeachersEvent.Retry) }, ghost = true)
            }
        }
        if (state.detailsLoading) item(key = "loading") {
            Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
        } else if (visibleDays.isEmpty() && state.loadError == null) item(key = "empty") {
            ZCard(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l), tag = "Teacher.EmptyWeek") {
                Text(stringResource(if (myGroupOnly) R.string.ux100_study_teacher_no_my_group
                    else R.string.ux30_teacher_no_week), style = Zapara.typography.body, color = c.text2)
                if (myGroupOnly) ZButton(stringResource(R.string.ux100_study_teacher_show_all_groups),
                    { myGroupOnly = false }, ghost = true, tag = "Teacher.ShowAllGroups")
                if (state.parityFilter != 0) ZButton(stringResource(R.string.ux30_study_teacher_all_weeks),
                    { onEvent(TeachersEvent.Parity(0)) }, ghost = true, tag = "Teacher.ShowAllWeeks")
            }
        }
            itemsIndexed(visibleDays, key = { _, it -> it.dow }) { index, day ->
                var collapsed by rememberSaveable(selected.id, state.parityFilter, day.dow) { mutableStateOf(false) }
                ZCard(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l).appear(index), tag = "Teacher.Day.${day.dow}") {
                    FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        Text(day.title, style = Zapara.typography.section, color = c.text1)
                        day.date?.let { date -> ZChip(date.format(DateTimeFormatter.ofPattern("d MMM", Locale("ru"))),
                            tag = "Teacher.Date.${day.dow}") }
                        ZChip(stringResource(R.string.polish_teacher_day_count, day.rows.size))
                    }
                    ZButton(stringResource(if (collapsed) R.string.ux300_android_week_expand
                        else R.string.ux300_android_week_collapse), { collapsed = !collapsed },
                        ghost = true, tag = "Teacher.Collapse.${day.dow}")
                    if (!collapsed) day.rows.forEachIndexed { rowIndex, row ->
                        if (rowIndex > 0) HorizontalDivider(color = c.line)
                        Column(Modifier.fillMaxWidth().testTag("Teacher.Row.${day.dow}.$rowIndex")
                            .padding(vertical = Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                            Text(row.time, style = Zapara.typography.bodyStrong, color = c.text1)
                                Text(row.subject, style = Zapara.typography.bodyStrong, color = c.text1)
                                Text(row.groups, style = Zapara.typography.caption, color = c.text2)
                            Text(row.room, style = Zapara.typography.caption, color = c.text2)
                            Text(TeacherDetailsComposer.parityLabel(row.parity, copy),
                                style = Zapara.typography.caption, color = c.text1,
                                modifier = Modifier.testTag("Teacher.Parity.${day.dow}.$rowIndex"))
                            if (row.isMyGroup) Text(stringResource(R.string.teacher_my_group),
                                style = Zapara.typography.caption, color = c.text1,
                                modifier = Modifier.testTag("Teacher.MyGroup.${day.dow}.$rowIndex"))
                            row.date?.takeIf { row.isMyGroup && state.groupId.isNotBlank() }?.let { date ->
                                ZButton(stringResource(R.string.uxnext_teacher_open_day),
                                    { onOpenOwnDay(date, state.groupId, state.profileName) },
                                    ghost = true, tag = "Teacher.OpenDay.${day.dow}.$rowIndex")
                            }
                            if (row.isMyGroup && row.classroomRaw.isNotBlank() &&
                                runCatching { ru.bgtu_voenmeh.zapara.data.MapResolve.resolve(row.classroomRaw)?.hasMap == true }
                                    .getOrDefault(false))
                                ZButton(stringResource(R.string.ux300_android_teacher_open_map),
                                    { onOpenMap(row.classroomRaw, state.groupId, state.profileName) },
                                    ghost = true, tag = "Teacher.OpenMap.${day.dow}.$rowIndex")
                        }
                    }
                }
            }
    }
}
