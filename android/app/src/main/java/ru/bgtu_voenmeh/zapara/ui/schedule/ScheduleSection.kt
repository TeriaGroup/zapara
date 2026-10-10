package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.animation.core.tween
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.background
import androidx.compose.foundation.border
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
import androidx.compose.material3.HorizontalDivider
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.DisposableEffect
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import androidx.compose.material3.Checkbox
import androidx.compose.foundation.clickable
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.stateDescription
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
import ru.bgtu_voenmeh.zapara.ui.theme.ZActionButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZDisclosureButton
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
import ru.bgtu_voenmeh.zapara.ui.theme.ZCompactButton
import ru.bgtu_voenmeh.zapara.ui.theme.appear
import ru.bgtu_voenmeh.zapara.ui.gestures.plannerSwipe
import ru.bgtu_voenmeh.zapara.ui.theme.plannerContentReveal
import kotlinx.coroutines.launch

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun ScheduleSection(state: ScheduleUiState, onEvent: (ScheduleEvent) -> Unit,
    onDiscuss: (String) -> Unit = {}, onWeek: (java.time.LocalDate) -> Unit = {},
    onShareDay: (DayPage) -> Unit = {},
    focusTime: String? = null, focusSubject: String? = null, academicKey: String? = null, onOpenMap: (String) -> Unit) {
    val uiText = rememberUiText()
    val chrome = LocalShellChrome.current
    val lifecycle = LocalLifecycleOwner.current
    val context = LocalContext.current
    val shareTitle = stringResource(R.string.ux300_android_share_lesson)
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
        if (state.undoSubgroup != null) ZCard(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l),
            tag = "Schedule.SubgroupUndo") {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.uxnext_subgroup_changed), style = Zapara.typography.caption,
                    modifier = Modifier.weight(1f))
                ZButton(stringResource(R.string.uxnext_subgroup_undo), { onEvent(ScheduleEvent.UndoSubgroup) }, ghost = true)
            }
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
                            .plannerContentReveal(state.selected)) { LessonList(page.copy(deadlines = emptyList()), state,
                                onEvent, onOpenMap, onDiscuss, onShareDay, focusTime, focusSubject, academicKey) }
                        LazyColumn(Modifier.width(320.dp)
                            .plannerSwipe(state.selected) { onEvent(ScheduleEvent.Select(state.selected.plusDays(it.dayDelta))) }
                            .plannerContentReveal(state.selected), contentPadding = PaddingValues(Zapara.space.l)) {
                            item { Text(uiText(R.string.space_day_23, page.deadlines.count { it.done }, page.deadlines.size), style = Zapara.typography.section) }
                             itemsIndexed(page.deadlines, key = { _, row -> row.sharedId ?: row.id }) { _, row -> DeadlineRow(row, state, onEvent) }
                        }
                    }
                } else Column(Modifier.fillMaxSize()) {
                    DateStrip(state.selected, state.today, state.pages, onQuickDay={ onEvent(ScheduleEvent.QuickDay(it)) }) { onEvent(ScheduleEvent.Select(it)) }
                    Box(Modifier.weight(1f).fillMaxWidth()
                        .plannerSwipe(state.selected) { onEvent(ScheduleEvent.Select(state.selected.plusDays(it.dayDelta))) }
                        .plannerContentReveal(state.selected)) {
                        when {
                            page == null -> Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
                            else -> LessonList(page, state, onEvent, onOpenMap, onDiscuss,
                                onShareDay, focusTime, focusSubject, academicKey)
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
            onDiscuss = { onEvent(ScheduleEvent.CloseActions); onDiscuss("${lesson.name} · ${state.selected} · ${lesson.timeStart}–${lesson.timeEnd} · ${lesson.room}") },
            onShare = {
                val day = state.selected.format(java.time.format.DateTimeFormatter.ofPattern(
                    "d MMMM yyyy", java.util.Locale.forLanguageTag("ru")))
                val details = listOf(lesson.name, "$day · ${lesson.timeStart}–${lesson.timeEnd}",
                    lesson.room.takeIf { it.isNotBlank() && !lesson.remote }).filterNotNull().joinToString("\n")
                onEvent(ScheduleEvent.CloseActions)
                context.startActivity(android.content.Intent.createChooser(
                    android.content.Intent(android.content.Intent.ACTION_SEND).apply {
                        type = "text/plain"
                        putExtra(android.content.Intent.EXTRA_TEXT, details)
                    }, shareTitle))
            }, date = state.selected
        )
    }
    state.subjectHomework?.let { lesson ->
        ZBottomSheet(onDismiss = { onEvent(ScheduleEvent.CloseSubjectHomework) }, tag = "Schedule.SubjectHomework") {
            Text(lesson.name, style = Zapara.typography.section)
            when (state.subjectRowsStatus) {
                SubjectRowsStatus.Loading -> Text(stringResource(R.string.schedule_subject_homework_loading), style = Zapara.typography.body, modifier = Modifier.testTag("Schedule.SubjectHomework.Loading"))
                SubjectRowsStatus.Failed -> {
                    Text(stringResource(R.string.schedule_subject_homework_failed), style = Zapara.typography.body,
                        modifier = Modifier.testTag("Schedule.SubjectHomework.Failed"))
                    ZButton(stringResource(R.string.repeat), { onEvent(ScheduleEvent.SubjectHomework(lesson)) },
                        ghost = true, tag = "Schedule.SubjectHomework.Retry")
                }
                SubjectRowsStatus.Ready -> if (state.subjectRows.isEmpty()) Text(stringResource(R.string.schedule_subject_homework_empty), style = Zapara.typography.body, modifier = Modifier.testTag("Schedule.SubjectHomework.Empty"))
            }
            state.subjectRows.forEach { row ->
                ZCard(Modifier.fillMaxWidth(), onClick = { onEvent(ScheduleEvent.OpenHomework(row)) }) {
                    Text(row.text, style = Zapara.typography.body); Text(row.label, style = Zapara.typography.caption)
                }
            }
            ZButton(stringResource(R.string.schedule_subject_homework_add), { onEvent(ScheduleEvent.CloseSubjectHomework); onEvent(ScheduleEvent.AddHomework(lesson)) })
        }
    }
    state.sharedDetail?.let { row -> ZBottomSheet(onDismiss = { onEvent(ScheduleEvent.CloseSubjectHomework) }, tag = "Schedule.SharedHomework") {
        Text(row.label, style = Zapara.typography.section); Text(row.text, style = Zapara.typography.body)
        Text(stringResource(R.string.schedule_homework_shared_context, row.audienceLabel), style = Zapara.typography.caption)
        if (row.canComplete) Text(stringResource(R.string.schedule_homework_my_done, stringResource(if (row.done) R.string.schedule_homework_yes else R.string.schedule_homework_no)), style = Zapara.typography.caption)
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
            onPickMany = { kind, uris -> onEvent(ScheduleEvent.HomeworkAttachMany(kind, uris)) },
            onRemove = { onEvent(ScheduleEvent.HomeworkRemoveFile(it)) },
            onShare = { onEvent(ScheduleEvent.HomeworkEditorShare(it)) },
            onAudience = { onEvent(ScheduleEvent.HomeworkEditorAudience(it)) },
            onRetryShare = { onEvent(ScheduleEvent.HomeworkRetryShare) },
            isGuest = state.guest, onRecalculate = { onEvent(ScheduleEvent.RecalculateHomework) },
            onRetryShareOptions = { onEvent(ScheduleEvent.HomeworkRetryShareOptions) },
            onConfirmDuplicate = { onEvent(ScheduleEvent.HomeworkApproveDuplicate) },
            onCancelDuplicate = { onEvent(ScheduleEvent.HomeworkCancelDuplicate) }
        )
    }
}

