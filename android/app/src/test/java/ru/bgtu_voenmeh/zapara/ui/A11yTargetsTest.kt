package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** #105 (AN-25, 27, 18, 19, 21): подписи переключателей и листов, касания от 48 dp. */
class A11yTargetsTest {
    private fun src(path: String) = File("src/main/java/ru/bgtu_voenmeh/zapara/$path").readText()

    @Test fun switch_takes_a_label_and_lesson_card_uses_it() {
        val controls = src("ui/components/Controls.kt")
        assertTrue(controls.contains("label: String? = null"))
        assertTrue(controls.contains("Modifier.semantics { contentDescription = label }"))
        assertTrue(src("ui/schedule/LessonCard.kt").contains("label = stringResource(R.string.hw_done_switch_label, row.text)"))
        assertTrue(File("src/main/res/values/strings_a11y_targets.xml").readText().contains(">Выполнено: %1\$s<"))
    }

    @Test fun sheet_backdrop_is_not_an_unlabelled_button() {
        val sheet = src("ui/components/Controls.kt").substringAfter("fun ZBottomSheet(").substringBefore("BoxWithConstraints(")
        assertFalse("подложка без clickable", sheet.contains(".clickable(onClick = requestDismiss)"))
        assertTrue(sheet.contains("detectTapGestures { requestDismiss() }"))
        assertTrue("«Назад» закрывает лист", src("ui/components/Controls.kt").contains("BackHandler(onBack = requestDismiss)"))
    }

    @Test fun sections_and_group_rows_are_at_least_48dp() {
        val sheets = src("ui/shell/Sheets.kt")
        assertTrue(sheets.contains(".testTag(section.tag)\n            .heightIn(min = Zapara.space.minTouch)"))
        assertTrue(sheets.contains(".testTag(\"Picker.Row.\${group.id}\")\n                        .heightIn(min = Zapara.space.minTouch)"))
    }

    @Test fun deadline_checkbox_has_a_48dp_toggle_area() {
        val row = src("ui/schedule/ScheduleSection.kt").substringAfter("private fun DeadlineRow(").substringBefore("\n}\n")
        assertTrue(row.contains("Box(Modifier.size(Zapara.space.minTouch).testTag(\"Deadline.Done.\${row.id}\")"))
        assertTrue(row.contains(".toggleable(row.done, enabled = canToggle, role = Role.Checkbox)"))
        assertTrue("сам чекбокс без своего обработчика", row.contains("Checkbox(row.done, null, enabled = canToggle)"))
    }
}
