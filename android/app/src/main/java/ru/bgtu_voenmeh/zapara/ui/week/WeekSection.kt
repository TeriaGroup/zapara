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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.constrainHeight
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.schedule.LessonTypeChip
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
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
fun WeekSection(state: WeekUiState, onEvent: (WeekEvent) -> Unit, onOpenDay: (LocalDate) -> Unit) {
    val chrome = LocalShellChrome.current
    val c = Zapara.colors
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
            Row(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZIconButton(R.drawable.ic_chevron_left, stringResource(R.string.ux30_week_previous),
                    { onEvent(WeekEvent.Shift(-1)) }, "Week.Previous")
                val rangeFormat = DateTimeFormatter.ofPattern("d MMM", Locale("ru"))
                Text(stringResource(R.string.ux30_week_range,
                    state.days.firstOrNull()?.date?.format(rangeFormat).orEmpty(),
                    state.days.lastOrNull()?.date?.format(rangeFormat).orEmpty()),
                    style = Zapara.typography.bodyStrong, color = c.text1, modifier = Modifier.weight(1f))
                ZIconButton(R.drawable.ic_chevron_right, stringResource(R.string.ux30_week_next),
                    { onEvent(WeekEvent.Shift(1)) }, "Week.Next")
                ZButton(stringResource(R.string.ux30_week_today), { onEvent(WeekEvent.Today) }, ghost = true, quiet = true)
            }
            state.error?.let { error ->
                ZCard(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l), tag = "Week.LoadError") {
                    Text(error, style = Zapara.typography.body, color = c.text1)
                    ZButton(stringResource(R.string.repeat), { onEvent(WeekEvent.Retry) }, ghost = true,
                        enabled = !state.refreshing, busy = state.refreshing)
                }
            }
            val odd = stringResource(R.string.week_odd)
            val even = stringResource(R.string.week_even)
            val labels = listOf(
                if (state.currentParity == 1) stringResource(R.string.week_current, odd) else odd,
                if (state.currentParity == 2) stringResource(R.string.week_current, even) else even
            )
            ZSegmented(labels, (state.parity - 1).coerceIn(0, 1), { onEvent(WeekEvent.Parity(it)) }, "Week.Segment", Modifier.padding(horizontal = Zapara.space.l))
            Text(stringResource(R.string.next_week_total, state.days.sumOf { it.rows.size }),
                style = Zapara.typography.caption, color = c.text2,
                modifier = Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s))
            LazyColumn(Modifier.fillMaxSize()
                .plannerSwipe(state.parity) { direction -> direction.weekIndex(state.parity)?.let { onEvent(WeekEvent.Parity(it)) } }
                .plannerContentReveal(state.parity), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                itemsIndexed(state.days, key = { _, it -> it.dow }) { index, day ->
                    ZCard(onClick = { onOpenDay(day.date) }, tag = "Week.Day.${day.dow}", modifier = Modifier.fillMaxWidth().appear(index)) {
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            Text(day.title, style = Zapara.typography.section, color = c.text1,
                                modifier = Modifier.weight(1f))
                            if (day.isToday) ZChip(stringResource(R.string.ux30_week_today), tag = "Week.Today.${day.dow}")
                            ZChip(stringResource(R.string.next_week_day_count, day.rows.size),
                                tag = "Week.Count.${day.dow}")
                        }
                        if (day.rows.isEmpty()) {
                            Text(stringResource(R.string.week_no_lessons), style = Zapara.typography.caption, color = c.text2)
                        } else {
                            day.rows.forEachIndexed { rowIndex, row ->
                                val spacing = Zapara.space.s
                                Layout(modifier = Modifier.fillMaxWidth(), content = {
                                    Text(row.time, style = Zapara.typography.caption, color = c.text2, softWrap = false)
                                    FlowRow(
                                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
                                        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)
                                    ) {
                                        Text(row.name, style = Zapara.typography.body, color = c.text1)
                                        if (row.type.isNotBlank()) {
                                            LessonTypeChip(row.type, "Week.Type.${day.dow}.$rowIndex")
                                        }
                                    }
                                    Text(row.room, style = Zapara.typography.caption, color = c.text2)
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
                            }
                        }
                    }
                }
            }
        }
    }
}