@OptIn(ExperimentalLayoutApi::class, ExperimentalFoundationApi::class)
@Composable
private fun LessonList(page: DayPage, state: ScheduleUiState, onEvent: (ScheduleEvent) -> Unit,
    onOpenMap: (String) -> Unit, onDiscuss: (String) -> Unit,
    onShareDay: (DayPage) -> Unit,
    focusTime: String? = null, focusSubject: String? = null, academicKey: String? = null) {
    val uiText = rememberUiText()
    val breaks = remember(page.lessons) { ScheduleComposer.breaksBeforeLessons(page.lessons) }
    val featured = ScheduleComposer.featured(page, state.now)
    val conflictPairs = remember(page.lessons) { ScheduleComposer.conflictPairs(page.lessons) }
    val conflicts = remember(page.lessons) { conflictPairs.flatMap { (a, b) -> listOf(page.lessons[a], page.lessons[b]) }.toSet() }
    val list = rememberLazyListState()
    val scrollScope = rememberCoroutineScope()
    val focusIndex = remember(page.lessons, focusTime, focusSubject, academicKey) {
        if (academicKey == null) ScheduleComposer.encounterIndex(page.lessons, focusTime, focusSubject)
        else page.lessons.indexOfFirst { row -> ru.bgtu_voenmeh.zapara.ui.week.academicLessonKey(row.index,
            row.timeStart, row.timeEnd, row.subjectRaw, row.teacher, row.classroomRaw, row.typeRaw) == academicKey }
    }
    // #108 / AN-08: прошедшие пары сегодня свёрнуты по умолчанию; одна строка в списке их показывает.
    val pastCount = if (page.isToday) page.lessons.count { it.isPast } else 0
    var remainingOnly by rememberSaveable(page.date, state.groupId, state.profileName) { mutableStateOf(pastCount > 0) }
    val live = if (page.isToday) TodayHero.live(page.lessons, state.now.toLocalTime()) else null
    var dayToolsOpen by rememberSaveable(page.date, state.groupId, state.profileName) { mutableStateOf(false) }
    var transfersOpen by rememberSaveable(page.date, state.groupId, state.profileName) { mutableStateOf(false) }
    val visibleIndices = page.lessons.indices.filter { !remainingOnly || !page.isToday || !page.lessons[it].isPast }
    LaunchedEffect(page.date, focusIndex) {
        if (focusIndex >= 0) { remainingOnly = false; list.animateScrollToItem(focusIndex + 1) }
    }
    LazyColumn(Modifier.fillMaxSize(), state = list, contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        // #108 / AN-08: закреплённый блок «Сейчас · Физика · 229 ГК · до 12:20» (рамка lineStrong), открывает лист пары.
        if (live != null) stickyHeader(key = "Schedule.NowHero") {
            Box(Modifier.fillMaxWidth().background(Zapara.colors.canvas).padding(bottom = Zapara.space.xs)) {
                ZCard(Modifier.fillMaxWidth().border(1.dp, Zapara.colors.lineStrong, RoundedCornerShape(Zapara.radii.card)),
                    onClick = { onEvent(ScheduleEvent.LongPress(live)) }, tag = "Schedule.NowHero") {
                    Text(TodayHero.line(live) { id, args -> uiText(id, *args) }, style = Zapara.typography.bodyStrong, color = Zapara.colors.text1)
                }
            }
        }
        item {
            Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            state.sourceStatus?.let { Text(it, style = Zapara.typography.caption, color = Zapara.colors.text2) }
            Text(page.weekLine.ifBlank { page.caption.substringAfter(" · ", page.caption) }, style = Zapara.typography.caption, color = Zapara.colors.text2)
            if (page.dataState != null) Text(page.dataState, style = Zapara.typography.body, color = Zapara.colors.text2)
            else if (page.lessons.isNotEmpty()) FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                Text(pluralStringResource(R.plurals.schedule_pair_count, page.lessons.size, page.lessons.size), modifier = Modifier.align(Alignment.CenterVertically), style = Zapara.typography.section, color = Zapara.colors.text1)
                ZChip("${page.lessons.minOf { it.timeStart }}–${page.lessons.maxOf { it.timeEnd }}")
            }
            ZCard(Modifier.fillMaxWidth(), padded = false, tag = "Schedule.DayActions") {
            Column(Modifier.fillMaxWidth()) {
            ZDisclosureButton(stringResource(R.string.ux300_visual_day_tools),
                dayToolsOpen, { dayToolsOpen = !dayToolsOpen },
                tag = "Schedule.DayTools", leadingIcon = R.drawable.ic_menu, quiet = true)
            if (dayToolsOpen) {
            HorizontalDivider(Modifier.padding(horizontal = Zapara.space.l), color = Zapara.colors.line)
            ZActionButton(stringResource(R.string.ux300_android_share_day), { onShareDay(page) },
                tag = "Schedule.ShareDay", leadingIcon = R.drawable.ic_send, quiet = true)
            if (page.transfers.isNotEmpty()) {
                ZDisclosureButton(stringResource(R.string.ux300_ext_transfers), transfersOpen,
                    { transfersOpen = !transfersOpen }, tag = "Schedule.Transfers", leadingIcon = R.drawable.ic_map, quiet = true)
                if (transfersOpen) {
                    Column(Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s),
                        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    Text(stringResource(R.string.ux300_ext_transfer_estimate), style = Zapara.typography.caption)
                    page.transfers.forEach { transfer ->
                        ZCard(Modifier.fillMaxWidth()) {
                            Text("${transfer.from} → ${transfer.to}", style = Zapara.typography.bodyStrong)
                            val assessment = transfer.assessment
                            Text(stringResource(when (assessment.status) {
                                "tight" -> R.string.ux300_ext_transfer_tight
                                "fits" -> R.string.ux300_ext_transfer_fits
                                "overlap" -> R.string.ux300_ext_transfer_overlap
                                else -> R.string.ux300_ext_transfer_unknown
                            }), style = Zapara.typography.body)
                            if (assessment.availableSeconds != null && assessment.routeSeconds != null)
                                Text(stringResource(R.string.ux300_ext_transfer_times,
                                    assessment.availableSeconds.coerceAtLeast(0) / 60,
                                    (assessment.routeSeconds + 59) / 60), style = Zapara.typography.caption)
                            if (transfer.destinationRaw.isNotBlank()) ZButton(stringResource(R.string.ux300_ext_destination_map),
                                { onOpenMap(transfer.destinationRaw) }, ghost = true)
                        }
                    }
                    }
                }
            }
            if (page.deadlines.isNotEmpty() || featured != null) BoxWithConstraints(
                Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l).padding(top = Zapara.space.s, bottom = Zapara.space.m)) {
                @Composable fun lessonShortcut(modifier: Modifier) {
                if (featured != null) ZButton(stringResource(R.string.schedule_day_jump_lesson), {
                    scrollScope.launch { list.animateScrollToItem(visibleIndices.indexOf(page.lessons.indexOf(featured)).coerceAtLeast(0) + 1) }
                }, modifier = modifier, ghost = true, tag = "Schedule.JumpCurrent")
                }
                @Composable fun deadlineShortcut(modifier: Modifier) {
                if (page.deadlines.isNotEmpty()) ZButton(stringResource(R.string.schedule_day_jump_deadlines), {
                    scrollScope.launch { list.animateScrollToItem(visibleIndices.size + 1) }
                }, modifier = modifier, ghost = true, tag = "Schedule.JumpDeadlines")
                }
                if (maxWidth < 340.dp || LocalDensity.current.fontScale >= 1.5f) {
                    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        lessonShortcut(Modifier.fillMaxWidth())
                        deadlineShortcut(Modifier.fillMaxWidth())
                    }
                } else Row(Modifier.fillMaxWidth().height(IntrinsicSize.Min),
                    horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    lessonShortcut(Modifier.weight(1f).fillMaxHeight())
                    deadlineShortcut(Modifier.weight(1f).fillMaxHeight())
                }
            }
            }
            }
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
            if (pastCount > 0) ZButton(
                if (remainingOnly) stringResource(R.string.schedule_show_past, pastCount) else stringResource(R.string.schedule_hide_past),
                { remainingOnly = !remainingOnly }, modifier = Modifier.fillMaxWidth(), ghost = true, quiet = true,
                tag = "Schedule.RemainingOnly", startAligned = true, leadingIcon = R.drawable.ic_chevron_right)
            if (conflictPairs.isNotEmpty()) ZCard(Modifier.fillMaxWidth(), tag = "Schedule.Conflicts") {
                Text(stringResource(R.string.uxnext_conflict_title, conflictPairs.size),
                    style = Zapara.typography.bodyStrong, color = Zapara.colors.warn)
                conflictPairs.forEach { (firstIndex, secondIndex) ->
                    val first = page.lessons[firstIndex]
                    val second = page.lessons[secondIndex]
                    Text(stringResource(R.string.uxnext_conflict_pair,
                        first.timeStart, first.timeEnd, first.name,
                        second.timeStart, second.timeEnd, second.name),
                        style = Zapara.typography.caption, color = Zapara.colors.text1)
                    ZButton(stringResource(R.string.uxnext_conflict_jump),
                        { remainingOnly = false; scrollScope.launch { list.animateScrollToItem(firstIndex + 1) } },
                        ghost = true, tag = "Schedule.ConflictJump.$firstIndex.$secondIndex")
                }
            }
        }
        itemsIndexed(visibleIndices, key = { _, index -> page.lessons[index].let {
            "${it.index}:${it.timeStart}:${it.subjectNorm}:${it.teacher}:$index" } }) { _, index ->
            val lesson = page.lessons[index]
            if (index == focusIndex) ZChip(stringResource(R.string.uxnext_friend_encounter),
                tag = "Schedule.Encounter.$index")
            breaks[index]?.let { gap ->
                val hours = (gap.minutes / 60).toInt()
                val minutes = (gap.minutes % 60).toInt()
                val duration = when {
                    hours == 0 -> stringResource(R.string.schedule_break_minutes, minutes)
                    minutes == 0 -> stringResource(R.string.schedule_break_hours, hours)
                    else -> stringResource(R.string.schedule_break_hours_minutes, hours, minutes)
                }
                Row(Modifier.fillMaxWidth().padding(bottom = Zapara.space.s)
                    .background(Zapara.colors.surface, RoundedCornerShape(Zapara.radii.control))
                    .padding(horizontal = Zapara.space.m, vertical = Zapara.space.s),
                    verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    Text(stringResource(R.string.schedule_break_time, gap.start.toString(), gap.end.toString()),
                        modifier = Modifier.weight(1f), style = Zapara.typography.caption, color = Zapara.colors.text2)
                    ZChip(duration)
                }
            }
            if (lesson in conflicts) Text(uiText(R.string.space_day_overlap), style = Zapara.typography.caption, color = Zapara.colors.warn)
            if (lesson == featured) {
                LessonCard(lesson, onLongClick = { onEvent(ScheduleEvent.LongPress(lesson)) }, onRoom = { onOpenMap(lesson.classroomRaw) },
                    onToggleDone = { id -> onEvent(ScheduleEvent.ToggleDone(id,
                        lesson.homework.firstOrNull { it.id == id }?.done, state.groupId, state.profileName)) }, onSubgroup = { stream, option ->
                        onEvent(ScheduleEvent.PickSubgroup(stream, option, state.groupId, state.profileName)) },
                    pendingDoneIds = state.completionBusyIds,
                    eyebrow = if (page.isToday) uiText(if (runCatching { java.time.LocalTime.parse(lesson.timeStart) <= state.now.toLocalTime() }.getOrDefault(false)) R.string.space_day_current_pair else R.string.space_day_next_pair) else uiText(R.string.space_day_19),
                    actions = {
                        FlowRow(Modifier.fillMaxWidth().padding(top = Zapara.space.xs), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            // The room chip already opens the map. Keep the primary actions in one row.
                            if (lesson.hasMapLocation && lesson.room.isBlank()) ZCompactButton(uiText(R.string.schedule_lesson_action_map),
                                { onOpenMap(lesson.classroomRaw) },
                                leadingIcon = R.drawable.ic_map, tag = "Schedule.Map.${lesson.index}.$index")
                            ZCompactButton(uiText(R.string.space_day_21), { onEvent(ScheduleEvent.SubjectHomework(lesson)) },
                                leadingIcon = R.drawable.ic_homework, tag = "Schedule.Homework.${lesson.index}.$index")
                            ZCompactButton(uiText(R.string.space_day_22), { onDiscuss("${lesson.name} · ${page.date} · ${lesson.timeStart}–${lesson.timeEnd} · ${lesson.room}") },
                                leadingIcon = R.drawable.ic_chat, tag = "Schedule.Discuss.${lesson.index}.$index")
                        }
                    })
            } else ZCard(Modifier.fillMaxWidth(), onClick = { onEvent(ScheduleEvent.LongPress(lesson)) }) {
                FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    Text("${lesson.timeStart}–${lesson.timeEnd}", style = Zapara.typography.caption, color = Zapara.colors.text2,
                        modifier = Modifier.align(Alignment.CenterVertically).semantics {
                            if (lesson.isUpcoming) stateDescription = uiText(R.string.schedule_upcoming_lesson_mark)
                        })
                    if (lesson.type.isNotBlank()) LessonTypeChip(lesson.type, "Lesson.Type.${lesson.index}",
                        Modifier.align(Alignment.CenterVertically))
                }
                Text(lesson.name, style = Zapara.typography.bodyStrong)
                val placeAndTeacher = listOf(lesson.room, lesson.teacher).filter(String::isNotBlank).joinToString(" · ")
                if (placeAndTeacher.isNotBlank()) Text(placeAndTeacher,
                    style = Zapara.typography.caption, color = Zapara.colors.text2)
                if (lesson.isUpcoming) FriendStatusRows(lesson.displayFriends, true)
                FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    ZCompactButton(uiText(R.string.space_day_21), { onEvent(ScheduleEvent.SubjectHomework(lesson)) },
                        leadingIcon = R.drawable.ic_homework,
                        tag = "Schedule.Homework.${lesson.index}.$index")
                    if (lesson.hasMapLocation) ZCompactButton(uiText(R.string.schedule_lesson_action_map),
                        { onOpenMap(lesson.classroomRaw) }, leadingIcon = R.drawable.ic_map,
                        tag = "Schedule.Map.${lesson.index}.$index")
                }
            }
        }
        if (page.deadlines.isNotEmpty()) {
            item { Text(uiText(R.string.space_day_23, (page.deadlines.count { it.done }).toString(), (page.deadlines.size).toString()), style = Zapara.typography.section) }
            itemsIndexed(page.deadlines, key = { _, row -> "deadline:${row.sharedId ?: row.id}" }) { _, row ->
                DeadlineRow(row, state, onEvent)
            }
        }
        state.undoShared?.let { item { Row(verticalAlignment = Alignment.CenterVertically) {
            Text(uiText(R.string.space_day_26), Modifier.weight(1f), style = Zapara.typography.caption)
            ZButton(uiText(R.string.space_day_27), { onEvent(ScheduleEvent.UndoShared) }, ghost = true, quiet = true)
        } } }
        state.undoDone?.let { item { Row(verticalAlignment = Alignment.CenterVertically) {
            Text(uiText(R.string.space_day_26), Modifier.weight(1f), style = Zapara.typography.caption)
            ZButton(uiText(R.string.space_day_27), { onEvent(ScheduleEvent.UndoDone) }, ghost = true,
                quiet = true, enabled = !state.undoDoneBusy)
        } } }
    }
}

