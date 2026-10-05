package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.schedule.DayPage
import ru.bgtu_voenmeh.zapara.ui.schedule.LessonUi
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleEvent
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleSection
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleUiState
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import java.time.LocalDate
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

@OptIn(ExperimentalComposeUiApi::class)
class ScheduleSectionTest {
    private val today = LocalDate.of(2026, 9, 8)
    private val selected = LocalDate.of(2026, 9, 7)
    private val lesson = LessonUi(
        index = 1, timeStart = "09:00", timeEnd = "10:35", type = "лекция",
        name = "Матан", original = "ВЫСШ. МАТЕМАТ", teacher = "Барт Е.Л.",
        room = "493 ГК", classroomRaw = "493;", nextDate = "14.09",
        homework = emptyList(), friends = emptyList(), isPast = true,
        subjectRaw = "лек ВЫСШ. МАТЕМАТ", subjectNorm = "лек высш. математ"
    )
    private val page = DayPage(selected, false, "Понедельник, 7 сентября · нечётная неделя · 1-я неделя", listOf(lesson), null, false)

    @Test fun pager_date_today_and_long_press() {
        val events = CopyOnWriteArrayList<ScheduleEvent>()
        launch(ThemeChoice.Dark, ScheduleUiState(true, true, today, selected, mapOf(selected to page)), events) { activity, device ->
            val pkg = installedPackage
            assertTrue(device.wait(Until.hasObject(By.res("Schedule.Date.20260907").pkg(pkg)), 10_000))
            assertTrue(device.wait(Until.hasObject(By.res("Lesson.Card.1").pkg(pkg)), 10_000))
            assertTrue(device.wait(Until.hasObject(By.res("Top.Today").pkg(pkg)), 10_000))
            val card = requireNotNull(device.findObject(By.res("Lesson.Card.1").pkg(pkg)))
            val bounds = card.visibleBounds
            device.swipe(bounds.centerX(), bounds.centerY(), bounds.centerX(), bounds.centerY(), 120)
            device.waitForIdle(2000)
            assertTrue(events.any { it is ScheduleEvent.LongPress })
            Frames.capture(activity, "schedule-day-dark")
        }
    }

    @Test fun today_hidden_when_selected_is_today() {
        launch(ThemeChoice.Light, ScheduleUiState(true, true, today, today, mapOf(today to page.copy(date = today, isToday = true))), CopyOnWriteArrayList()) { activity, device ->
            assertTrue(device.findObject(By.res("Top.Today").pkg(installedPackage)) == null)
            Frames.capture(activity, "schedule-day-light")
        }
    }

    @Test fun load_fail_shows_retry() {
        val events = CopyOnWriteArrayList<ScheduleEvent>()
        launch(
            ThemeChoice.Dark,
            ScheduleUiState(loaded = true, hasGroup = false, today = today, selected = today, error = "empty db"),
            events
        ) { activity, device ->
            val pkg = installedPackage
            assertTrue(device.wait(Until.hasObject(By.res("Empty.LoadFail").pkg(pkg)), 10_000))
            assertTrue(device.wait(Until.hasObject(By.text("Не удалось загрузить расписание")), 5_000))
            assertTrue(device.wait(Until.hasObject(By.text("Повторить")), 5_000))
            device.findObject(By.text("Повторить"))?.click()
            device.waitForIdle(2000)
            assertTrue(events.any { it is ScheduleEvent.Retry })
            Frames.capture(activity, "schedule-load-fail")
        }
    }

    @Test fun empty_day() {
        val empty = DayPage(today, true, "caption", emptyList(), "следующая пара — 14 сентября, Матан", false)
        launch(ThemeChoice.Light, ScheduleUiState(true, true, today, today, mapOf(today to empty)), CopyOnWriteArrayList()) { activity, _ ->
            Frames.capture(activity, "schedule-empty-light")
        }
    }

    @Test fun actions_sheet() {
        launch(ThemeChoice.Dark, ScheduleUiState(true, true, today, selected, mapOf(selected to page), actionsFor = lesson), CopyOnWriteArrayList()) { activity, device ->
            assertTrue(device.wait(Until.hasObject(By.res("Lesson.Actions").pkg(installedPackage)), 10_000))
            Frames.capture(activity, "schedule-actions-dark")
        }
    }

    private fun launch(
        choice: ThemeChoice,
        state: ScheduleUiState,
        events: MutableList<ScheduleEvent>,
        body: (ComponentActivity, UiDevice) -> Unit
    ) {
        val ins = InstrumentationRegistry.getInstrumentation()
        val ready = CountDownLatch(1)
        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            lateinit var activity: ComponentActivity
            scenario.onActivity { host ->
                activity = host
                host.setContent {
                    ZaparaTheme(choice) {
                        CompositionLocalProvider(LocalShellChrome provides ShellChrome("А863С · нечёт.", false, true) {}) {
                            androidx.compose.foundation.layout.Box(Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }) {
                                ScheduleSection(state, { events += it }) {}
                            }
                        }
                    }
                }
                ready.countDown()
            }
            assertTrue(ready.await(10, TimeUnit.SECONDS))
            ins.waitForIdleSync()
            body(activity, UiDevice.getInstance(ins))
        }
    }
}
