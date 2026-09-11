package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.widthIn
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.ui.AndroidUiCopy
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.summary.*
import ru.bgtu_voenmeh.zapara.ui.theme.*

/** Real summary components, isolated activity and memory-only data; no AppContainer or Room. */
class SummaryBreakdownTest {
    @get:Rule val rule = createAndroidComposeRule<ComponentActivity>()
    private val longRoom = "Учебная лаборатория техники"
    private val fixture = listOf(1 to 1, 1 to 2, 1 to 0, 2 to 1).map { (day, parity) ->
        Lesson(dayOfWeek = day, parity = parity, typeRaw = "лек", subjectRaw = "Математика",
            teacherRaw = "Иванов", roomRaw = longRoom, buildingRaw = "ГК")
    }

    @Test fun filters_and_counts_dark_200() = flow(ThemeChoice.Dark)
    @Test fun filters_and_counts_light_200() = flow(ThemeChoice.Light)
    @Test fun empty_rooms_light_200() {
        show(ThemeChoice.Light, listOf(fixture.first().copy(roomRaw = "", classroomRaw = "")))
        rule.onNodeWithTag("Summary.Total").assertTextEquals("1")
        rule.onNodeWithTag("Summary.List").performScrollToIndex(5)
        rule.onNodeWithText("Аудитории не указаны").assertIsDisplayed()
        checkText("Аудитории не указаны")
        Frames.capture(rule.activity, "task4-empty-light-200")
    }

    private fun flow(theme: ThemeChoice) {
        show(theme, fixture)
        val label = theme.name.lowercase()
        listOf(3, 2, 4).forEachIndexed { segment, total ->
            rule.onNodeWithTag("Summary.Segment.$segment").performClick().assertIsSelected()
            rule.onNodeWithTag("Summary.List").performScrollToIndex(0)
            rule.onNodeWithTag("Summary.Total").assertTextEquals(total.toString())
            Frames.capture(rule.activity, "task4-$label-$segment-total-200")
            rule.onNodeWithTag("Summary.List").performScrollToIndex(1)
            row("Summary.Day.1", "Понедельник", if (segment == 2) 3 else 2)
            row("Summary.Day.2", "Вторник", if (segment == 1) 0 else 1)
            checkText("Понедельник")
            checkText("Вторник")
            Frames.capture(rule.activity, "task4-$label-$segment-days-200")
            listOf("По типам", "По предметам", "По преподавателям").forEachIndexed { i, title ->
                rule.onNodeWithTag("Summary.List").performScrollToIndex(i + 2)
                rule.onNodeWithText(title).assertIsDisplayed()
                SummaryCaptureAssertions(rule).singleCountCard(title, listOf("лекция", "Математика", "Иванов")[i], total)
                checkText(title)
            }
            rule.onNodeWithTag("Summary.List").performScrollToIndex(5)
            rule.onNodeWithTag("Summary.ByRoom").assertIsDisplayed()
            row("Summary.Room.0", "$longRoom ГК", total)
            checkText("$longRoom ГК")
            Frames.capture(rule.activity, "task4-$label-$segment-rooms-200")
        }
    }

    private fun row(tag: String, name: String, count: Int) {
        SummaryCaptureAssertions(rule).taggedRow(
            if (tag.startsWith("Summary.Day.")) "Summary.ByDay" else "Summary.ByRoom", tag, name, count)
    }

    private fun checkText(text: String) {
        val node = rule.onNodeWithText(text, useUnmergedTree = true).fetchSemanticsNode()
        rule.runOnIdle { RenderedTextEvidence.verify(RenderedTextEvidence.capture(node), 2f) }
    }

    private fun show(theme: ThemeChoice, lessons: List<Lesson>) {
        val copy = AndroidUiCopy(rule.activity)
        rule.setContent {
            var segment by remember { mutableIntStateOf(2) }
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, 2f),
                LocalShellChrome provides ShellChrome("Тестовая группа", false, true) {}) {
                ZaparaTheme(theme, MotionSettings.Off) {
                    Box(Modifier.widthIn(max = 320.dp).fillMaxSize()) {
                        SummarySection(SummaryUiState(true, true, segment,
                            SummaryComposer.tiles(segment, lessons, { _, _ -> "" }, copy))) {
                            if (it is SummaryEvent.Segment) segment = it.index
                        }
                    }
                }
            }
        }
    }
}