@Composable
private fun DeadlineRow(row: HomeworkRowUi, state: ScheduleUiState, onEvent: (ScheduleEvent) -> Unit) {
    val uiText = rememberUiText()
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Checkbox(row.done, { if (row.sharedId != null) onEvent(ScheduleEvent.ToggleShared(row.sharedId, !row.done,
            state.groupId, state.profileName))
            else onEvent(ScheduleEvent.ToggleDone(row.id, row.done, state.groupId, state.profileName)) },
            enabled = row.canComplete && (row.sharedId?.let { it !in state.sharedBusyIds }
                ?: (row.id !in state.completionBusyIds)),
            modifier = Modifier.semantics { contentDescription = uiText(R.string.space_day_24, row.text) })
        Column(Modifier.weight(1f).clickable { onEvent(ScheduleEvent.OpenHomework(row)) }) {
            Text(row.text, style = Zapara.typography.body, textDecoration = if (row.done) androidx.compose.ui.text.style.TextDecoration.LineThrough else null)
            Text(row.label, style = Zapara.typography.caption, color = Zapara.colors.text2)
            if (row.sharedId?.let { it in state.sharedBusyIds } == true ||
                row.sharedId == null && row.id in state.completionBusyIds)
                Text(stringResource(R.string.ux60_saving), style = Zapara.typography.caption,
                    color = Zapara.colors.text2)
            Text(if (row.sharedId == null) stringResource(R.string.schedule_homework_personal) else stringResource(R.string.schedule_homework_shared_row, row.audienceLabel), style = Zapara.typography.caption, color = Zapara.colors.text2)
            if (row.done) Text(uiText(R.string.space_day_25), style = Zapara.typography.caption)
        }
    }
}
