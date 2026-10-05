package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.text.TextLayoutResult
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.LessonTypeKind
import ru.bgtu_voenmeh.zapara.ui.homework.*
import ru.bgtu_voenmeh.zapara.ui.schedule.LessonTypeChip
import ru.bgtu_voenmeh.zapara.ui.schedule.LessonTypeStyle
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.theme.*

class VisualFindingsRegressionTest {
    @get:Rule val rule = createEmptyComposeRule()

    @Test fun lesson_type_caption_has_readable_contrast_in_both_themes() {
        val types = listOf("Практика" to LessonTypeKind.Practice, "Лабораторная" to LessonTypeKind.Lab)
        for (theme in listOf(ThemeChoice.Light, ThemeChoice.Dark)) {
            OwnedTestHost.launch().use { host ->
                host.scenario.onActivity { activity -> activity.setContent {
                    ZaparaTheme(theme, MotionSettings.Off) {
                        Column(Modifier.fillMaxSize().background(Zapara.colors.card)) {
                            types.forEach { (raw, kind) -> LessonTypeChip(raw, "Contrast.${kind.name}") }
                        }
                    }
                } }
                rule.waitForIdle()
                host.awaitForeground()
                types.forEach { (raw, kind) ->
                    assertEquals("Fixture must use the real type", kind, LessonTypeStyle.kind(raw))
                    val label = host.activity.getString(LessonTypeStyle.labelRes(kind))
                    val layouts = mutableListOf<TextLayoutResult>()
                    rule.onNode(hasText(label) and hasAnyAncestor(hasTestTag("Contrast.${kind.name}")),
                        useUnmergedTree = true).performSemanticsAction(SemanticsActions.GetTextLayoutResult) { it(layouts) }
                    assertTrue(layouts.isNotEmpty())
                    val ink = layouts.single().layoutInput.style.color
                    assertNotEquals(Color.Unspecified, ink)
                    val palette = if (theme == ThemeChoice.Dark) DarkColors else LightColors
                    val background = LessonTypeStyle.wash(kind, palette.isDark).compositeOver(palette.card)
                    val a = ink.compositeOver(background).luminance()
                    val b = background.luminance()
                    val ratio = (maxOf(a, b) + .05f) / (minOf(a, b) + .05f)
                    assertTrue("$theme $kind caption contrast=$ratio, required 4.5", ratio >= 4.5f)
                }
                Frames.capture(host.activity, "fixed-type-contrast-${theme.name.lowercase()}")
            }
        }
    }

    @Test fun completed_homework_has_its_own_empty_state_and_opens_done_tasks() {
        var state by mutableStateOf(HomeworkUiState(loaded = true, hasGroup = true,
            groups = listOf(HomeworkGroupUi(GroupStatus.Done, "Выполнено",
                listOf(HomeworkItemUi(1, "Математика", "Решить задачи", "", "done", true)), false))))
        OwnedTestHost.launch().use { host ->
            host.scenario.onActivity { activity -> activity.setContent {
                ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                    CompositionLocalProvider(LocalShellChrome provides ShellChrome("Тестовая группа", false, true) {}) {
                        Surface(Modifier.fillMaxSize(), color = Zapara.colors.canvas) {
                            HomeworkSection(state) { event ->
                                if (event is HomeworkEvent.BrowseFilter) state = state.copy(browseFilter = event.value)
                            }
                        }
                    }
                }
            } }
            rule.waitForIdle()
            rule.onNodeWithText("Все задания выполнены").assertIsDisplayed()
            Frames.capture(host.activity, "fixed-homework-completed-empty")
            rule.onNodeWithText("Показать выполненные").performClick()
            rule.runOnIdle { assertEquals(HomeworkCompletionFilter.Done, state.browseFilter) }
            rule.onNodeWithTag("Homework.Row.1").assertIsDisplayed()
        }
    }

    @Test fun search_with_no_results_does_not_claim_all_homework_is_done() {
        OwnedTestHost.launch().use { host ->
            host.scenario.onActivity { activity -> activity.setContent {
                ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                    CompositionLocalProvider(LocalShellChrome provides ShellChrome("Тестовая группа", false, true) {}) {
                        HomeworkSection(HomeworkUiState(loaded = true, hasGroup = true, browseQuery = "нет такого",
                            groups = listOf(HomeworkGroupUi(GroupStatus.Done, "Выполнено",
                                listOf(HomeworkItemUi(1, "Математика", "Решить задачи", "", "done", true)), false)))) { }
                    }
                }
            } }
            rule.waitForIdle()
            rule.onNodeWithText("Все задания выполнены").assertDoesNotExist()
            rule.onNodeWithTag("Empty.HomeworkFiltered").assertIsDisplayed()
        }
    }
}
