package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.sp
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.runners.Parameterized
import ru.bgtu_voenmeh.zapara.ui.schedule.*
import ru.bgtu_voenmeh.zapara.ui.week.*
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.LocalDate

/** Actual production components and painted paragraphs; fixtures never touch Room. */
@RunWith(Parameterized::class)
class ScheduleWeekWidthTest(private val width: Int, private val scale: Float, private val theme: ThemeChoice) {
    @get:Rule val rule = createAndroidComposeRule<ComponentActivity>()
    private var density = 1f

    @Test fun homework_words_and_actions() {
        var toggles = 0
        var rooms = 0
        var edits = 0
        var done by mutableStateOf(false)
        show {
            Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(Zapara.space.l)) {
                LessonCard(LessonUi(0, "09:00", "10:35", "лекция", "ВЫСШ. МАТЕМАТ", null,
                    "Барт Е.Л.", "493 ГК", "493", "16.09",
                    listOf(HomeworkRowUi(7, "прочитать §5", "просрочено", "overdue", done)),
                    emptyList(), false, "Математика", "математика"),
                    { edits++ }, { rooms++ }, { assertEquals(7L, it); toggles++; done = !done })
            }
        }
        painted("прочитать §5", 15)
        painted("просрочено", 12)
        target("Homework.Done.7").performClick()
        rule.runOnIdle { assertEquals(1, toggles); assertTrue(done); assertEquals(0, edits) }
        rule.onNodeWithTag("Homework.Done.7").assertIsOn()
        target("Lesson.Room.0").performClick()
        rule.runOnIdle { assertEquals(1, rooms); assertEquals(1, toggles) }
        rule.onNodeWithText("ВЫСШ. МАТЕМАТ", true).performScrollTo().performTouchInput { longClick() }
        rule.runOnIdle { assertEquals(1, edits); assertEquals(1, rooms); assertEquals(1, toggles) }
    }

    @Test fun week_clock_and_subject_are_readable() {
        val date = LocalDate.of(2026, 9, 14)
        val opened = mutableListOf<LocalDate>()
        show {
            WeekSection(WeekUiState(true, true, 1, 1, listOf(
                WeekDayUi(1, "Понедельник · 14.09", date,
                    listOf(WeekRowUi("09:00", "ВЫСШ. МАТЕМАТ", "493 ГК")), true))), {}, { opened += it })
        }
        painted("09:00", 12, clock = true)
        painted("ВЫСШ. МАТЕМАТ", 15)
        painted("493 ГК", 12)
        rule.onNodeWithTag("Week.Day.1").performClick()
        rule.runOnIdle { assertEquals(listOf(date), opened) }
    }

    private fun painted(text: String, size: Int, clock: Boolean = false) {
        val node = rule.onNodeWithText(text, useUnmergedTree = true).performScrollTo().assertIsDisplayed().fetchSemanticsNode()
        rule.runOnIdle {
            val snapshot = RenderedTextEvidence.capture(node)
            RenderedTextEvidence.verify(snapshot, scale)
            assertEquals("No font suppression", size.sp, snapshot.raw.layoutInput.style.fontSize)
            if (clock) assertEquals("HH:mm must stay atomic", 1, snapshot.paragraph.lineCount)
        }
    }

    private fun target(tag: String): SemanticsNodeInteraction {
        val target = rule.onNodeWithTag(tag).performScrollTo().assertIsDisplayed()
        val bounds = target.fetchSemanticsNode().boundsInRoot
        val host = rule.onNodeWithTag("ScheduleWeek.Host").fetchSemanticsNode().boundsInRoot
        assertTrue("48dp width", bounds.width + .5f >= 48 * density)
        assertTrue("48dp height", bounds.height + .5f >= 48 * density)
        assertTrue("Control stays inside host", bounds.left >= host.left && bounds.right <= host.right &&
            bounds.top >= host.top && bounds.bottom <= host.bottom)
        return target
    }

    private fun show(content: @Composable () -> Unit) {
        rule.setContent {
            BoxWithConstraints(Modifier.fillMaxSize()) {
                val controlled = Density(constraints.maxWidth.toFloat() / width, scale)
                SideEffect { density = controlled.density }
                CompositionLocalProvider(LocalDensity provides controlled) {
                    ZaparaTheme(theme, MotionSettings.Off) {
                        Box(Modifier.fillMaxSize().testTag("ScheduleWeek.Host")) { content() }
                    }
                }
            }
        }
        rule.waitUntil(10_000) { rule.activity.hasWindowFocus() }
        rule.waitForIdle()
        val bounds = rule.onNodeWithTag("ScheduleWeek.Host").fetchSemanticsNode().boundsInRoot
        assertEquals(width.toFloat(), bounds.width / density, .5f)
    }

    companion object {
        @JvmStatic @Parameterized.Parameters(name = "{0}dp-{1}-{2}")
        fun cases(): List<Array<Any>> = listOf(360, 411).flatMap { width ->
            listOf(1f, 1.5f, 2f).flatMap { scale ->
                listOf(ThemeChoice.Light, ThemeChoice.Dark).map { arrayOf(width, scale, it) }
            }
        }
    }
}
