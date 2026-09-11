package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.widthIn
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import java.io.File
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GroupRef
import ru.bgtu_voenmeh.zapara.data.LecturerLesson
import ru.bgtu_voenmeh.zapara.ui.AndroidUiCopy
import ru.bgtu_voenmeh.zapara.ui.teachers.*
import ru.bgtu_voenmeh.zapara.ui.theme.*

/** Isolated real TeacherScreen/Composer, memory-only event host. No AppContainer or user DB. */
@OptIn(androidx.compose.ui.ExperimentalComposeUiApi::class)
class TeacherParityTest {
    @get:Rule val rule = createAndroidComposeRule<ComponentActivity>()
    private val name = "Александрова Константина Александровна"
    private val teachers = listOf(TeacherRowUi("mine", name, "Математика", true),
        TeacherRowUi("other", "Петров Пётр Петрович", "Физика", false))
    private val labels = mapOf(0 to "Обе недели", 1 to "Нечётная неделя",
        2 to "Чётная неделя", -1 to "Чётность не указана")
    private val fixture = listOf(0, 1, 2, -1).map { parity ->
        LecturerLesson(dayOfWeek = 1, parity = parity, timeStart = "09:00",
            subjectRaw = "Математика", roomRaw = "Учебная лаборатория", buildingRaw = "ГК",
            groups = listOf(GroupRef(if (parity == 2) "other" else "mine", "А863С")))
    }

    @Test fun actual_rows_and_filters_dark_200() = flow(ThemeChoice.Dark)
    @Test fun actual_rows_and_filters_light_200() = flow(ThemeChoice.Light)

    private fun flow(theme: ThemeChoice) {
        show(theme)
        val prefix = theme.name.lowercase()
        rule.onNodeWithText(name).assertIsDisplayed()
        checkText(rule.onNodeWithText(name, useUnmergedTree = true))
        rule.onNodeWithTag("Teacher.Back").assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp)
        capture("$prefix-header-200")
        listOf(listOf(0, 1, 2, -1), listOf(0, 1), listOf(0, 2)).forEachIndexed { filter, expected ->
            rule.onNodeWithTag("Teacher.List").performScrollToIndex(1)
            rule.onNodeWithTag("Teacher.Segment.$filter").performClick().assertIsSelected()
                .assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp)
            listOf("Обе", "Нечётная", "Чётная").forEachIndexed { index, text ->
                val segment = rule.onNodeWithTag("Teacher.Segment.$index")
                if (index != filter) segment.assertIsNotSelected()
                segment.assertTextEquals(text)
            }
            capture("$prefix-$filter-segment-200")
            rule.onNodeWithTag("Teacher.List").performScrollToIndex(2)
            val card = hasAnyAncestor(hasTestTag("Teacher.Day.1"))
            val rows = rule.onAllNodes(card and SemanticsMatcher("lesson row") {
                it.config.getOrElse(SemanticsProperties.TestTag) { "" }.startsWith("Teacher.Row.")
            }, true)
            rows.assertCountEquals(expected.size)
            expected.forEachIndexed { rowIndex, parity ->
                val tag = "Teacher.Row.1.$rowIndex"
                rule.onNodeWithTag(tag, true).performScrollTo().assertIsDisplayed()
                val scoped = hasAnyAncestor(hasTestTag(tag))
                listOf("09:00", "Математика", "А863С", "Учебная лаборатория ГК", labels.getValue(parity)).forEach { text ->
                    val node = rule.onNode(scoped and hasText(text), true)
                    node.assertIsDisplayed()
                    checkText(node)
                }
                val mine = rule.onNode(scoped and hasText("Моя группа"), true)
                if (parity == 2) mine.assertDoesNotExist() else {
                    mine.assertIsDisplayed()
                    checkText(mine)
                }
                capture("$prefix-$filter-row-$rowIndex-200")
            }
        }
        rule.onNodeWithTag("Teacher.List").performScrollToIndex(0)
        rule.onNodeWithTag("Teacher.Back").performClick()
        rule.onNodeWithTag("Teachers.Row.mine").assertIsDisplayed()
        rule.onNodeWithTag("Teachers.Row.other").assertDoesNotExist()
        rule.onNodeWithTag("Teachers.OnlyMine").performClick()
        rule.onNodeWithTag("Teachers.Row.other").assertExists()
        rule.onNodeWithTag("Teachers.Search").performTextInput("Петров")
        rule.onNodeWithTag("Teachers.Row.other").assertExists()
        rule.onNodeWithTag("Teachers.Row.mine").assertDoesNotExist()
        rule.onNodeWithTag("Teachers.OnlyMine").performClick()
        rule.onNodeWithTag("Teachers.Row.other").assertDoesNotExist()
        rule.onNodeWithTag("Teachers.Search").performTextClearance()
        rule.onNodeWithTag("Teachers.Row.mine").assertExists().performClick()
        rule.onNodeWithTag("Teacher.Back").assertExists()
        rule.runOnIdle { rule.activity.onBackPressedDispatcher.onBackPressed() }
        rule.onNodeWithTag("Teachers.Search").assertExists()
        capture("$prefix-back-list-200")
    }

    private fun checkText(node: SemanticsNodeInteraction) {
        val semantics = node.fetchSemanticsNode()
        rule.runOnIdle { RenderedTextEvidence.verify(RenderedTextEvidence.capture(semantics), 2f) }
    }

    private fun capture(name: String) {
        val png = Frames.capture(rule.activity, "task5-$name")
        UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
            .dumpWindowHierarchy(File(png.parentFile, "task5-$name.xml"))
    }

    private fun show(theme: ThemeChoice) {
        val copy = AndroidUiCopy(rule.activity)
        rule.setContent {
            var state by remember { mutableStateOf(TeachersUiState(loaded = true, total = 2,
                list = teachers.take(1), selected = teachers.first(),
                details = TeacherDetailsComposer.compose(fixture, 0, "mine", copy))) }
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, 2f)) {
                ZaparaTheme(theme, MotionSettings.Off) {
                    Box(Modifier.widthIn(max = 320.dp).fillMaxSize().background(Zapara.colors.canvas)
                        .semantics { testTagsAsResourceId = true }) {
                        TeachersSection(state) { event ->
                            state = when (event) {
                                is TeachersEvent.Parity -> state.copy(parityFilter = event.index,
                                    details = TeacherDetailsComposer.compose(fixture, event.index, "mine", copy))
                                TeachersEvent.Back -> state.copy(selected = null, details = emptyList())
                                is TeachersEvent.Open -> state.copy(selected = teachers.first { it.id == event.id },
                                    details = TeacherDetailsComposer.compose(fixture, state.parityFilter, "mine", copy))
                                is TeachersEvent.Query -> state.copy(query = event.value)
                                is TeachersEvent.OnlyMine -> state.copy(onlyMine = event.value)
                            }
                            state = state.copy(list = teachers.filter {
                                (!state.onlyMine || it.isMine) && it.name.contains(state.query, ignoreCase = true)
                            })
                        }
                    }
                }
            }
        }
    }
}
