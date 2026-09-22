package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.constrainHeight
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.FriendDot
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.breath

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun LessonCard(
    lesson: LessonUi,
    onLongClick: () -> Unit,
    onRoom: () -> Unit,
    onToggleDone: (Long) -> Unit,
    onSubgroup: (String, String) -> Unit = { _, _ -> },
    modifier: Modifier = Modifier
) {
    val c = Zapara.colors
    var expanded by remember { mutableStateOf(false) }
    var friendHint by remember { mutableStateOf<String?>(null) }
    val hw = if (!expanded && lesson.homework.size > 2) lesson.homework.take(2) else lesson.homework
    ZCard(
        modifier = modifier.fillMaxWidth().alpha(if (lesson.isPast) 0.6f else 1f),
        onClick = { if (lesson.homework.size > 2) expanded = !expanded },
        onLongClick = onLongClick,
        tag = "Lesson.Card.${lesson.index}"
    ) {
        Row(
            Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)
        ) {
            Text(
                "${lesson.timeStart} – ${lesson.timeEnd}",
                style = Zapara.typography.caption,
                color = c.text2,
                modifier = Modifier.weight(1f)
            )
            if (lesson.type.isNotBlank()) {
                LessonTypeChip(lesson.type, "Lesson.Type.${lesson.index}")
            }
            if (lesson.room.isNotBlank() && !lesson.remote) {
                ZChip(lesson.room, onClick = onRoom, tag = "Lesson.Room.${lesson.index}")
            }
        }
        Text(lesson.name, style = Zapara.typography.section, color = c.text1)
        lesson.original?.let { Text(it, style = Zapara.typography.caption, color = c.text3) }
        Text(lesson.teacher, style = Zapara.typography.caption, color = c.text2)
        lesson.subgroup?.takeIf { it.showChooser }?.let { mark ->
            Text(
                stringResource(if (mark.chosenId == null) R.string.subgroup_pick else R.string.subgroup_yours),
                style = Zapara.typography.caption,
                color = c.text2
            )
            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                mark.options.forEach { option ->
                    ZChip(
                        option.label,
                        selected = option.id == mark.chosenId,
                        onClick = { onSubgroup(mark.streamId, option.id) },
                        tag = "Lesson.Subgroup.${lesson.index}.${option.id}"
                    )
                }
            }
        }
        lesson.nextDate?.let { Text(stringResource(R.string.next_short, it), style = Zapara.typography.caption, color = c.text3) }
        hw.forEach { row ->
            val burning = !row.done && (row.status == "burning" || row.status == "burning_urgent")
            val spacing = Zapara.space.s
            Layout(modifier = Modifier.fillMaxWidth(), content = {
                Text(
                    row.text,
                    style = Zapara.typography.body,
                    color = if (row.done) c.text2 else c.text1,
                    textDecoration = if (row.done) TextDecoration.LineThrough else null
                )
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing)) {
                    if (burning) {
                        Box(Modifier.size(8.dp).clip(CircleShape).background(c.warn).breath(true))
                    }
                    ZChip(row.label, selected = row.status == "burning" || row.status == "burning_urgent")
                    ZSwitch(row.done, { onToggleDone(row.id) }, "Homework.Done.${row.id}")
                }
            }) { measurables, constraints ->
                val gap = spacing.roundToPx()
                val actionWidth = measurables[1].maxIntrinsicWidth(Constraints.Infinity)
                // Reserve the longest word, not a fraction of the row, before placing actions beside text.
                val stacked = measurables[0].minIntrinsicWidth(Constraints.Infinity).toLong() + gap + actionWidth > constraints.maxWidth
                val loose = constraints.copy(minWidth = 0, minHeight = 0)
                val textWidth = if (stacked) constraints.maxWidth else constraints.maxWidth - actionWidth - gap
                val text = measurables[0].measure(loose.copy(minWidth = textWidth, maxWidth = textWidth))
                val actions = measurables[1].measure(loose)
                val height = constraints.constrainHeight(if (stacked) text.height + gap + actions.height else maxOf(text.height, actions.height))
                layout(constraints.maxWidth, height) {
                    text.placeRelative(0, if (stacked) 0 else (height - text.height) / 2)
                    actions.placeRelative(if (stacked) 0 else textWidth + gap,
                        if (stacked) text.height + gap else (height - actions.height) / 2)
                }
            }
        }
        if (lesson.friends.isNotEmpty()) {
            Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs), verticalAlignment = Alignment.CenterVertically) {
                lesson.friends.forEach { dot ->
                    FriendDot(dot.index, Modifier.clickableHint { friendHint = dot.hint })
                }
            }
            if (friendHint == null) {
                lesson.friends.firstOrNull()?.let { Text(it.hint, style = Zapara.typography.caption, color = c.text3) }
            }
            friendHint?.let { ZChip(it, onClick = { friendHint = null }) }
        }
    }
}

@Composable
private fun Modifier.clickableHint(onClick: () -> Unit): Modifier = this.then(clickable(onClick = onClick))
