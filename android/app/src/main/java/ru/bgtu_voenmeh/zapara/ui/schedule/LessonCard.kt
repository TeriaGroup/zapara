package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Icon
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
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
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
import ru.bgtu_voenmeh.zapara.ui.theme.ZIcon
import ru.bgtu_voenmeh.zapara.ui.theme.breath

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun LessonCard(
    lesson: LessonUi,
    onLongClick: () -> Unit,
    onRoom: () -> Unit,
    onToggleDone: (Long) -> Unit,
    onSubgroup: (String, String) -> Unit = { _, _ -> },
    pendingDoneIds: Set<Long> = emptySet(),
    modifier: Modifier = Modifier,
    eyebrow: String? = null,
    actions: (@Composable ColumnScope.() -> Unit)? = null
) {
    val c = Zapara.colors
    val upcomingDescription = stringResource(R.string.schedule_upcoming_lesson_mark)
    var expanded by remember(lesson.index, lesson.name) { mutableStateOf(false) }
    val hw = if (!expanded && lesson.homework.size > 2) lesson.homework.take(2) else lesson.homework
    ZCard(
        modifier = modifier.fillMaxWidth().alpha(if (lesson.isPast) 0.6f else 1f),
        onClick = { if (lesson.homework.size > 2) expanded = !expanded },
        onLongClick = onLongClick,
        tag = "Lesson.Card.${lesson.index}"
    ) {
        eyebrow?.let { Text(it, style = Zapara.typography.caption, color = c.text2) }
        FlowRow(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)
        ) {
            Text(
                "${lesson.timeStart} – ${lesson.timeEnd}",
                style = Zapara.typography.bodyStrong,
                color = c.text1,
                modifier = Modifier.align(Alignment.CenterVertically).semantics {
                    if (lesson.isUpcoming) stateDescription = upcomingDescription
                }
            )
            if (lesson.type.isNotBlank()) {
                LessonTypeChip(lesson.type, "Lesson.Type.${lesson.index}", Modifier.align(Alignment.CenterVertically))
            }
            if (lesson.room.isNotBlank() && lesson.hasMapLocation) {
                ZChip(lesson.room, modifier = Modifier.align(Alignment.CenterVertically), onClick = onRoom, leading = { ZIcon(R.drawable.ic_map_pin, null) }, tag = "Lesson.Room.${lesson.index}")
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
                    ZSwitch(row.done, { if (row.id !in pendingDoneIds) onToggleDone(row.id) }, "Homework.Done.${row.id}",
                        label = stringResource(R.string.hw_done_switch_label, row.text))
                    if (row.id in pendingDoneIds) Text(stringResource(R.string.ux60_saving),
                        style = Zapara.typography.caption, color = c.text2)
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
        FriendStatusRows(lesson.displayFriends, lesson.isUpcoming)
        if (lesson.homework.size > 2) ZChip(
            if (expanded) stringResource(R.string.ux30_study_hide_homework)
            else pluralStringResource(R.plurals.ux30_study_show_more_homework, lesson.homework.size - 2, lesson.homework.size - 2),
            onClick = { expanded = !expanded }, tag = "Lesson.HomeworkExpand.${lesson.index}")
        actions?.invoke(this)
    }
}

@Composable
@OptIn(ExperimentalLayoutApi::class)
internal fun FriendStatusRows(friends: List<FriendDotUi>, upcoming: Boolean) {
    if (friends.isEmpty()) return
    val c = Zapara.colors
    var friendHint by remember(friends) { mutableStateOf<String?>(null) }
    friends.forEach { dot ->
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
            verticalAlignment = Alignment.CenterVertically) {
            FriendDot(dot.index, Modifier.clickableHint { friendHint = dot.hint })
            val intersection = stringResource(when {
                dot.intersectionScore >= 100 -> R.string.schedule_intersection_same_room
                dot.intersectionScore >= 75 -> R.string.schedule_intersection_same_floor
                dot.intersectionScore >= 50 -> R.string.schedule_intersection_same_building
                dot.intersectionScore >= 25 -> R.string.schedule_intersection_same_time
                else -> R.string.schedule_intersection_none
            })
            FlowRow(Modifier.weight(1f), horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                Text(dot.groupName, style = Zapara.typography.caption, color = c.text2)
                if (upcoming) {
                    val status = stringResource(when (dot.hasLesson) {
                        true -> R.string.schedule_friend_has_lesson
                        false -> R.string.schedule_friend_no_lesson
                        null -> R.string.schedule_friend_no_data
                    })
                    Row(horizontalArrangement = Arrangement.spacedBy(3.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(painterResource(R.drawable.ic_calendar), null, Modifier.size(13.dp), tint = c.info)
                        Text(status, style = Zapara.typography.caption, color = c.text2)
                    }
                }
                if (!upcoming || dot.intersectionScore >= 25)
                    Text("· $intersection", style = Zapara.typography.caption, color = c.text2)
            }
        }
    }
    friendHint?.let { ZChip(it, onClick = { friendHint = null }) }
}

@Composable
private fun Modifier.clickableHint(onClick: () -> Unit): Modifier = this.then(clickable(onClick = onClick))
