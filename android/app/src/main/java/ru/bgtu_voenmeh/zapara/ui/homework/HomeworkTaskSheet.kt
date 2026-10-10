package ru.bgtu_voenmeh.zapara.ui.homework

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.ZActionButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

/** #101 / AN-04: правила плотного экрана «Домашка» (чистые, проверяются JVM-тестами). */
object HomeworkDensity {
    /** Ошибка загрузки и показать нечего — экран ошибки вместо фильтров и счётчиков (AN-10). */
    fun fullError(loadError: String?, personal: Int, shared: Int): Boolean = loadError != null && personal + shared == 0

    /** «Найдено N» нужно, только когда список чем-то сужен: поиск, не «Активные», доп. фильтры, предмет. */
    fun filtered(query: String, completion: HomeworkCompletionFilter, advancedActive: Boolean, subject: String?): Boolean =
        query.isNotBlank() || completion != HomeworkCompletionFilter.Active || advancedActive || subject != null
}

/** Лист задания: то, что раньше занимало карточку (✎, ✕, «Следующая пара по предмету», «Повторить задание»). */
@Composable
fun HomeworkTaskSheet(item: HomeworkItemUi, onClose: () -> Unit, onEvent: (HomeworkEvent) -> Unit, onOpenNextLesson: () -> Unit) {
    val c = Zapara.colors
    ZBottomSheet(onClose, "Sheet.Task.${item.id}") {
        Text(item.subject, style = Zapara.typography.section, color = c.text1)
        Text(item.text, style = Zapara.typography.body, color = c.text1)
        Text(listOf(item.dueLabel, item.statusLabel).filter { it.isNotBlank() }.joinToString(" · "),
            style = Zapara.typography.caption, color = c.text2)
        ZActionButton(stringResource(R.string.hw_task_edit), { onClose(); onEvent(HomeworkEvent.Edit(item.id)) },
            tag = "Homework.Edit.${item.id}", leadingIcon = R.drawable.ic_pencil)
        ZActionButton(stringResource(R.string.ux300_android_next_subject_lesson), onOpenNextLesson,
            tag = "Homework.NextLesson.${item.id}", leadingIcon = R.drawable.ic_calendar)
        if (item.done) ZActionButton(stringResource(R.string.ux300_android_clone_homework),
            { onClose(); onEvent(HomeworkEvent.Clone(item.id)) }, tag = "Homework.Clone.${item.id}")
        ZButton(stringResource(R.string.hw_task_delete), { onClose(); onEvent(HomeworkEvent.AskDelete(item.id)) },
            tag = "Homework.Delete.${item.id}", leadingIcon = R.drawable.ic_trash, startAligned = true, destructive = true,
            modifier = Modifier.fillMaxWidth())
    }
}
