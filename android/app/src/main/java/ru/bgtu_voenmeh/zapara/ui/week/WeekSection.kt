package ru.bgtu_voenmeh.zapara.ui.week

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.DropdownMenu
import androidx.compose.ui.draw.rotate
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.constrainHeight
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.schedule.LessonTypeChip
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZActionButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZDisclosureButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear
import ru.bgtu_voenmeh.zapara.ui.gestures.plannerSwipe
import ru.bgtu_voenmeh.zapara.ui.theme.plannerContentReveal
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun WeekSection(state: WeekUiState, onEvent: (WeekEvent) -> Unit, onOpenDay: (LocalDate) -> Unit) =
    WeekSection(state, onEvent, onOpenLesson = null, onOpenDay = onOpenDay)

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun WeekSection(state: WeekUiState, onEvent: (WeekEvent) -> Unit,
    onOpenLesson: ((LocalDate, String, String) -> Unit)? = null,
    onExportIcs: (WeekUiState) -> Unit = {},
    onShareText: (WeekUiState) -> Unit = {},
    onOpenHomework: (Long) -> Unit = {},
    onOpenAcademicLesson: ((LocalDate, ru.bgtu_voenmeh.zapara.data.Lesson) -> Unit)? = null,
    onOpenAgendaMap: (String) -> Unit = {},
    onOpenDay: (LocalDate) -> Unit) {
    val chrome = LocalShellChrome.current
    val c = Zapara.colors
    val largeText = LocalDensity.current.fontScale >= 1.5f
    var lessonsOnly by rememberSaveable(state.groupId, state.profileName) { mutableStateOf(false) }
    var query by rememberSaveable(state.groupId, state.profileName) { mutableStateOf("") }
    var browseOpen by rememberSaveable(state.groupId, state.profileName) { mutableStateOf(false) }
    var shareOpen by rememberSaveable(state.groupId, state.profileName) { mutableStateOf(false) }
    val keyboard = LocalSoftwareKeyboardController.current
    val context = LocalContext.current
    fun pickWeekDate() {
        val date = state.selectedDate
        val theme = if (c.isDark) R.style.Zapara_DatePicker_Dark else R.style.Zapara_DatePicker_Light
        android.app.DatePickerDialog(context, theme, { _, year, month, day ->
            onEvent(WeekEvent.Jump(LocalDate.of(year, month + 1, day)))
        }, date.year, date.monthValue - 1, date.dayOfMonth).show()
    }
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_week)) { ZButton(stringResource(R.string.nav_schedule), { onOpenDay(state.selectedDate) }, ghost = true, quiet = true) }
        if (!state.loaded) {
            Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
        } else if (state.error != null && !state.hasGroup && state.days.isEmpty()) {
            EmptyState(R.drawable.ic_week, stringResource(R.string.load_fail), state.error,
                stringResource(R.string.repeat), { onEvent(WeekEvent.Retry) }, "Week.LoadFail")
        } else if (!state.hasGroup) {
            EmptyState(R.drawable.ic_week, stringResource(R.string.empty_no_group), stringResource(R.string.empty_no_group_hint), stringResource(R.string.group_pick), chrome.onGroupChip, "Empty.NoGroup")
        } else if (state.noSavedSchedule) {
            EmptyState(R.drawable.ic_week, stringResource(R.string.ux30_week_no_saved),
                state.error ?: stringResource(R.string.ux30_week_no_saved_hint),
                stringResource(R.string.ux30_week_load), { onEvent(WeekEvent.Retry) }, "Week.NoSavedSchedule")
        } else {
            // #102 / AN-05: одна строка шапки «‹ 5–11 окт. · чётная › Сегодня ⋯». Чётность — в подписи
            // (‹ › и так переходят на неделю другой чётности), дата / инструменты / поделиться — в «⋯».
            val caption = WeekHeader.caption(WeekHeader.range(state.days.firstOrNull()?.date, state.days.lastOrNull()?.date),
                stringResource(if (state.parity == 1) R.string.week_odd else R.string.week_even))
            var menuOpen by remember { mutableStateOf(false) }
            @Composable fun Prev() = ZIconButton(R.drawable.ic_chevron_left, stringResource(R.string.ux30_week_previous),
                { onEvent(WeekEvent.Shift(-1)) }, "Week.Previous")
            @Composable fun Next() = ZIconButton(R.drawable.ic_chevron_right, stringResource(R.string.ux30_week_next),
                { onEvent(WeekEvent.Shift(1)) }, "Week.Next")
            @Composable fun Range(modifier: Modifier) = Text(caption, style = Zapara.typography.bodyStrong, color = c.text1,
                modifier = modifier.testTag("Week.Range"), maxLines = 2)
            @Composable fun TodayAndMore() {
                ZButton(stringResource(R.string.ux30_week_today), { onEvent(WeekEvent.Today) }, ghost = true, quiet = true,
                    tag = "Week.Today")
                Box {
                    ZIconButton(R.drawable.ic_ellipsis, stringResource(R.string.week_more), { menuOpen = true }, "Week.More")
                    DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
                        DropdownMenuItem(text = { Text(stringResource(R.string.ux300_android_week_pick_date)) },
                            onClick = { menuOpen = false; pickWeekDate() }, modifier = Modifier.testTag("Week.PickDate"))
                        DropdownMenuItem(text = { Text(stringResource(if (browseOpen) R.string.ux300_visual_week_close
                            else R.string.ux300_visual_week_tools)) },
                            onClick = { menuOpen = false; browseOpen = !browseOpen }, modifier = Modifier.testTag("Week.BrowseTools"))
                        DropdownMenuItem(text = { Text(stringResource(R.string.ux300_visual_week_share)) },
                            onClick = { menuOpen = false; shareOpen = true }, modifier = Modifier.testTag("Week.ShareOpen"))
                    }
                }
            }
            if (largeText) {
                // При fontScale ≥ 1.5 — не больше двух строк: «‹ диапазон ›» и «Сегодня ⋯».
                Column(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l).testTag("Week.Header")) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) { Prev(); Range(Modifier.weight(1f)); Next() }
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs, Alignment.End)) { TodayAndMore() }
                }
            } else {
                Row(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l).testTag("Week.Header"),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    Prev(); Range(Modifier.weight(1f)); Next(); TodayAndMore()
                }
            }
            state.error?.let { error ->
                ZCard(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l), tag = "Week.LoadError") {
                    Text(error, style = Zapara.typography.body, color = c.text1)
                    ZButton(stringResource(R.string.repeat), { onEvent(WeekEvent.Retry) }, ghost = true,
                        enabled = !state.refreshing, busy = state.refreshing)
                }
            }
            val selectedParity = (state.parity - 1).coerceIn(0, 1)
            val visibleDays = WeekBrowse.filter(state.days, query, lessonsOnly)
            LazyColumn(Modifier.fillMaxSize()
                .plannerSwipe(state.parity) { direction -> direction.weekIndex(state.parity)?.let { onEvent(WeekEvent.Parity(it)) } }
                .plannerContentReveal(state.parity), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (browseOpen || query.isNotBlank() || lessonsOnly) item("browse-controls") {
                    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        if (browseOpen) ZDisclosureButton(stringResource(R.string.ux300_visual_week_close),
                            expanded = true, onClick = { browseOpen = false }, tag = "Week.BrowseClose")
                        if (browseOpen) Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            WeekPlanningTools(state, onEvent, onOpenHomework)
                            RoomAgendaTools(state, onEvent, onOpenAgendaMap)
                            AcademicAgendaTools(state) { date, lesson ->
                                if (onOpenAcademicLesson != null) onOpenAcademicLesson(date, lesson)
                                else if (onOpenLesson != null) onOpenLesson(date, lesson.timeStart, lesson.subjectNormalized)
                                else onOpenDay(date)
                            }
                        }
                        if (!browseOpen && (query.isNotBlank() || lessonsOnly))
                            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                                if (query.isNotBlank()) {
                                    Text(query, style = Zapara.typography.caption, color = c.text2)
                                    ZButton(stringResource(R.string.ux100_study_week_clear_search),
                                        { query = "" }, ghost = true, tag = "Week.ClearSearchCompact")
                                }
                                if (lessonsOnly) ZChip(stringResource(R.string.ux100_study_week_classes_only),
                                    selected = true, onClick = { lessonsOnly = false }, tag = "Week.ClassesOnlyCompact")
                            }
                        if (browseOpen) {
                            ZTextField(query, { query = it }, modifier = Modifier.fillMaxWidth()
                                .testTag("Week.Search"),
                                placeholder = { Text(stringResource(R.string.ux100_study_week_search)) },
                                singleLine = true, keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                                keyboardActions = KeyboardActions(onSearch = { keyboard?.hide() }))
                            Column(Modifier.fillMaxWidth(),
                                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                                ZChip(stringResource(R.string.ux100_study_week_classes_only), selected = lessonsOnly,
                                    onClick = { lessonsOnly = !lessonsOnly }, tag = "Week.ClassesOnly")
                                state.days.lastOrNull { it.date.isBefore(state.selectedDate) && it.rows.isNotEmpty() }?.let { previous ->
                                    ZActionButton(stringResource(R.string.ux300_android_week_open_previous),
                                        { onOpenDay(previous.date) }, tag = "Week.OpenPreviousClass")
                                }
                                state.days.firstOrNull { !it.date.isBefore(state.selectedDate) && it.rows.isNotEmpty() }?.let { next ->
                                    ZActionButton(stringResource(R.string.ux100_study_week_open_next),
                                        { onOpenDay(next.date) }, tag = "Week.OpenNextClass")
                                }
                            }
                        }
                    }
                }
                if (query.isBlank() && state.days.isNotEmpty() && state.days.all { it.rows.isEmpty() }) item("empty-week") {
                    ZCard(Modifier.fillMaxWidth(), tag = "Week.EmptyWeek") {
                        Text(stringResource(R.string.ux30_study_empty_week),
                            style = Zapara.typography.body, color = c.text2)
                        if (lessonsOnly) ZActionButton(stringResource(R.string.ux100_study_week_show_days),
                            { lessonsOnly = false }, tag = "Week.ShowDays")
                        ZActionButton(stringResource(R.string.ux30_study_other_week),
                            { onEvent(WeekEvent.Parity(1 - selectedParity)) },
                            tag = "Week.OtherParity")
                    }
                }
                if (state.days.isNotEmpty() && visibleDays.isEmpty() && query.isNotBlank()) item("no-search-results") {
                    ZCard(Modifier.fillMaxWidth(), tag = "Week.NoSearchResults") {
                        Text(stringResource(R.string.ux100_study_week_no_match),
                            style = Zapara.typography.body, color = c.text2)
                        ZActionButton(stringResource(R.string.ux100_study_week_clear_search),
                            { query = "" }, tag = "Week.ClearSearch")
                    }
                }
                itemsIndexed(visibleDays, key = { _, it -> it.dow }) { index, day ->
                    var collapsed by rememberSaveable(state.groupId, state.profileName, day.date.toString()) { mutableStateOf(false) }
                    ZCard(tag = "Week.Day.${day.dow}", modifier = Modifier.fillMaxWidth().appear(index)) {
                        // #102 / AN-05: заголовок дня сам открывает день, стрелка сворачивает пары —
                        // без отдельных строк «Открыть день» и «Свернуть пары».
                        val openDayLabel = stringResource(R.string.week_open_day_named, day.title)
                        val expanded = !collapsed || query.isNotBlank()
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            FlowRow(Modifier.weight(1f).heightIn(min = Zapara.space.minTouch)
                                .clickable(role = Role.Button, onClickLabel = openDayLabel) { onOpenDay(day.date) }
                                .testTag("Week.OpenDay.${day.dow}"),
                                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs, Alignment.CenterVertically)) {
                                Text(day.title, style = Zapara.typography.section, color = c.text1,
                                    modifier = Modifier.align(Alignment.CenterVertically))
                                if (day.isToday) ZChip(stringResource(R.string.ux30_week_today),
                                    tag = "Week.Today.${day.dow}", modifier = Modifier.align(Alignment.CenterVertically))
                                if (day.rows.isNotEmpty()) ZChip(stringResource(R.string.next_week_day_count, day.rows.size),
                                    tag = "Week.Count.${day.dow}", modifier = Modifier.align(Alignment.CenterVertically))
                            }
                            if (day.rows.isNotEmpty() && query.isBlank()) ZIconButton(R.drawable.ic_chevron_right,
                                stringResource(if (collapsed) R.string.ux300_android_week_expand else R.string.ux300_android_week_collapse),
                                { collapsed = !collapsed }, "Week.Collapse.${day.dow}",
                                Modifier.rotate(if (expanded) 90f else 0f))
                        }
                        if (day.rows.isEmpty()) {
                            Text(stringResource(R.string.week_no_lessons), style = Zapara.typography.caption, color = c.text2)
                        } else {
                            if (!collapsed || query.isNotBlank()) day.rows.forEachIndexed { rowIndex, row ->
                                val spacing = Zapara.space.s
                                Layout(modifier = Modifier.fillMaxWidth().heightIn(min = Zapara.space.minTouch)
                                    .clickable(role = Role.Button) {
                                        if (onOpenLesson != null && row.start.isNotBlank() && row.subjectNorm.isNotBlank())
                                            onOpenLesson(day.date, row.start, row.subjectNorm)
                                        else onOpenDay(day.date)
                                    }.testTag("Week.Lesson.${day.dow}.$rowIndex"), content = {
                                    Text(row.time, style = Zapara.typography.caption, color = c.text2, softWrap = false)
                                    FlowRow(
                                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
                                        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)
                                    ) {
                                        Text(WeekHeader.noBreak(row.name), style = Zapara.typography.body, color = c.text1)
                                        if (row.type.isNotBlank()) {
                                            LessonTypeChip(row.type, "Week.Type.${day.dow}.$rowIndex")
                                        }
                                    }
                                    Text(WeekHeader.noBreak(row.room), style = Zapara.typography.caption, color = c.text2)
                                }) { measurables, constraints ->
                                    val gap = spacing.roundToPx()
                                    val timeWidth = measurables[0].maxIntrinsicWidth(Constraints.Infinity)
                                    val roomWidth = measurables[2].maxIntrinsicWidth(Constraints.Infinity)
                                    val subjectWidth = measurables[1].minIntrinsicWidth(Constraints.Infinity)
                                    val stacked = timeWidth.toLong() + roomWidth + subjectWidth + gap * 2 > constraints.maxWidth
                                    val loose = constraints.copy(minWidth = 0, minHeight = 0)
                                    val time = measurables[0].measure(loose)
                                    val room = measurables[2].measure(loose)
                                    val nameWidth = if (stacked) constraints.maxWidth else constraints.maxWidth - time.width - room.width - gap * 2
                                    val name = measurables[1].measure(loose.copy(minWidth = nameWidth, maxWidth = nameWidth))
                                    val height = constraints.constrainHeight(if (stacked) time.height + name.height + room.height + gap * 2 else maxOf(time.height, name.height, room.height))
                                    layout(constraints.maxWidth, height) {
                                        time.placeRelative(0, 0)
                                        name.placeRelative(if (stacked) 0 else time.width + gap, if (stacked) time.height + gap else 0)
                                        room.placeRelative(if (stacked) 0 else constraints.maxWidth - room.width,
                                            if (stacked) time.height + name.height + gap * 2 else 0)
                                    }
                                }
                                if (row.teacher.isNotBlank()) Text(row.teacher,
                                    style = Zapara.typography.caption, color = c.text2,
                                    modifier = Modifier.fillMaxWidth())
                                if (query.isBlank()) day.rows.getOrNull(rowIndex + 1)?.let { following ->
                                    WeekBrowse.freeMinutes(row, following)?.let { minutes ->
                                        Text(stringResource(R.string.ux300_android_week_free_window, minutes),
                                            style = Zapara.typography.caption, color = c.text2,
                                            modifier = Modifier.fillMaxWidth().testTag("Week.Gap.${day.dow}.$rowIndex"))
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    if (shareOpen) ZBottomSheet(onDismiss = { shareOpen = false }, tag = "Week.ShareSheet") {
        Text(stringResource(R.string.ux300_android_share_schedule),
            style = Zapara.typography.section, color = c.text1)
        ZButton(stringResource(R.string.ux300_android_export_ics), {
            shareOpen = false
            onExportIcs(state)
        }, enabled = state.days.size == 7 && state.days.any { it.rows.isNotEmpty() },
            tag = "Week.ExportIcs", modifier = Modifier.fillMaxWidth(), startAligned = true)
        ZActionButton(stringResource(R.string.ux300_android_share_text), {
            shareOpen = false
            onShareText(state)
        }, enabled = state.days.size == 7, tag = "Week.ShareText")
    }
}
