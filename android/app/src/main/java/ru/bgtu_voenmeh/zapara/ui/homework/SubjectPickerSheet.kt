package ru.bgtu_voenmeh.zapara.ui.homework

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.HighlightText
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.schedule.LessonTypeChip
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton

@Composable
fun SubjectPickerSheet(
    picker: SubjectPickerUi,
    onQuery: (String) -> Unit,
    onPick: (String) -> Unit,
    onDismiss: () -> Unit,
    onManual: (String) -> Unit = {}
) {
    val c = Zapara.colors
    val keyboard = LocalSoftwareKeyboardController.current
    val filtered = remember(picker.subjects, picker.query) {
        val q = picker.query.trim()
        if (q.isEmpty()) picker.subjects else picker.subjects.filter { it.display.contains(q, true) || it.raw.contains(q, true) }
    }
    val ambiguous = remember(filtered) {
        filtered.groupingBy { it.display }.eachCount().filterValues { it > 1 }.keys
    }
    ZBottomSheet(onDismiss, "Sheet.Subject") {
        Text(stringResource(R.string.subject_picker_title), style = Zapara.typography.section, color = c.text1)
        Spacer(Modifier.height(Zapara.space.s))
        ZTextField(
            value = picker.query, onValueChange = onQuery,
            modifier = Modifier.fillMaxWidth().testTag("Picker.Search"),
            placeholder = { Text(stringResource(R.string.hw_pick_subject), style = Zapara.typography.caption, color = c.text3) },
            singleLine = true,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { keyboard?.hide() }),
            trailingIcon = if (picker.query.isNotEmpty()) {
                {
                ZIconButton(R.drawable.ic_x, stringResource(R.string.next_teachers_clear), { onQuery("") }, "Picker.ClearSearch")
                }
            } else null
        )
        Spacer(Modifier.height(Zapara.space.s))
        Text(stringResource(if (filtered.isEmpty()) R.string.polish_subject_no_results else R.string.homework_ux_subject_count, filtered.size),
            style = Zapara.typography.caption, color = c.text2,
            modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite })
        if (picker.subjects.isEmpty()) {
            Text(stringResource(R.string.uxnext_homework_manual_hint), style = Zapara.typography.caption, color = c.text2)
            ZButton(stringResource(R.string.uxnext_homework_manual_add), { onManual(picker.query.trim()) },
                enabled = manualSubjectAllowed(picker.subjects, picker.query), tag = "Picker.ManualSubject")
        }
        LazyColumn(Modifier.fillMaxWidth().heightIn(max = 420.dp), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            items(filtered, key = { it.norm }) { subject ->
                ZCard(onClick = { onPick(subject.raw) }, tag = "Picker.Row.${subject.norm}", modifier = Modifier.fillMaxWidth()) {
                    Row(
                        Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)
                    ) {
                        HighlightText(subject.display, picker.query, Zapara.typography.bodyStrong, Modifier.weight(1f))
                        if (subject.display in ambiguous) LessonTypeChip(subject.type, "Picker.Type.${subject.norm}")
                    }
                }
            }
        }
    }
}
