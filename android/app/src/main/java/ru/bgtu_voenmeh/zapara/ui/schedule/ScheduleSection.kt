package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.animation.core.tween
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.background
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.width
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.PagerDefaults
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.DisposableEffect
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import androidx.compose.material3.Checkbox
import androidx.compose.foundation.clickable
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.components.rememberUiText
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.nestedscroll.NestedScrollConnection
import androidx.compose.ui.input.nestedscroll.NestedScrollSource
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.res.pluralStringResource
import ru.bgtu_voenmeh.zapara.ui.theme.ZIcon
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorSheet
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.Durations
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaEase
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.appear
import ru.bgtu_voenmeh.zapara.ui.gestures.plannerSwipe
import ru.bgtu_voenmeh.zapara.ui.theme.plannerContentReveal

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun ScheduleSection(state: ScheduleUiState, onEvent: (ScheduleEvent) -> Unit, onDiscuss: (String) -> Unit = {}, onWeek: (java.time.LocalDate) -> Unit = {}, onOpenMap: (String) -> Unit) {
    val uiText = rememberUiText()
    val chrome = LocalShellChrome.current
    val lifecycle = LocalLifecycleOwner.current
    DisposableEffect(lifecycle) {
        val observer = LifecycleEventObserver { _, event -> if (event == Lifecycle.Event.ON_RESUME) onEvent(ScheduleEvent.RefreshShared) }
        lifecycle.lifecycle.addObserver(observer)
        onDispose { lifecycle.lifecycle.removeObserver(observer) }
    }
    var restoredDate by rememberSaveable { mutableStateOf<String?>(null) }
    var didRestore by remember { mutableStateOf(false) }
    LaunchedEffect(state.loaded) {
        if (state.loaded && !didRestore) {
            didRestore = true
            val date = runCatching { java.time.LocalDate.parse(restoredDate ?: state.selected.toString()) }.getOrNull()
            if (date != null && date != state.selected) onEvent(ScheduleEvent.Select(date))
        }
    }
    LaunchedEffect(state.selected) { if (didRestore) restoredDate = state.selected.toString() }
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_schedule)) {
            ZButton(stringResource(R.string.nav_week), { onWeek(state.selected) }, ghost = true, quiet = true)
        }
        when (ScheduleComposer.pane(state)) {
            ScheduleComposer.SchedulePane.Loading -> Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
            ScheduleComposer.SchedulePane.LoadFail -> EmptyState(
                R.drawable.ic_alert,
                stringResource(R.string.load_fail),
                hint = state.error,
                actionText = stringResource(R.string.repeat),
                onAction = { onEvent(ScheduleEvent.Retry) },
                tag = "Empty.LoadFail"
            )
            ScheduleComposer.SchedulePane.NoGroup -> EmptyState(R.drawable.ic_calendar, stringResource(R.string.empty_no_group), stringResource(R.string.empty_no_group_hint), stringResource(R.string.group_pick), chrome.onGroupChip, "Empty.NoGroup")
            ScheduleComposer.SchedulePane.Day -> {
            val page = state.pages[state.selected]?.let { ScheduleComposer.atClock(it, state.now) }
            LaunchedEffect(state.selected) { onEvent(ScheduleEvent.Need(state.selected)) }
            BoxWithConstraints(Modifier.fillMaxSize()) {
                if (maxWidth >= 1100.dp && page != null) {
                    Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.l)) {
                        Column(Modifier.width(400.dp)) { DateStrip(state.selected, state.today, state.pages, visibleCount = 7, onQuickDay={ onEvent(ScheduleEvent.QuickDay(it)) }) { onEvent(ScheduleEvent.Select(it)) } }
                        Box(Modifier.weight(1f)
                            .plannerSwipe(state.selected) { onEvent(ScheduleEvent.Select(state.selected.plusDays(it.dayDelta))) }
                            .plannerContentReveal(state.selected)) { LessonList(page.copy(deadlines = emptyList()), state, onEvent, onOpenMap, onDiscuss) }
                        LazyColumn(Modifier.width(320.dp)
                            .plannerSwipe(state.selected) { onEvent(ScheduleEvent.Select(state.selected.plusDays(it.dayDelta))) }
                            .plannerContentReveal(state.selected), contentPadding = PaddingValues(Zapara.space.l)) {
                            item { Text(uiText(R.string.space_day_23, page.deadlines.count { it.done }, page.deadlines.size), style = Zapara.typography.section) }
                            itemsIndexed(page.deadlines, key = { _, row -> row.sharedId ?: row.id }) { _, row -> DeadlineRow(row, onEvent) }
                        }
                    }
                } else Column(Modifier.fillMaxSize()) {
                    DateStrip(state.selected, state.today, state.pages, onQuickDay={ onEvent(ScheduleEvent.QuickDay(it)) }) { onEvent(ScheduleEvent.Select(it)) }
                    Box(Modifier.weight(1f).fillMaxWidth()
                        .plannerSwipe(state.selected) { onEvent(ScheduleEvent.Select(state.selected.plusDays(it.dayDelta))) }
                        .plannerContentReveal(state.selected)) {
                        when {
                            page == null -> Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
                            else -> LessonList(page, state, onEvent, onOpenMap, onDiscuss)
                        }
                    }
                }
            }
            }
        }
    }
    state.actionsFor?.let { lesson ->
        LessonActionsSheet(
            lesson,
            onRename = { onEvent(ScheduleEvent.Rename(lesson)) },
            onHomework = { onEvent(ScheduleEvent.SubjectHomework(lesson)) },
            onMap = { onOpenMap(lesson.classroomRaw); onEvent(ScheduleEvent.CloseActions) },
            onDismiss = { onEvent(ScheduleEvent.CloseActions) },
            onDiscuss = { onEvent(ScheduleEvent.CloseActions); onDiscuss("${lesson.name} · ${state.selected} · ${lesson.timeStart}–${lesson.timeEnd} · ${lesson.room}") }, date = state.selected
        )
    }
    state.subjectHomework?.let { lesson ->
        ZBottomSheet(onDismiss = { onEvent(ScheduleEvent.CloseSubjectHomework) }, tag = "Schedule.SubjectHomework") {
            Text(lesson.name, style = Zapara.typography.section)
            state.subjectRows.forEach { row ->
                ZCard(Modifier.fillMaxWidth(), onClick = { onEvent(ScheduleEvent.OpenHomework(row)) }) {
                    Text(row.text, style = Zapara.typography.body); Text(row.label, style = Zapara.typography.caption)
                }
            }
            ZButton(uiText(R.string.space_day_10), { onEvent(ScheduleEvent.CloseSubjectHomework); onEvent(ScheduleEvent.AddHomework(lesson)) })
        }
    }
    state.sharedDetail?.let { row -> ZBottomSheet(onDismiss = { onEvent(ScheduleEvent.CloseSubjectHomework) }, tag = "Schedule.SharedHomework") {
        Text(row.label, style = Zapara.typography.section); Text(row.text, style = Zapara.typography.body)
        ZButton(uiText(R.string.space_day_11), { onEvent(ScheduleEvent.CloseSubjectHomework); onDiscuss("${row.label} · ${row.text}") }, ghost = true)
    } }
    state.rename?.let { RenameSheet(it, onEvent) }
    state.homeworkEditor?.let { editor ->
        HomeworkEditorSheet(
            editor,
            onText = { onEvent(ScheduleEvent.HomeworkEditorText(it)) },
            onInc = { onEvent(ScheduleEvent.HomeworkEditorInc) },
            onDec = { onEvent(ScheduleEvent.HomeworkEditorDec) },
            onSave = { onEvent(ScheduleEvent.HomeworkEditorSave) },
            onCancel = { onEvent(ScheduleEvent.HomeworkEditorCancel) },
            onPick = { kind, uri -> onEvent(ScheduleEvent.HomeworkAttach(kind, uri)) },
            onRemove = { onEvent(ScheduleEvent.HomeworkRemoveFile(it)) },
            onShare = { onEvent(ScheduleEvent.HomeworkEditorShare(it)) },
            isGuest = state.guest, onRecalculate = { onEvent(ScheduleEvent.RecalculateHomework) }
        )
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun LessonList(page: DayPage, state: ScheduleUiState, onEvent: (ScheduleEvent) -> Unit, onOpenMap: (String) -> Unit, onDiscuss: (String) -> Unit) {
    val uiText = rememberUiText()
    val breaks = remember(page.lessons) { ScheduleComposer.breaks(page.lessons) }
    val featured = ScheduleComposer.featured(page, state.now)
    val conflicts = remember(page.lessons) { ScheduleComposer.conflicts(page.lessons) }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item {
            Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            state.sourceStatus?.let { Text(it, style = Zapara.typography.caption, color = Zapara.colors.text2) }
            Text(page.caption.substringAfter(" · ", page.caption), style = Zapara.typography.caption, color = Zapara.colors.text2)
            if (page.dataState != null) Text(page.dataState, style = Zapara.typography.body, color = Zapara.colors.text2)
            else if (page.lessons.isNotEmpty()) FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                Text(pluralStringResource(R.plurals.schedule_pair_count, page.lessons.size, page.lessons.size), modifier = Modifier.align(Alignment.CenterVertically), style = Zapara.typography.section, color = Zapara.colors.text1)
                ZChip("${page.lessons.minOf { it.timeStart }}–${page.lessons.maxOf { it.timeEnd }}")
            }
            }
            if (page.lessons.isEmpty() && page.dataState == null) {
                ZCard(Modifier.fillMaxWidth().padding(top = Zapara.space.l), tag = "Schedule.EmptyDay") {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.m)) {
                        Box(Modifier.size(40.dp).background(Zapara.colors.chip, RoundedCornerShape(Zapara.radii.control)), contentAlignment = Alignment.Center) {
                            ZIcon(R.drawable.ic_calendar, null)
                        }
                        Text(stringResource(R.string.schedule_empty_title), modifier = Modifier.weight(1f), style = Zapara.typography.section)
                    }
                    Text(page.nextHint ?: uiText(R.string.space_day_121), style = Zapara.typography.body, color = Zapara.colors.text2)
                    page.nextKnownDate?.let { date -> ZButton(stringResource(R.string.schedule_next_day, date.format(java.time.format.DateTimeFormatter.ofPattern("d MMMM", java.util.Locale("ru")))), { onEvent(ScheduleEvent.Select(date)) }, modifier = Modifier.fillMaxWidth(), tag = "Schedule.NextStudyDay", leadingIcon = R.drawable.ic_calendar) }
                }
            }
            if (page.isToday && page.lessons.isNotEmpty() && featured == null) Text(uiText(R.string.space_day_15), style = Zapara.typography.body)
            if (breaks.isNotEmpty()) Row(Modifier.fillMaxWidth().heightIn(min = Zapara.space.minTouch).clickable { onEvent(ScheduleEvent.FreeTime(!state.showFreeTime)) }, verticalAlignment = Alignment.CenterVertically) {
                Text(uiText(R.string.space_day_16), modifier = Modifier.weight(1f), style = Zapara.typography.body)
                ZSwitch(state.showFreeTime, { onEvent(ScheduleEvent.FreeTime(it)) }, "Schedule.FreeTime")
            }
        }
        itemsIndexed(page.lessons, key = { _, it -> "${it.index}:${it.timeStart}:${it.subjectNorm}:${it.teacher}" }) { _, lesson ->
            if (state.showFreeTime) breaks.filter { it.end.toString() == lesson.timeStart }.forEach { Text(uiText(R.string.space_day_17, (it.start).toString(), (it.end).toString(), (it.minutes / 60).toString(), (it.minutes % 60).toString()), style = Zapara.typography.caption, color = Zapara.colors.text2) }
            if (lesson in conflicts) Text(uiText(R.string.space_day_overlap), style = Zapara.typography.caption, color = Zapara.colors.warn)
            if (lesson == featured) {
                LessonCard(lesson, onLongClick = { onEvent(ScheduleEvent.LongPress(lesson)) }, onRoom = { onOpenMap(lesson.classroomRaw) },
                    onToggleDone = { onEvent(ScheduleEvent.ToggleDone(it)) }, onSubgroup = { stream, option -> onEvent(ScheduleEvent.PickSubgroup(stream, option)) },
                    eyebrow = if (page.isToday) uiText(if (runCatching { java.time.LocalTime.parse(lesson.timeStart) <= state.now.toLocalTime() }.getOrDefault(false)) R.string.space_day_current_pair else R.string.space_day_next_pair) else uiText(R.string.space_day_19),
                    actions = {
                        FlowRow(Modifier.fillMaxWidth().padding(top = Zapara.space.xs), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            ZButton(uiText(R.string.space_day_20), { onOpenMap(lesson.classroomRaw) }, enabled = lesson.classroomRaw.isNotBlank(), ghost = true, leadingIcon = R.drawable.ic_map)
                            ZButton(uiText(R.string.space_day_21), { onEvent(ScheduleEvent.SubjectHomework(lesson)) }, ghost = true, leadingIcon = R.drawable.ic_homework)
                            ZButton(uiText(R.string.space_day_22), { onDiscuss("${lesson.name} · ${page.date} · ${lesson.timeStart}–${lesson.timeEnd} · ${lesson.room}") }, ghost = true, quiet = true, leadingIcon = R.drawable.ic_chat)
                        }
                    })
            } else ZCard(Modifier.fillMaxWidth(), onClick = { onEvent(ScheduleEvent.LongPress(lesson)) }) {
                Text("${lesson.timeStart}–${lesson.timeEnd} · ${lesson.type}", style = Zapara.typography.caption, color = Zapara.colors.text2)
                Text(lesson.name, style = Zapara.typography.bodyStrong)
                Text("${lesson.room} · ${lesson.teacher}", style = Zapara.typography.caption, color = Zapara.colors.text2)
            }
        }
        if (page.deadlines.isNotEmpty()) {
            item { Text(uiText(R.string.space_day_23, (page.deadlines.count { it.done }).toString(), (page.deadlines.size).toString()), style = Zapara.typography.section) }
            itemsIndexed(page.deadlines, key = { _, row -> "deadline:${row.sharedId ?: row.id}" }) { _, row ->
                DeadlineRow(row, onEvent)
            }
        }
        state.undoShared?.let { item { Row(verticalAlignment = Alignment.CenterVertically) {
            Text(uiText(R.string.space_day_26), Modifier.weight(1f), style = Zapara.typography.caption)
            ZButton(uiText(R.string.space_day_27), { onEvent(ScheduleEvent.UndoShared) }, ghost = true, quiet = true)
        } } }
        state.undoDone?.let { item { Row(verticalAlignment = Alignment.CenterVertically) {
            Text(uiText(R.string.space_day_26), Modifier.weight(1f), style = Zapara.typography.caption)
            ZButton(uiText(R.string.space_day_27), { onEvent(ScheduleEvent.UndoDone) }, ghost = true, quiet = true)
        } } }
    }
}

@Composable
private fun DeadlineRow(row: HomeworkRowUi, onEvent: (ScheduleEvent) -> Unit) {
    val uiText = rememberUiText()
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Checkbox(row.done, { if (row.sharedId != null) onEvent(ScheduleEvent.ToggleShared(row.sharedId, !row.done)) else onEvent(ScheduleEvent.ToggleDone(row.id)) }, modifier = Modifier.semantics { contentDescription = uiText(R.string.space_day_24, row.text) })
        Column(Modifier.weight(1f).clickable { onEvent(ScheduleEvent.OpenHomework(row)) }) {
            Text(row.text, style = Zapara.typography.body, textDecoration = if (row.done) androidx.compose.ui.text.style.TextDecoration.LineThrough else null)
            Text(row.label, style = Zapara.typography.caption, color = Zapara.colors.text2)
            Text(uiText(if (row.sharedId == null) R.string.space_day_local_source else R.string.space_day_group_source), style = Zapara.typography.caption, color = Zapara.colors.text2)
            if (row.done) Text(uiText(R.string.space_day_25), style = Zapara.typography.caption)
        }
    }
}
