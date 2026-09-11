package ru.bgtu_voenmeh.zapara.ui.summary

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear

@Composable
fun SummarySection(state: SummaryUiState, onEvent: (SummaryEvent) -> Unit) {
    val chrome = LocalShellChrome.current
    val c = Zapara.colors
    Column(Modifier.fillMaxSize().background(c.canvas)) {
        ZTopBar(stringResource(R.string.nav_summary))
        if (!state.loaded) {
            Box(Modifier.padding(Zapara.space.l)) { SkeletonList() }
        } else if (!state.hasGroup) {
            EmptyState(R.drawable.ic_summary, stringResource(R.string.empty_no_group), stringResource(R.string.empty_no_group_hint), stringResource(R.string.group_pick), chrome.onGroupChip, "Empty.NoGroup")
        } else {
            ZSegmented(
                listOf(stringResource(R.string.week_odd), stringResource(R.string.week_even), stringResource(R.string.summary_both)),
                state.segment, { onEvent(SummaryEvent.Segment(it)) }, "Summary.Segment",
                Modifier.padding(horizontal = Zapara.space.l)
            )
            LazyColumn(Modifier.fillMaxSize().testTag("Summary.List"), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                item {
                    ZCard(Modifier.fillMaxWidth().appear(0)) {
                        Text(stringResource(R.string.summary_total), style = Zapara.typography.caption, color = c.text2)
                        Text("${state.tiles.total}", style = Zapara.typography.title, color = c.text1, modifier = Modifier.testTag("Summary.Total"))
                    }
                }
                item {
                    CountCard(stringResource(R.string.summary_by_day),
                        state.tiles.byDay.map { Parity.dayNumberToTitle(it.first) to it.second }, 1,
                        "Summary.ByDay", state.tiles.byDay.map { "Summary.Day.${it.first}" })
                }
                item { CountCard(stringResource(R.string.summary_by_type), state.tiles.byType, 2) }
                item { CountCard(stringResource(R.string.summary_by_subject), state.tiles.bySubject, 3) }
                item { CountCard(stringResource(R.string.summary_by_teacher), state.tiles.byTeacher, 4) }
                item {
                    CountCard(stringResource(R.string.summary_by_room), state.tiles.byRoom, 5,
                        "Summary.ByRoom", state.tiles.byRoom.indices.map { "Summary.Room.$it" },
                        stringResource(R.string.summary_rooms_empty))
                }
            }
        }
    }
}

@Composable
private fun CountCard(title: String, rows: List<Pair<String, Int>>, index: Int,
    tag: String? = null, rowTags: List<String> = emptyList(), emptyText: String? = null) {
    val c = Zapara.colors
    ZCard(Modifier.fillMaxWidth().appear(index).then(if (tag == null) Modifier else Modifier.testTag(tag))) {
        Text(title, style = Zapara.typography.section, color = c.text1)
        if (rows.isEmpty() && emptyText != null) Text(emptyText, style = Zapara.typography.body, color = c.text2)
        rows.forEachIndexed { rowIndex, (name, n) ->
            val rowTag = rowTags.getOrNull(rowIndex)
            Row(Modifier.fillMaxWidth().then(if (rowTag == null) Modifier else Modifier.testTag(rowTag)),
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Text(name, style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                Text("$n", style = Zapara.typography.bodyStrong, color = c.text1)
            }
        }
    }
}
