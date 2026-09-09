package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Box
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
import ru.bgtu_voenmeh.zapara.ui.homework.GroupStatus
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEvent
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkGroupUi
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkItemUi
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkSection
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkUiState
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

@OptIn(ExperimentalComposeUiApi::class)
class HomeworkSectionTest {
    private val item = HomeworkItemUi(7, "Матан", "§5", "срок 21.09 (Пн)", "far", false)
    private val done = HomeworkItemUi(8, "История", "конспект", "сдано", "done", true)
    private val groups = listOf(
        HomeworkGroupUi(GroupStatus.Later, "Дальше", listOf(item), false),
        HomeworkGroupUi(GroupStatus.Done, "Сдано", listOf(done), true)
    )

    @Test fun list_and_done_group() {
        val events = CopyOnWriteArrayList<HomeworkEvent>()
        launch(ThemeChoice.Dark, HomeworkUiState(true, true, groups), events) { activity, device ->
            val pkg = installedPackage
            assertTrue(device.wait(Until.hasObject(By.res("Homework.Row.7").pkg(pkg)), 10_000))
            assertTrue(device.wait(Until.hasObject(By.res("Homework.Group.Done").pkg(pkg)), 10_000))
            val group = requireNotNull(device.findObject(By.res("Homework.Group.Done").pkg(pkg)))
            val bounds = group.visibleBounds
            device.click(bounds.centerX(), bounds.centerY())
            device.waitForIdle(2000)
            assertTrue(events.any { it is HomeworkEvent.ToggleGroup && it.status == GroupStatus.Done })
            Frames.capture(activity, "homework-list-dark")
        }
    }

    @Test fun empty_and_light() {
        launch(ThemeChoice.Dark, HomeworkUiState(true, true, emptyList()), CopyOnWriteArrayList()) { activity, _ ->
            Frames.capture(activity, "homework-empty-dark")
        }
        launch(ThemeChoice.Light, HomeworkUiState(true, true, groups), CopyOnWriteArrayList()) { activity, _ ->
            Frames.capture(activity, "homework-list-light")
        }
    }

    private fun launch(
        choice: ThemeChoice,
        state: HomeworkUiState,
        events: MutableList<HomeworkEvent>,
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
                            Box(Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }) {
                                HomeworkSection(state) { events += it }
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
