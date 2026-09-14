package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.ViewRootForTest
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

    @OptIn(androidx.compose.ui.ExperimentalComposeUiApi::class)
    @Test fun due_count_has_russian_units_clamps_and_preserves_date_preview() {
        val stored = homework(7, "overdue")
        var editor by mutableStateOf(HomeworkEditorState(stored.id, stored.norm, "Высшая математика",
            stored.text, 1, true, homeworkEditorDueFor(stored, today) { from, n -> from.plusDays(n.toLong()) }))
        var increments = 0
        var decrements = 0
        show(ThemeChoice.Dark) {
            HomeworkEditorSheet(editor, { editor = editor.withText(it) },
                { increments++; editor = editor.inc() }, { decrements++; editor = editor.dec() }, {}, {})
        }
        fun dialogText(tag: String): SemanticsNodeInteraction {
            val matcher = hasTestTag(tag) and hasAnyAncestor(hasTestTag("Sheet.Homework"))
            // Compose idleness does not establish platform Dialog window focus.
            rule.waitUntil(timeoutMillis = 5_000) {
                val node = rule.onAllNodes(matcher, true).fetchSemanticsNodes().singleOrNull()
                rule.runOnIdle {
                    val view = (node?.root as? ViewRootForTest)?.view
                    view != null && view.isAttachedToWindow && view.hasWindowFocus() &&
                        view.rootView !== rule.activity.window.decorView.rootView &&
                        ownsContext(rule.activity, view.context)
                }
            }
            // Re-query after readiness; never reuse a snapshot from a polling iteration.
            return rule.onNode(matcher, true).assertIsDisplayed()
        }
        fun count(n: Int) {
            val unit = when (n) { 1 -> "занятие"; in 2..4 -> "занятия"; else -> "занятий" }
            // Intentional new visible-caption contract, not an existing production tag.
            val node = dialogText("Editor.Count")
                .assertTextEquals("Через $n $unit").assertIsDisplayed().fetchSemanticsNode()
            rule.runOnIdle {
                assertEquals(n, editor.n)
                val snapshot = RenderedTextEvidence.capture(node)
                RenderedTextEvidence.verify(snapshot, 2f)
                assertTrue("Count caption must not shrink below caption token", snapshot.raw.layoutInput.style.fontSize.value >= 12f)
            }
        }
        count(1)
        rule.onNodeWithTag("Editor.Due", true).assertTextEquals("Срок: 11.09 (Пт)")
        listOf("Editor.Inc", "Editor.Dec").forEach { tag ->
            val bounds = rule.onNodeWithTag(tag).assertIsDisplayed().fetchSemanticsNode().boundsInRoot
            val px = rule.density.density * 48
            assertTrue("$tag actual 48dp bounds", bounds.width + 0.5f >= px && bounds.height + 0.5f >= px)
        }
        rule.onNodeWithTag("Editor.Inc").assertContentDescriptionEquals("Увеличить число занятий до срока")
        rule.onNodeWithTag("Editor.Dec").assertContentDescriptionEquals("Уменьшить число занятий до срока")
        rule.onNodeWithTag("Editor.Dec").performClick()
        count(1)
        rule.runOnIdle { assertEquals(1, decrements) }
        (2..10).forEach { n ->
            rule.onNodeWithTag("Editor.Inc").performClick()
            count(n)
            val expected = "Срок: ${String.format(java.util.Locale.ROOT, "%02d", n + 1)}.09"
            rule.onNodeWithTag("Editor.Due", true).assertTextContains(expected, substring = true)
            val node = dialogText("Editor.Due").fetchSemanticsNode()
            rule.runOnIdle { RenderedTextEvidence.check(node, 2f) }
        }
        rule.onNodeWithTag("Editor.Inc").performClick()
        count(10)
        rule.runOnIdle { assertEquals(10, increments) }
        repeat(9) { rule.onNodeWithTag("Editor.Dec").performClick() }
        count(1)
        rule.onNodeWithTag("Editor.Due", true).assertTextEquals("Срок: 11.09 (Пт)")
        rule.runOnIdle { assertEquals(10, decrements); assertEquals(due, stored.due) }
    }

    @Test fun explicit_edit_opens_correct_row_once_without_toggling_done() {
        val rows = listOf(homework(7, "overdue"), homework(8, "overdue").copy(text = "Второе задание"))
        val events = mutableListOf<HomeworkEvent>()
        var editor by mutableStateOf<HomeworkEditorState?>(null)
        var completed by mutableStateOf(false)
        show(ThemeChoice.Dark) {
            val items = rows.map { row -> HomeworkGroups.toItem(row.copy(done = row.id == 8L && completed),
                if (row.id == 7L) "Матан" else "История", today, copy) }
            HomeworkSection(HomeworkUiState(true, true, HomeworkGroups.group(items, copy), editor)) { event ->
                events += event
                when (event) {
                    is HomeworkEvent.Edit -> {
                        val row = rows.single { it.id == event.id }
                        editor = HomeworkEditorState(row.id, row.norm, if (row.id == 7L) "Матан" else "История",
                            row.text, row.n, true, homeworkEditorDueFor(row, today) { from, n -> from.plusDays(n.toLong()) })
                    }
                    is HomeworkEvent.ToggleDone -> { assertEquals(8L, event.id); completed = !completed }
                    HomeworkEvent.Cancel -> editor = null
                    else -> Unit
                }
            }
        }
        // Intentional new discoverable icon/action; the pre-existing card tap is not enough.
        val edit = rule.onNodeWithTag("Homework.Edit.8").performScrollTo().assertIsDisplayed()
            .assertContentDescriptionEquals("Редактировать: История, Второе задание")
        val bounds = edit.fetchSemanticsNode().boundsInRoot
        assertTrue("Edit actual 48dp target", bounds.width + 0.5f >= 48 * rule.density.density &&
            bounds.height + 0.5f >= 48 * rule.density.density)
        edit.performClick()
        rule.onNodeWithTag("Editor.Text").assertTextContains("Второе задание")
        rule.onNode(hasText("История") and hasAnyAncestor(hasTestTag("Sheet.Homework")),
            useUnmergedTree = true).assertIsDisplayed()
        rule.runOnIdle { assertEquals(listOf(HomeworkEvent.Edit(8)), events); assertFalse(completed); assertEquals(8L, editor?.id) }
        rule.onNodeWithTag("Editor.Cancel").performClick()
        rule.onNodeWithTag("Homework.Done.8").performScrollTo().performClick().assertIsOn()
        rule.runOnIdle {
            assertNull(editor)
            assertEquals(listOf(HomeworkEvent.Edit(8), HomeworkEvent.Cancel, HomeworkEvent.ToggleDone(8)), events)
        }
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
