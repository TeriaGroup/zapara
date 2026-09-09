package ru.bgtu_voenmeh.zapara.ui.week

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear
import java.time.LocalDate

@Composable
fun WeekSection(state: WeekUiState, onEvent: (WeekEvent) -> Unit, onOpenDay: (LocalDate) -> Unit) {
    val chrome = LocalShellChrome.current
    val c = Zapara.colors
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_week))
        if (!state.hasGroup) {
            EmptyState(R.drawable.ic_week, stringResource(R.string.empty_no_group), stringResource(R.string.empty_no_group_hint), stringResource(R.string.group_pick), chrome.onGroupChip, "Empty.NoGroup")
        } else {
            val odd = stringResource(R.string.week_odd)
            val even = stringResource(R.string.week_even)
            val labels = listOf(
                if (state.currentParity == 1) stringResource(R.string.week_current, odd) else odd,
                if (state.currentParity == 2) stringResource(R.string.week_current, even) else even
            )
            ZSegmented(labels, (state.parity - 1).coerceIn(0, 1), { onEvent(WeekEvent.Parity(it)) }, "Week.Segment", Modifier.padding(horizontal = Zapara.space.l))
            LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                itemsIndexed(state.days, key = { _, it -> it.dow }) { index, day ->
                    ZCard(onClick = { onOpenDay(day.date) }, tag = "Week.Day.${day.dow}", modifier = Modifier.fillMaxWidth().appear(index)) {
                        Text(day.title, style = Zapara.typography.section, color = c.text1)
                        if (day.rows.isEmpty()) {
                            Text(stringResource(R.string.week_no_lessons), style = Zapara.typography.caption, color = c.text2)
                        } else {
                            day.rows.forEach { row ->
                                Row(Modifier.fillMaxWidth()) {
                                    Text(row.time, style = Zapara.typography.caption, color = c.text2, modifier = Modifier.width(44.dp))
                                    Text(row.name, style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                                    Text(row.room, style = Zapara.typography.caption, color = c.text2)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
