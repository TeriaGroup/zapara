package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.animation.core.tween
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.nestedscroll.NestedScrollConnection
import androidx.compose.ui.input.nestedscroll.NestedScrollSource
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorSheet
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.Durations
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaEase
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.appear

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun ScheduleSection(state: ScheduleUiState, onEvent: (ScheduleEvent) -> Unit, onOpenMap: (String) -> Unit) {
    val chrome = LocalShellChrome.current
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_schedule)) {
            if (state.selected != state.today) {
                ZButton(stringResource(R.string.today), { onEvent(ScheduleEvent.Today) }, ghost = true, tag = "Top.Today")
            }
        }
        when (ScheduleComposer.pane(state)) {
            ScheduleComposer.SchedulePane.Loading -> Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
            ScheduleComposer.SchedulePane.LoadFail -> EmptyState(
                R.drawable.ic_alert,
                stringResource(R.string.load_fail),
                actionText = stringResource(R.string.repeat),
                onAction = { onEvent(ScheduleEvent.Retry) },
                tag = "Empty.LoadFail"
            )
            ScheduleComposer.SchedulePane.NoGroup -> EmptyState(R.drawable.ic_calendar, stringResource(R.string.empty_no_group), stringResource(R.string.empty_no_group_hint), stringResource(R.string.group_pick), chrome.onGroupChip, "Empty.NoGroup")
            ScheduleComposer.SchedulePane.Day -> {
            DateStrip(state.selected, state.today) { onEvent(ScheduleEvent.Select(it)) }
            val page = state.pages[state.selected]
            Text(page?.caption ?: "", style = Zapara.typography.caption, color = Zapara.colors.text2, modifier = Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s).testTag("Schedule.Caption"))
            val pager = rememberPagerState(initialPage = ScheduleComposer.pageIndex(state.selected, state.today)) { ScheduleComposer.PAGE_COUNT }
            val motionOn = Zapara.motion.enabled
            LaunchedEffect(pager.settledPage, state.today) {
                val date = ScheduleComposer.dateAt(pager.settledPage, state.today)
                if (date != state.selected) onEvent(ScheduleEvent.Select(date))
            }
            LaunchedEffect(state.selected, state.today) {
                val idx = ScheduleComposer.pageIndex(state.selected, state.today)
                if (pager.currentPage != idx) {
                    if (motionOn) pager.animateScrollToPage(idx) else pager.scrollToPage(idx)
                }
            }
            HorizontalPager(
                pager,
                Modifier.fillMaxSize().testTag("Schedule.Pager"),
                flingBehavior = PagerDefaults.flingBehavior(
                    state = pager,
                    lowVelocityAnimationSpec = tween(Zapara.motion.ms(Durations.section), easing = ZaparaEase),
                    snapAnimationSpec = tween(Zapara.motion.ms(Durations.section), easing = ZaparaEase)
                )
            ) { index ->
                val date = ScheduleComposer.dateAt(index, state.today)
                val day = state.pages[date]
                LaunchedEffect(date) { if (day == null) onEvent(ScheduleEvent.Need(date)) }
                when {
                    day == null -> Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
                    day.isSunday -> EmptyState(null, stringResource(R.string.no_lessons_sunday))
                    day.lessons.isEmpty() -> EmptyState(R.drawable.ic_calendar, stringResource(R.string.no_lessons_day), day.nextHint)
                    else -> LessonList(day, state.refreshing, onEvent, onOpenMap)
                }
            }
            }
        }
    }
    state.actionsFor?.let { lesson ->
        LessonActionsSheet(
            lesson,
            onRename = { onEvent(ScheduleEvent.Rename(lesson)) },
            onHomework = { onEvent(ScheduleEvent.AddHomework(lesson)) },
            onMap = { onOpenMap(lesson.classroomRaw); onEvent(ScheduleEvent.CloseActions) },
            onDismiss = { onEvent(ScheduleEvent.CloseActions) }
        )
    }
    state.rename?.let { RenameSheet(it, onEvent) }
    state.homeworkEditor?.let { editor ->
        HomeworkEditorSheet(
            editor,
            onText = { onEvent(ScheduleEvent.HomeworkEditorText(it)) },
            onInc = { onEvent(ScheduleEvent.HomeworkEditorInc) },
            onDec = { onEvent(ScheduleEvent.HomeworkEditorDec) },
            onSave = { onEvent(ScheduleEvent.HomeworkEditorSave) },
            onCancel = { onEvent(ScheduleEvent.HomeworkEditorCancel) }
        )
    }
}

@Composable
private fun LessonList(page: DayPage, refreshing: Boolean, onEvent: (ScheduleEvent) -> Unit, onOpenMap: (String) -> Unit) {
    val list = rememberLazyListState()
    val pull = remember {
        object : NestedScrollConnection {
            var pulled = 0f
            override fun onPostScroll(consumed: Offset, available: Offset, source: NestedScrollSource): Offset {
                if (available.y > 0 && list.firstVisibleItemIndex == 0 && list.firstVisibleItemScrollOffset == 0) {
                    pulled += available.y
                    if (pulled > 140f) { pulled = 0f; onEvent(ScheduleEvent.Refresh) }
                } else pulled = 0f
                return Offset.Zero
            }
        }
    }
    LazyColumn(
        state = list,
        modifier = Modifier.fillMaxSize().nestedScroll(pull),
        contentPadding = PaddingValues(Zapara.space.l),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
    ) {
        itemsIndexed(page.lessons, key = { _, it -> it.index }) { index, lesson ->
            LessonCard(
                lesson,
                onLongClick = { onEvent(ScheduleEvent.LongPress(lesson)) },
                onRoom = { onOpenMap(lesson.classroomRaw) },
                onToggleDone = { onEvent(ScheduleEvent.ToggleDone(it)) },
                modifier = Modifier.appear(index)
            )
        }
    }
}
