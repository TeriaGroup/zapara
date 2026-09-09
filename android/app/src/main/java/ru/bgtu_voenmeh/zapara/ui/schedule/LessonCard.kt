package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.FriendDot
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.breath

@Composable
fun LessonCard(
    lesson: LessonUi,
    onLongClick: () -> Unit,
    onRoom: () -> Unit,
    onToggleDone: (Long) -> Unit,
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
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text("${lesson.timeStart} – ${lesson.timeEnd} · ${lesson.type}", style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
            if (lesson.room.isNotBlank() && !lesson.remote) {
                ZChip(lesson.room, onClick = onRoom, tag = "Lesson.Room.${lesson.index}")
            }
        }
        Text(lesson.name, style = Zapara.typography.section, color = c.text1)
        lesson.original?.let { Text(it, style = Zapara.typography.caption, color = c.text3) }
        Text(lesson.teacher, style = Zapara.typography.caption, color = c.text2)
        lesson.nextDate?.let { Text(stringResource(R.string.next_short, it), style = Zapara.typography.caption, color = c.text3) }
        hw.forEach { row ->
            val burning = !row.done && (row.status == "burning" || row.status == "burning_urgent")
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (burning) {
                    Box(Modifier.size(8.dp).clip(CircleShape).background(c.warn).breath(true))
                }
                Text(
                    row.text,
                    style = Zapara.typography.body,
                    color = if (row.done) c.text2 else c.text1,
                    textDecoration = if (row.done) TextDecoration.LineThrough else null,
                    modifier = Modifier.weight(1f)
                )
                ZChip(row.label, selected = row.status == "burning" || row.status == "burning_urgent")
                ZSwitch(row.done, { onToggleDone(row.id) }, "Homework.Done.${row.id}")
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
