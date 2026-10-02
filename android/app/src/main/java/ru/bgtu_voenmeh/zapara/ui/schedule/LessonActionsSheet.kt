package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import ru.bgtu_voenmeh.zapara.ui.components.rememberUiText
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.ZActionButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import java.time.format.DateTimeFormatter
import java.util.Locale

@Composable
fun LessonActionsSheet(
    lesson: LessonUi,
    onRename: () -> Unit,
    onHomework: () -> Unit,
    onMap: () -> Unit,
    onDismiss: () -> Unit,
    onDiscuss: () -> Unit = {},
    onShare: () -> Unit = {},
    date: java.time.LocalDate? = null
) {
    val uiText = rememberUiText()
    ZBottomSheet(onDismiss, "Lesson.Actions", scrollable = true) {
        Column(
            Modifier.fillMaxWidth(),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.m)
        ) {
            Text(lesson.name, style = Zapara.typography.section, color = Zapara.colors.text1)
            val details = listOfNotNull(
                date?.format(DateTimeFormatter.ofPattern(stringResource(R.string.ux30_study_lessons_date), Locale("ru"))),
                "${lesson.timeStart}–${lesson.timeEnd}",
                lesson.type.takeIf { it.isNotBlank() },
                lesson.room.takeIf { it.isNotBlank() && !lesson.remote }
            )
            Text(details.joinToString(" · "), style = Zapara.typography.caption, color = Zapara.colors.text2)
            ActionRow(R.drawable.ic_pencil, stringResource(R.string.action_rename), "Actions.Rename", onRename)
            ActionRow(R.drawable.ic_plus, stringResource(R.string.action_homework), "Actions.Homework", onHomework)
            ActionRow(R.drawable.ic_chat, uiText(R.string.space_day_28), "Actions.Discuss", onDiscuss)
            ActionRow(R.drawable.ic_send, stringResource(R.string.ux300_android_share_lesson),
                "Actions.Share", onShare)
            if (!lesson.remote) ActionRow(R.drawable.ic_map_pin, stringResource(R.string.action_map), "Actions.Map", onMap)
        }
    }
}

@Composable
private fun ActionRow(icon: Int, title: String, tag: String, onClick: () -> Unit) {
    ZActionButton(title, onClick, tag = tag, leadingIcon = icon)
}
