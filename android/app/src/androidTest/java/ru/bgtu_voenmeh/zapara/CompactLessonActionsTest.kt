package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertHeightIsAtLeast
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertWidthIsAtLeast
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import java.time.LocalDate
import java.time.LocalDateTime
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.schedule.DayPage
import ru.bgtu_voenmeh.zapara.ui.schedule.LessonUi
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleEvent
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleSection
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleUiState
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

class CompactLessonActionsTest {
    @get:Rule val rule = createEmptyComposeRule()
    private var host: OwnedTestHost? = null

    @After fun closeHost() { host?.close() }

    @Test fun normal_phone_keeps_three_actions_in_one_row_with_full_touch_targets() {
        checkActions(1f, ThemeChoice.Light, "101", "lesson-actions-compact-light")
    }

    @Test fun large_font_keeps_labels_and_disabled_map_without_overlapping_targets() {
        checkActions(2f, ThemeChoice.Dark, "", "lesson-actions-compact-large-dark")
    }

    private fun checkActions(fontScale: Float, theme: ThemeChoice, roomRaw: String, frame: String) {
        val date = LocalDate.of(2026, 10, 3)
        val lesson = LessonUi(index = 1, timeStart = "09:00", timeEnd = "10:35", type = "Практика",
            name = "Математика", original = null, teacher = "Иванов И. И.", room = "",
            classroomRaw = roomRaw, nextDate = null, homework = emptyList(), friends = emptyList(),
            isPast = false, subjectRaw = "Математика", subjectNorm = "математика")
        val page = DayPage(date, true, "Суббота", listOf(lesson), null, false)
        val state = ScheduleUiState(loaded = true, hasGroup = true, today = date,
            selected = date, pages = mapOf(date to page), now = LocalDateTime.of(2026, 10, 3, 8, 0))
        var mapCalls = 0
        var homework: LessonUi? = null
        var discussed: String? = null
        val current = OwnedTestHost.launch().also { host = it }
        current.scenario.onActivity { activity -> activity.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale)) {
                ZaparaTheme(theme, MotionSettings.Off) {
                    Box(Modifier.width(360.dp).fillMaxHeight()) {
                        ScheduleSection(state, onEvent = { if (it is ScheduleEvent.SubjectHomework) homework = it.lesson },
                            onDiscuss = { discussed = it }, onOpenMap = { assertEquals(roomRaw, it); mapCalls++ })
                    }
                }
            }
        } }
        rule.waitForIdle()
        val tags = listOf("Schedule.Map.1.0", "Schedule.Homework.1.0", "Schedule.Discuss.1.0")
        rule.onNodeWithTag(tags.last()).performScrollTo()
        val buttons = tags.map { rule.onNodeWithTag(it).assertIsDisplayed()
            .assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp) }
        val bounds = buttons.map { it.fetchSemanticsNode().boundsInRoot }
        current.awaitForeground()
        assertTrue(Frames.capture(current.activity, frame).length() > 1000)
        if (fontScale == 1f) assertTrue("Actions must fit one row at 360 dp: $bounds",
            bounds.maxOf { it.top } - bounds.minOf { it.top } < 1f)
        for (a in bounds.indices) for (b in a + 1 until bounds.size) {
            assertTrue("Touch targets overlap", !bounds[a].overlaps(bounds[b]))
        }
        if (roomRaw.isBlank()) buttons[0].assertIsNotEnabled().performClick()
        else buttons[0].performClick()
        buttons[1].performClick()
        buttons[2].performClick()
        rule.runOnIdle {
            assertEquals(if (roomRaw.isBlank()) 0 else 1, mapCalls)
            assertEquals(lesson, homework)
            assertEquals("Математика · 2026-10-03 · 09:00–10:35 · ", discussed)
        }
    }
}
