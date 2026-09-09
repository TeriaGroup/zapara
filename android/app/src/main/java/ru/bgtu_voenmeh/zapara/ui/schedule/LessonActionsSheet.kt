package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIcon
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun LessonActionsSheet(
    lesson: LessonUi,
    onRename: () -> Unit,
    onHomework: () -> Unit,
    onMap: () -> Unit,
    onDismiss: () -> Unit
) {
    ZBottomSheet(onDismiss, "Lesson.Actions") {
        Text(lesson.name, style = Zapara.typography.section, color = Zapara.colors.text1)
        ActionRow(R.drawable.ic_pencil, stringResource(R.string.action_rename), "Actions.Rename", onRename)
        ActionRow(R.drawable.ic_plus, stringResource(R.string.action_homework), "Actions.Homework", onHomework)
        if (!lesson.remote) ActionRow(R.drawable.ic_map_pin, stringResource(R.string.action_map), "Actions.Map", onMap)
    }
}

@Composable
private fun ActionRow(icon: Int, title: String, tag: String, onClick: () -> Unit) {
    ZCard(onClick = onClick, tag = tag, modifier = Modifier.fillMaxWidth()) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZIcon(icon, title, Modifier.size(Zapara.space.icon))
            Text(title, style = Zapara.typography.bodyStrong, color = Zapara.colors.text1)
        }
    }
}
