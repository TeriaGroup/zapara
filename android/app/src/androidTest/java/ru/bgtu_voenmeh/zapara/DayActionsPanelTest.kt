package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertHeightIsAtLeast
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertWidthIsAtLeast
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import java.time.LocalDate
import java.time.LocalDateTime
import kotlin.math.abs
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.schedule.DayPage
import ru.bgtu_voenmeh.zapara.ui.schedule.HomeworkRowUi
import ru.bgtu_voenmeh.zapara.ui.schedule.LessonUi
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleSection
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleUiState
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import androidx.compose.ui.unit.dp

class DayActionsPanelTest {
    @get:Rule val rule = createEmptyComposeRule()
    private var host: OwnedTestHost? = null
    private var originalFontScale: String? = null

    @After fun closeHost() {
        try {
            host?.close()
        } finally {
            host = null
            originalFontScale?.let(FontScaleReadiness::setAndAwait)
            originalFontScale = null
        }
    }

    @Test fun expanded_day_actions_keep_two_equal_shortcuts_and_share_selected_day() {
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        originalFontScale = device.executeShellCommand("settings get system font_scale").trim().also {
            require(it.toFloatOrNull() != null)
        }
        FontScaleReadiness.setAndAwait("1.0")
        val current = OwnedTestHost.launch().also { host = it }
        FontScaleReadiness.awaitHost(current, 1f)

        val date = LocalDate.of(2026, 10, 3)
        val deadline = HomeworkRowUi(7L, "Решить задачи", "Математика", "active", false)
        val lesson = LessonUi(
            index = 1, timeStart = "09:00", timeEnd = "10:35", type = "Практика",
            name = "Математика", original = null, teacher = "", room = "101",
            classroomRaw = "101", nextDate = null, homework = emptyList(), friends = emptyList(),
            isPast = false, subjectRaw = "Математика", subjectNorm = "математика"
        )
        val page = DayPage(date, true, "Суббота", listOf(lesson), null, false,
            deadlines = listOf(deadline))
        val state = ScheduleUiState(loaded = true, hasGroup = true, today = date,
            selected = date, pages = mapOf(date to page), now = LocalDateTime.of(2026, 10, 3, 8, 0))
        var sharedPage: DayPage? = null
        var composedScale = Float.NaN
        current.scenario.onActivity { activity ->
            activity.setContent {
                val actualScale = LocalDensity.current.fontScale
                SideEffect { composedScale = actualScale }
                ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                    ScheduleSection(state, onEvent = {}, onShareDay = { sharedPage = it }, onOpenMap = {})
                }
            }
        }
        rule.waitForIdle()
        rule.runOnIdle { assertEquals(1f, composedScale) }

        rule.onNodeWithTag("Schedule.DayTools").assertIsDisplayed().performClick()
        rule.onNodeWithText("Действия дня").assertIsDisplayed()
        val currentJump = rule.onNodeWithTag("Schedule.JumpCurrent")
            .assertIsDisplayed().assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp)
        val deadlineJump = rule.onNodeWithTag("Schedule.JumpDeadlines")
            .assertIsDisplayed().assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp)
        val currentBounds = currentJump.fetchSemanticsNode().boundsInRoot
        val deadlineBounds = deadlineJump.fetchSemanticsNode().boundsInRoot
        assertTrue("Shortcut widths differ", abs(currentBounds.width - deadlineBounds.width) <= 1f)
        assertTrue("Shortcut heights differ", abs(currentBounds.height - deadlineBounds.height) <= 1f)
        assertTrue("Shortcuts are not aligned", abs(currentBounds.top - deadlineBounds.top) <= 1f)

        assertTrue(Frames.capture(current.activity, "day-actions-grouped").length() > 1000)
        rule.onNodeWithTag("Schedule.ShareDay").assertIsDisplayed().performClick()
        rule.runOnIdle { assertEquals(page, sharedPage) }
    }
}
