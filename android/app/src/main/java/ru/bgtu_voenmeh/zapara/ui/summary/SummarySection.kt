package ru.bgtu_voenmeh.zapara.ui.summary

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.Text
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIcon
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
            HorizontalDivider(Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s),
                thickness = Zapara.space.hairline, color = c.line)
            LazyColumn(Modifier.fillMaxSize().testTag("Summary.List"), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                item {
                    ZCard(Modifier.fillMaxWidth().appear(0)) {
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(Zapara.space.m)) {
                            Box(Modifier.size(40.dp).background(c.chip, RoundedCornerShape(Zapara.radii.control)), contentAlignment = Alignment.Center) {
                                ZIcon(R.drawable.ic_summary, null)
                            }
                            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                                Text(stringResource(R.string.panels_summary_total), style = Zapara.typography.bodyStrong, color = c.text1)
                                Text(stringResource(R.string.panels_summary_period), style = Zapara.typography.caption, color = c.text2)
                            }
                            Text("${state.tiles.total}", style = Zapara.typography.title, color = c.text1, modifier = Modifier.testTag("Summary.Total"))
                        }
                    }
                }
                item {
                    CountCard(stringResource(R.string.summary_by_day),
                        state.tiles.byDay.map { Parity.dayNumberToTitle(it.first) to it.second }, 1,
                        "Summary.ByDay", state.tiles.byDay.map { "Summary.Day.${it.first}" }, showBars = true)
                }
                item { CountCard(stringResource(R.string.summary_by_type), state.tiles.byType, 2, showBars = true) }
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
    tag: String? = null, rowTags: List<String> = emptyList(), emptyText: String? = null, showBars: Boolean = false) {
    val c = Zapara.colors
    val maximum = rows.maxOfOrNull { it.second }?.coerceAtLeast(1) ?: 1
    ZCard(Modifier.fillMaxWidth().appear(index).then(if (tag == null) Modifier else Modifier.testTag(tag))) {
        Text(title, style = Zapara.typography.section, color = c.text1)
        if (rows.isEmpty()) Text(emptyText ?: stringResource(R.string.panels_summary_empty), style = Zapara.typography.body, color = c.text2)
        rows.forEachIndexed { rowIndex, (name, n) ->
            val rowTag = rowTags.getOrNull(rowIndex)
            Column(Modifier.fillMaxWidth().then(if (rowTag == null) Modifier else Modifier.testTag(rowTag)),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    Text(name, style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                    ZChip("$n")
                }
                if (showBars) LinearProgressIndicator(progress = { (n.toFloat() / maximum).coerceIn(0f, 1f) },
                    modifier = Modifier.fillMaxWidth().height(4.dp).clip(RoundedCornerShape(Zapara.radii.pill)).clearAndSetSemantics { },
                    color = c.accent, trackColor = c.chip)
                else if (rowIndex < rows.lastIndex) HorizontalDivider(Modifier.padding(top = Zapara.space.xs), thickness = Zapara.space.hairline, color = c.line)
            }
        }
    }
}
