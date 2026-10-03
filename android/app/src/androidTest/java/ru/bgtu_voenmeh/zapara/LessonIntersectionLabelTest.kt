package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import org.junit.After
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.schedule.FriendDotUi
import ru.bgtu_voenmeh.zapara.ui.schedule.LessonCard
import ru.bgtu_voenmeh.zapara.ui.schedule.LessonUi
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

class LessonIntersectionLabelTest {
    @get:Rule val rule = createEmptyComposeRule()
    private var host: OwnedTestHost? = null

    @After fun closeHost() { host?.close() }

    @Test fun each_group_shows_its_own_schedule_intersection_even_below_dot_threshold() {
        val lesson = LessonUi(
            index = 0,
            timeStart = "09:00",
            timeEnd = "10:35",
            type = "лекция",
            name = "Математика",
            original = null,
            teacher = "",
            room = "326 ГК",
            classroomRaw = "326",
            nextDate = null,
            homework = emptyList(),
            friends = listOf(
                FriendDotUi(0, "A863C", "", -1, "Подробности A863C", intersectionScore = 75),
                FriendDotUi(1, "B999D", "", 100, "Подробности B999D", intersectionScore = 100),
                FriendDotUi(2, "C123E", "", 25, "Подробности C123E", intersectionScore = 25)
            ),
            isPast = false,
            subjectRaw = "Математика",
            subjectNorm = "математика"
        )
        val current = OwnedTestHost.launch().also { host = it }
        current.scenario.onActivity { activity ->
            activity.setContent {
                val density = LocalDensity.current
                CompositionLocalProvider(LocalDensity provides Density(density.density, 1f)) {
                    ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                        Column(Modifier.fillMaxSize().padding(16.dp)) {
                            LessonCard(lesson, onLongClick = {}, onRoom = {}, onToggleDone = {})
                        }
                    }
                }
            }
        }
        rule.waitForIdle()
        current.awaitForeground()

        rule.onNodeWithText("A863C · Один этаж").assertIsDisplayed()
        rule.onNodeWithText("B999D · Одна аудитория").assertIsDisplayed()
        rule.onNodeWithText("C123E · Занятия в одно время").assertIsDisplayed()
        rule.onNodeWithText("Подробности A863C").assertDoesNotExist()
        Frames.capture(current.activity, "lesson-intersection-kind")
    }
}
