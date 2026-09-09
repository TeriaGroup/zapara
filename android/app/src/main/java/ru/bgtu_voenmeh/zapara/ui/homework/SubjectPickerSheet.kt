package ru.bgtu_voenmeh.zapara.ui.homework

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.HighlightText
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun SubjectPickerSheet(
    picker: SubjectPickerUi,
    onQuery: (String) -> Unit,
    onPick: (String) -> Unit,
    onDismiss: () -> Unit
) {
    val c = Zapara.colors
    val filtered = remember(picker.subjects, picker.query) {
        val q = picker.query.trim()
        if (q.isEmpty()) picker.subjects else picker.subjects.filter { it.display.contains(q, true) || it.raw.contains(q, true) }
    }
    ZBottomSheet(onDismiss, "Sheet.Subject") {
        Text(stringResource(R.string.subject_picker_title), style = Zapara.typography.section, color = c.text1)
        Spacer(Modifier.height(Zapara.space.s))
        OutlinedTextField(
            value = picker.query, onValueChange = onQuery,
            modifier = Modifier.fillMaxWidth().testTag("Picker.Search"),
            placeholder = { Text(stringResource(R.string.teachers_search_hint), style = Zapara.typography.caption, color = c.text3) },
            singleLine = true,
            shape = RoundedCornerShape(Zapara.radii.control),
            colors = OutlinedTextFieldDefaults.colors(
                focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
                focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
                focusedTextColor = c.text1, unfocusedTextColor = c.text1
            )
        )
        Spacer(Modifier.height(Zapara.space.s))
        LazyColumn(Modifier.fillMaxWidth().heightIn(max = 420.dp), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            items(filtered, key = { it.norm }) { subject ->
                ZCard(onClick = { onPick(subject.raw) }, tag = "Picker.Row.${subject.norm}", modifier = Modifier.fillMaxWidth()) {
                    HighlightText(subject.display, picker.query, Zapara.typography.bodyStrong)
                }
            }
        }
    }
}
