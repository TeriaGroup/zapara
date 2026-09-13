package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.ui.AndroidUiCopy
import ru.bgtu_voenmeh.zapara.ui.homework.*
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.LocalDate

/** Isolated ComponentActivity + in-memory homework. Never opens or seeds the user database. */
class HomeworkSectionTest {
    @get:Rule val rule = createAndroidComposeRule<ComponentActivity>()
    private val today = LocalDate.of(2026, 9, 12)
    private val due = today.minusDays(1)
    private val copy get() = AndroidUiCopy(rule.activity)
    private fun homework(id: Long, status: String, date: LocalDate? = due) = Homework(
        id, "матан", "Прочитать главу и подготовить конспект", today.minusDays(11), 1, date, status, status == "done"
    )

    @Test fun list_and_done_group() {
        var groups by mutableStateOf(HomeworkGroups.group(listOf(
            HomeworkGroups.toItem(homework(7, "far"), "Матан", today, copy),
            HomeworkGroups.toItem(homework(8, "done"), "История", today, copy)
        ), copy))
        show(ThemeChoice.Dark) {
            HomeworkSection(HomeworkUiState(true, true, groups)) { event ->
                if (event is HomeworkEvent.ToggleGroup) groups = groups.map {
                    if (it.status == event.status) it.copy(collapsed = !it.collapsed) else it
                }
            }
        }
        rule.onNodeWithTag("Homework.Row.7").assertIsDisplayed()
        rule.onNodeWithTag("Homework.Row.8").assertDoesNotExist()
        rule.onNodeWithTag("Homework.Group.Done").performClick()
        rule.onNodeWithTag("Homework.Row.8").assertIsDisplayed()
        rule.onNodeWithTag("Homework.Due.8", true).assertTextEquals("срок 11.09 (Пт)")
    }

    @Test fun empty_and_light() {
        show(ThemeChoice.Light) { HomeworkSection(HomeworkUiState(true, true)) {} }
        rule.onNodeWithTag("Empty.Homework").assertIsDisplayed()
        Frames.capture(rule.activity, "task3-empty-light")
    }

    @Test fun overdue_done_editor_dark_200() = flow(ThemeChoice.Dark)
    @Test fun overdue_done_editor_light_200() = flow(ThemeChoice.Light)

