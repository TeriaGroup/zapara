package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.theme.Durations
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaEase
import java.time.LocalDate
import java.time.format.DateTimeFormatter

@Composable
fun DateStrip(selected: LocalDate, today: LocalDate, onPick: (LocalDate) -> Unit) {
    val dates = remember(today) {
        (0 until ScheduleComposer.PAGE_COUNT).map { ScheduleComposer.dateAt(it, today) }
    }
    val selectedIndex = ScheduleComposer.pageIndex(selected, today)
    val list = rememberLazyListState(selectedIndex)
    val motionOn = Zapara.motion.enabled
    LaunchedEffect(selected, today) {
        val idx = ScheduleComposer.pageIndex(selected, today)
        if (list.firstVisibleItemIndex != idx) {
            if (motionOn) list.animateScrollToItem(idx) else list.scrollToItem(idx)
        }
    }
    val c = Zapara.colors
    LazyRow(
        state = list,
        modifier = Modifier.testTag("Schedule.DateStrip"),
        contentPadding = PaddingValues(horizontal = Zapara.space.l),
        horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)
    ) {
        items(dates, key = { it }) { date ->
            val on = date == selected
            val bg by animateColorAsState(if (on) c.accent else c.chip, tween(Zapara.motion.ms(Durations.indicator), easing = ZaparaEase), label = "dateChip")
            val label by animateColorAsState(if (on) c.onAccent else c.text2, tween(Zapara.motion.ms(Durations.indicator), easing = ZaparaEase), label = "dateLabel")
            val day by animateColorAsState(if (on) c.onAccent else c.text1, tween(Zapara.motion.ms(Durations.indicator), easing = ZaparaEase), label = "dateDay")
            Column(
                Modifier
                    .defaultMinSize(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
                    .clip(RoundedCornerShape(Zapara.radii.control))
                    .background(bg)
                    .clickable { onPick(date) }
                    .padding(horizontal = Zapara.space.s, vertical = Zapara.space.s)
                    .testTag("Schedule.Date.${date.format(DateTimeFormatter.BASIC_ISO_DATE)}"),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs, Alignment.CenterVertically)
            ) {
                Text(stringResource(weekdayRes(date.dayOfWeek.value)), style = Zapara.typography.caption, color = label)
                Text("${date.dayOfMonth}", style = Zapara.typography.bodyStrong, color = day)
                if (date == today) {
                    Box(Modifier.size(4.dp).clip(CircleShape).background(if (on) c.onAccent else c.text2))
                }
            }
        }
    }
}

private fun weekdayRes(dow: Int) = when (dow) {
    1 -> R.string.weekday_1
    2 -> R.string.weekday_2
    3 -> R.string.weekday_3
    4 -> R.string.weekday_4
    5 -> R.string.weekday_5
    6 -> R.string.weekday_6
    else -> R.string.weekday_7
}
