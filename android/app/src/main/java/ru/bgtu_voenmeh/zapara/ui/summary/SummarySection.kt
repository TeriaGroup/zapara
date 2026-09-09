package ru.bgtu_voenmeh.zapara.ui.summary

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SummarySection(state: SummaryUiState, onEvent: (SummaryEvent) -> Unit) {
    val chrome = LocalShellChrome.current
    val c = Zapara.colors
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_summary))
        if (!state.hasGroup) {
            EmptyState(R.drawable.ic_summary, stringResource(R.string.empty_no_group), stringResource(R.string.empty_no_group_hint), stringResource(R.string.group_pick), chrome.onGroupChip, "Empty.NoGroup")
        } else {
            ZSegmented(
                listOf(stringResource(R.string.week_odd), stringResource(R.string.week_even), stringResource(R.string.summary_both)),
                state.segment, { onEvent(SummaryEvent.Segment(it)) }, "Summary.Segment",
                Modifier.padding(horizontal = Zapara.space.l)
            )
            LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                item {
                    ZCard(Modifier.fillMaxWidth().appear(0)) {
                        Text(stringResource(R.string.summary_total), style = Zapara.typography.caption, color = c.text2)
                        Text("${state.tiles.total}", style = Zapara.typography.title, color = c.text1, modifier = Modifier.testTag("Summary.Total"))
                    }
                }
                item { CountCard(stringResource(R.string.summary_by_type), state.tiles.byType, 1) }
                item { CountCard(stringResource(R.string.summary_by_subject), state.tiles.bySubject, 2) }
                item { CountCard(stringResource(R.string.summary_by_teacher), state.tiles.byTeacher, 3) }
                item {
                    ZCard(Modifier.fillMaxWidth().appear(4)) {
                        Text(stringResource(R.string.summary_rooms), style = Zapara.typography.section, color = c.text1)
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            state.tiles.rooms.forEach { ZChip(it) }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun CountCard(title: String, rows: List<Pair<String, Int>>, index: Int) {
    val c = Zapara.colors
    ZCard(Modifier.fillMaxWidth().appear(index)) {
        Text(title, style = Zapara.typography.section, color = c.text1)
        rows.forEach { (name, n) ->
            Row(Modifier.fillMaxWidth()) {
                Text(name, style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                Text("$n", style = Zapara.typography.bodyStrong, color = c.text1)
            }
        }
    }
}