    private fun flow(theme: ThemeChoice) {
        val label = theme.name.lowercase()
        var stored by mutableStateOf(homework(7, "overdue"))
        var editor by mutableStateOf<HomeworkEditorState?>(null)
        var saves = 0
        show(theme) {
            val item = HomeworkGroups.toItem(stored, "Высшая математика", today, copy)
            val groups = HomeworkGroups.group(listOf(item), copy).map { it.copy(collapsed = false) }
            HomeworkSection(HomeworkUiState(true, true, groups, editor)) { event ->
                when (event) {
                    is HomeworkEvent.ToggleDone -> stored = stored.copy(done = !stored.done,
                        status = if (stored.done) "overdue" else "done")
                    is HomeworkEvent.Edit -> editor = HomeworkEditorState(stored.id, stored.norm,
                        "Высшая математика", stored.text, stored.n, true,
                        homeworkEditorDueFor(stored, today) { from, n -> from.plusDays(n.toLong()) })
                    is HomeworkEvent.EditorText -> editor = editor?.withText(event.text)
                    HomeworkEvent.Inc -> editor = editor?.inc()
                    HomeworkEvent.Dec -> editor = editor?.dec()
                    HomeworkEvent.Save -> { saves++; editor = null }
                    HomeworkEvent.Cancel -> editor = null
                    else -> Unit
                }
            }
        }
        Frames.capture(rule.activity, "task3-diagnostic-$label-200")
        checkLabels("Просрочено")
        val toggle = rule.onNodeWithTag("Homework.Done.7")
        toggle.assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp)
            .assertContentDescriptionEquals("Сдано: Высшая математика, Прочитать главу и подготовить конспект")
        Frames.capture(rule.activity, "task3-overdue-$label-200")
        toggle.performClick().assertIsOn()
        checkLabels("Сдано")
        Frames.capture(rule.activity, "task3-done-$label-200")
        rule.onNodeWithTag("Homework.Row.7").performClick()
        rule.onNodeWithTag("Editor.Due", true).assertTextEquals("Срок: 11.09 (Пт)")
        noOverflow("Editor.Due")
        Frames.capture(rule.activity, "task3-editor-$label-200")
        rule.onNodeWithTag("Editor.Text").performTextReplacement("Новый конспект")
        rule.onNodeWithTag("Editor.Due", true).assertTextEquals("Срок: 02.09 (Ср)")
        rule.onNodeWithTag("Editor.Inc").performClick()
        rule.onNodeWithTag("Editor.Dec").performClick()
        rule.onNodeWithTag("Editor.Due", true).assertTextEquals("Срок: 02.09 (Ср)")
        noOverflow("Editor.Due")
        Frames.capture(rule.activity, "task3-fix-text-$label-200")
        rule.onNodeWithTag("Editor.Text").performTextReplacement(stored.text)
        rule.onNodeWithTag("Editor.Due", true).assertTextEquals("Срок: 11.09 (Пт)")
        rule.onNodeWithTag("Editor.Cancel").performClick()
        rule.runOnIdle { assertEquals(0, saves) }
        rule.onNodeWithTag("Homework.Row.7").performClick()
        rule.onNodeWithTag("Editor.Due", true).assertTextEquals("Срок: 11.09 (Пт)")
        rule.onNodeWithTag("Editor.Save").assertHeightIsAtLeast(48.dp).performClick()
        rule.runOnIdle { assertEquals(1, saves); assertEquals(due, stored.due) }
        checkLabels("Сдано")
        toggle.performClick().assertIsOff()
        checkLabels("Просрочено")
        rule.runOnIdle { assertEquals(due, stored.due) }
    }

    @Test fun pending_null_has_no_invented_date() {
        val item = HomeworkGroups.toItem(homework(9, "pending", null), "Физика", today, copy)
        show(ThemeChoice.Light) { HomeworkSection(HomeworkUiState(true, true, HomeworkGroups.group(listOf(item), copy))) {} }
        rule.onNodeWithTag("Homework.Due.9", true).assertTextEquals("срок —")
        rule.onNodeWithTag("Homework.Status.9", true).assertTextEquals("Позже")
        Frames.capture(rule.activity, "task3-pending-light-200")
    }

    @Test fun null_editor_text_change_and_revert_200() {
        val stored = homework(9, "pending", null)
        var editor by mutableStateOf(HomeworkEditorState(stored.id, stored.norm, "Физика", stored.text, stored.n, true,
            homeworkEditorDueFor(stored, today) { from, n -> from.plusDays(n.toLong()) }))
        show(ThemeChoice.Light) {
            HomeworkEditorSheet(editor, { editor = editor.withText(it) }, { editor = editor.inc() },
                { editor = editor.dec() }, {}, {})
        }
        rule.onNodeWithTag("Editor.Due", true).assertTextEquals("Срок: —")
        rule.onNodeWithTag("Editor.Text").performTextReplacement("Новый конспект")
        rule.onNodeWithTag("Editor.Due", true).assertTextEquals("Срок: 02.09 (Ср)")
        noOverflow("Editor.Due")
        captureSettledEditor("task3-fix-null-edited-light-200-v2")
        rule.onNodeWithTag("Editor.Text").performTextReplacement(stored.text)
        rule.onNodeWithTag("Editor.Due", true).assertTextEquals("Срок: —")
        noOverflow("Editor.Due")
        captureSettledEditor("task3-fix-null-reverted-light-200-v2")
    }

    private fun captureSettledEditor(name: String) {
        rule.waitForIdle()
        // The platform IME/insets transition is not driven by the Compose test clock.
        Thread.sleep(1000)
        rule.waitForIdle()
        Frames.capture(rule.activity, name)
    }

    private fun checkLabels(status: String) {
        rule.onNodeWithTag("Homework.Due.7", true).assertTextEquals("срок 11.09 (Пт)").assertIsDisplayed()
        rule.onNodeWithTag("Homework.Status.7", true).assertTextEquals(status).assertIsDisplayed()
        noOverflow("Homework.Due.7")
        noOverflow("Homework.Status.7")
    }

    private fun noOverflow(tag: String) {
        val layouts = mutableListOf<TextLayoutResult>()
        rule.onNodeWithTag(tag, true).performSemanticsAction(androidx.compose.ui.semantics.SemanticsActions.GetTextLayoutResult) { it(layouts) }
        assertTrue("$tag has measured text", layouts.isNotEmpty())
        assertTrue("$tag wraps without clipping: ${layouts.map { "size=${it.size}, paragraph=${it.multiParagraph.width}x${it.multiParagraph.height}, lines=${it.lineCount}, width=${it.didOverflowWidth}, height=${it.didOverflowHeight}" }}", layouts.none { it.hasVisualOverflow })
    }

    private fun show(theme: ThemeChoice, content: @Composable () -> Unit) {
        rule.setContent {
            ZaparaTheme(theme, MotionSettings.Off) {
                CompositionLocalProvider(LocalDensity provides Density(LocalDensity.current.density, 2f),
                    LocalShellChrome provides ShellChrome("А863С", false, true) {}) {
                    Box(Modifier.fillMaxSize().background(Zapara.colors.canvas)) { content() }
                }
            }
        }
    }
}
