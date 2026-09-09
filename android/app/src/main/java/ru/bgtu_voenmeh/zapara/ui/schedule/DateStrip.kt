package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import java.time.LocalDate
import java.time.format.DateTimeFormatter

@Composable
fun DateStrip(selected: LocalDate, today: LocalDate, onPick: (LocalDate) -> Unit) {
    val dates = remember(selected) { (-30..30).map { selected.plusDays(it.toLong()) } }
    val list = rememberLazyListState(30)
    LaunchedEffect(selected) { list.scrollToItem(30) }
    val c = Zapara.colors
    LazyRow(
        state = list,
        modifier = Modifier.testTag("Schedule.DateStrip"),
        contentPadding = PaddingValues(horizontal = Zapara.space.l),
        horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)
    ) {
        items(dates, key = { it }) { date ->
            val on = date == selected
            Column(
                Modifier
                    .size(44.dp, 56.dp)
                    .clip(RoundedCornerShape(Zapara.radii.control))
                    .background(if (on) c.accent else c.chip)
                    .clickable { onPick(date) }
                    .testTag("Schedule.Date.${date.format(DateTimeFormatter.BASIC_ISO_DATE)}"),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center
            ) {
                Text(stringResource(weekdayRes(date.dayOfWeek.value)), style = Zapara.typography.caption, color = if (on) c.onAccent else c.text2)
                Text("${date.dayOfMonth}", style = Zapara.typography.bodyStrong, color = if (on) c.onAccent else c.text1)
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
