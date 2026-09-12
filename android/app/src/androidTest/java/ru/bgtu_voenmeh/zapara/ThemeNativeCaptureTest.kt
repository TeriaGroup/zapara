package ru.bgtu_voenmeh.zapara

import android.provider.Settings
import android.graphics.BitmapFactory
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.*
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.ViewRootForTest
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.semantics.getAllSemanticsNodes
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.sp
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.text.font.FontWeight
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.By
import androidx.test.uiautomator.Until
import androidx.test.uiautomator.Direction
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import ru.bgtu_voenmeh.zapara.ui.friends.*
import ru.bgtu_voenmeh.zapara.ui.homework.*

/** Shared real-platform driver and additional system-setting verification on API37. */
internal object NativeTextChecks {
    @OptIn(ExperimentalComposeUiApi::class)
    internal fun assertTextLayouts(activity: ComponentActivity, scale: Float = activity.resources.configuration.fontScale) {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        instrumentation.runOnMainSync {
            var checked = 0
            fun visit(view: android.view.View) {
                if (view is ViewRootForTest) {
                    view.semanticsOwner.getAllSemanticsNodes(mergingEnabled = false).forEach { node ->
                        val action = node.config.getOrElseNullable(SemanticsActions.GetTextLayoutResult) { null }
                        if (action != null || node.config.contains(androidx.compose.ui.semantics.SemanticsProperties.Text) ||
                            node.config.contains(androidx.compose.ui.semantics.SemanticsProperties.EditableText)) {
                            RenderedTextEvidence.check(node, scale)
                            checked++
                        }
                    }
                }
                if (view is android.view.ViewGroup) repeat(view.childCount) { visit(view.getChildAt(it)) }
            }
            val roots = if (android.os.Build.VERSION.SDK_INT >= 29)
                android.view.inspector.WindowInspector.getGlobalWindowViews() else listOf(activity.window.decorView)
            val current = roots.filter { it.hasWindowFocus() }
            assertEquals("One focused window required", 1, current.size)
            assertTrue("Window belongs to current host", current.single() === activity.window.decorView || ownsContext(activity, current.single().context))
            current.forEach { visit(it) }
            assertTrue("No foreground text evidence", checked > 0)
        }
    }
}

class ThemeNativeCaptureTest {
    private fun assertTextLayouts(activity: ComponentActivity) = NativeTextChecks.assertTextLayouts(activity)
    private fun capture(choice: ThemeChoice, scale: Float, expected: ZaparaColors = if (choice == ThemeChoice.Dark) DarkColors else LightColors) {
        OwnedTestHost.launch().use { host -> NativeShowcaseDriver.capture(host, choice, scale, expected) }
    }

    @Test fun accessibility_dark_100() = accessibility(ThemeChoice.Dark)
    @Test fun accessibility_light_100() = accessibility(ThemeChoice.Light)
    @Test fun editors_dark() = editors(ThemeChoice.Dark)
    @Test fun editors_light() = editors(ThemeChoice.Light)

    private fun editors(choice: ThemeChoice) {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val device = UiDevice.getInstance(instrumentation)
        val picked = AtomicInteger(0)
        val increments = AtomicInteger(0)
        val decrements = AtomicInteger(0)
        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            lateinit var activity: ComponentActivity
            scenario.onActivity { host ->
                activity = host
                host.setContent {
                    ZaparaTheme(choice, MotionSettings.Off) {
                        var editor by remember { mutableStateOf(FriendEditorUi(null, null, "ИВТ-123", "", 0)) }
                        FriendsSection(FriendsUiState(editor = editor)) { event ->
                            if (event is FriendsEvent.EditorColor) {
                                editor = editor.copy(colorIndex = event.index)
                                picked.set(event.index)
                            }
                        }
                    }
                }
            }
            val density = activity.resources.displayMetrics.density
            val scale = (activity.resources.configuration.fontScale * 100).toInt()
            val name = "task2-${choice.name.lowercase()}-$scale"
            device.wait(Until.hasObject(By.res("Editor.Color.0")), 5000)
            listOf("Оранжевый", "Зелёный", "Синий", "Фиолетовый", "Розовый").forEachIndexed { index, color ->
                val node = requireNotNull(device.findObject(By.desc("Цвет группы: $color")))
                assertTrue(node.visibleBounds.width() / density >= 48f)
                assertTrue(node.visibleBounds.height() / density >= 48f)
                node.click()
                instrumentation.waitForIdleSync()
                assertEquals(index, picked.get())
                assertTrue(requireNotNull(device.findObject(By.res("Editor.Color.$index"))).isChecked)
            }
            Frames.capture(activity, "$name-friends")
            assertTextLayouts(activity)
            scenario.onActivity { host ->
                host.setContent {
                    ZaparaTheme(choice, MotionSettings.Off) {
                        var text by remember { mutableStateOf("") }
                HomeworkEditorSheet(HomeworkEditorState(null, "Математика", "Математика", text, 1, false) { _, _ -> null },
                            { text = it }, { increments.incrementAndGet() }, { decrements.incrementAndGet() }, {}, {})
                    }
                }
            }
            assertTrue(device.wait(Until.hasObject(By.desc("Увеличить число занятий до срока")), 5000))
            val field = requireNotNull(device.wait(Until.findObject(By.res("Editor.Text")), 5000))
            field.click()
            device.waitForIdle(2000)
            device.executeShellCommand("input text 123")
            device.waitForIdle(2000)
            assertTrue("Text input reached editor", device.wait(Until.hasObject(By.text("123")), 5000))
            Frames.capture(activity, "$name-homework-ime-before")
            assertTextLayouts(activity)
            listOf("Уменьшить число занятий до срока", "Увеличить число занятий до срока").forEach { label ->
                val node = requireNotNull(device.findObject(By.desc(label)))
                assertTrue(node.visibleBounds.width() / density >= 48f)
                assertTrue(node.visibleBounds.height() / density >= 48f)
                node.click()
            }
            instrumentation.waitForIdleSync()
            assertEquals(1, increments.get())
            assertEquals(1, decrements.get())
            Frames.capture(activity, "$name-homework-ime")
            device.pressBack()
            instrumentation.waitForIdleSync()
            Frames.capture(activity, "$name-homework")
            assertTextLayouts(activity)
        }
    }

    internal fun accessibility(choice: ThemeChoice) {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            lateinit var activity: ComponentActivity
            scenario.onActivity { host ->
                activity = host
                host.setContent { ZaparaTheme(choice, MotionSettings.Off) { AccessibilityShowcase() } }
            }
            instrumentation.waitForIdleSync()
            val device = UiDevice.getInstance(instrumentation)
            device.wait(Until.hasObject(By.text("Преподаватели")), 5000)
            val scale = activity.resources.configuration.fontScale
            val density = activity.resources.displayMetrics.density
            val name = "task2-${choice.name.lowercase()}-${(scale * 100).toInt()}"
            Frames.capture(activity, name)
            assertTextLayouts(activity)
            device.dumpWindowHierarchy(java.io.File(activity.getExternalFilesDir("frames"), "$name.xml"))
            val labels = listOf("Nav.Schedule", "Nav.Maps", "Nav.Homework", "Nav.Sections",
                "Accessibility.Segments.0", "Accessibility.Segments.1", "Accessibility.Segments.2", "Top.GroupChip", "Accessibility.Icon")
            val bounds = labels.map { label ->
                val node = requireNotNull(device.findObject(By.res(label))) { "Missing $label" }
                // Compose omits ACTION_CLICK for an already selected accessibility item.
                assertTrue("$label actionable or selected", node.isClickable || node.isSelected || node.isChecked)
                assertTrue("$label width", node.visibleBounds.width() / density >= 48f)
                assertTrue("$label height", node.visibleBounds.height() / density >= 48f)
                node.visibleBounds
            }
            if (scale >= 1.5f) assertTrue("Navigation must have two rows", bounds[2].top >= bounds[0].bottom)
            labels.take(7).forEach { tag ->
                requireNotNull(device.findObject(By.res(tag))).click()
                instrumentation.waitForIdleSync()
                val selected = requireNotNull(device.findObject(By.res(tag)))
                assertTrue("$tag selected", selected.isSelected || selected.isChecked)
            }
            Frames.capture(activity, "$name-selected")
            assertTextLayouts(activity)
        }
    }

    @Test fun dark_pixels() = capture(ThemeChoice.Dark, 1f)
    @Test fun light_pixels() = capture(ThemeChoice.Light, 1f)
    @Test fun dark_large_pixels() = capture(ThemeChoice.Dark, 1.3f)
    @Test fun light_large_pixels() = capture(ThemeChoice.Light, 1.3f)

    @Test fun system_choice_follows_device_night_mode() {
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        val original = device.executeShellCommand("cmd uimode night").trim().substringAfter("Night mode: ")
        check(original in setOf("yes", "no", "auto", "custom")) { "Unknown night mode: $original" }
        try {
            device.executeShellCommand("cmd uimode night yes")
            capture(ThemeChoice.System, 1f, DarkColors)
            device.executeShellCommand("cmd uimode night no")
            capture(ThemeChoice.System, 1f, LightColors)
        } finally {
            device.executeShellCommand("cmd uimode night $original")
            assertEquals("Night mode: $original", device.executeShellCommand("cmd uimode night").trim())
        }
    }

    @Test fun observes_system_motion_changes_and_app_preference() {
        observeMotionChanges()
    }
}

internal object NativeShowcaseDriver {
    internal fun capture(host: OwnedTestHost, choice: ThemeChoice, scale: Float, expected: ZaparaColors = if (choice == ThemeChoice.Dark) DarkColors else LightColors) {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val composed = CountDownLatch(1)
        val palette = AtomicReference<ZaparaColors>()
        val clicked = AtomicBoolean(false)
        val scenario = host.scenario
        val activity = host.activity
        run {
            scenario.onActivity { host ->
                host.setContent {
                    val density = LocalDensity.current
                    CompositionLocalProvider(LocalDensity provides Density(density.density, scale)) {
                        ZaparaTheme(choice, MotionSettings.Off) {
                            val c = Zapara.colors
                            assertEquals(Inter, Zapara.typography.body.fontFamily)
                            assertEquals(22.sp, Zapara.typography.title.fontSize)
                            assertEquals(FontWeight.Medium, Zapara.typography.section.fontWeight)
                            assertEquals(19.5.sp, Zapara.typography.body.lineHeight)
                            assertFalse(Zapara.motion.enabled)
                            Showcase { clicked.set(true) }
                            SideEffect { palette.set(c); composed.countDown() }
                        }
                    }
                }
            }
            assertTrue("Composition did not complete", composed.await(10, TimeUnit.SECONDS))
            instrumentation.waitForIdleSync()
            val device = UiDevice.getInstance(instrumentation)
            host.awaitForeground()
            assertEquals(expected, palette.get())
            host.await("showcase heading in owned root", 5000) { device.hasObject(By.text("Компоненты оформления")) }
            val suffix = if (expected.isDark) "dark" else "light"
            val variant = if (choice == ThemeChoice.System) "system-" else "${(scale * 100).toInt()}-"
            Frames.capture(activity, "theme-native-$variant$suffix-top")
            NativeTextChecks.assertTextLayouts(activity, scale)
            val button = requireNotNull(device.wait(Until.findObject(By.text("Сохранить")), 5000))
            // Accessibility may expose the label node; clickable ancestor is the actual hit target.
            var target = button
            while (!target.isClickable && target.parent != null) target = target.parent
            assertTrue(target.isClickable)
            val density = activity.resources.displayMetrics.density
            assertTrue(target.visibleBounds.width() / density >= 48f)
            assertTrue(target.visibleBounds.height() / density >= 48f)
            val buttonBounds = target.visibleBounds
            val rootLocation = IntArray(2)
            instrumentation.runOnMainSync {
                activity.findViewById<android.view.View>(android.R.id.content).getLocationOnScreen(rootLocation)
            }
            target.click()
            instrumentation.waitForIdleSync()
            assertTrue("Native input did not invoke onClick", clicked.get())
            val pressed = Frames.capture(activity, "theme-native-$variant$suffix-pressed")
            val pressedBitmap = BitmapFactory.decodeFile(pressed.path)
            assertEquals("Motion Off must not start a ripple", expected.accent.toArgb(), pressedBitmap.getPixel(
                buttonBounds.centerX() - rootLocation[0], buttonBounds.top + (4 * density).toInt() - rootLocation[1]))
            pressedBitmap.recycle()
            device.waitForIdle(2000)
            var disabled = requireNotNull(device.findObject(By.text("Недоступно")))
            while (disabled.isEnabled && disabled.parent != null) disabled = disabled.parent
            assertFalse(disabled.isEnabled)
            for (i in 1..27) {
                var icon = device.findObject(By.desc("Иконка $i"))
                while (icon == null || icon!!.visibleBounds.isEmpty) {
                    host.remainingMillis()
                    val scroll = requireNotNull(device.findObject(By.scrollable(true))) { "Missing scroll region for icon $i" }
                    val more = scroll.scroll(Direction.DOWN, 0.4f)
                    icon = device.findObject(By.desc("Иконка $i"))
                    if (!more && (icon == null || icon!!.visibleBounds.isEmpty)) fail("Icon $i absent at end of scroll region")
                }
                assertNotNull("Icon $i not visible after scrolling", icon)
                assertFalse(requireNotNull(icon).visibleBounds.isEmpty)
            }
            val file = Frames.capture(activity, "theme-native-$variant$suffix")
            assertTrue(file.length() > 1000)
            val bitmap = BitmapFactory.decodeFile(file.path)
            assertEquals(expected.canvas.toArgb(), bitmap.getPixel(0, 0))
            bitmap.recycle()
        }
    }

}

private fun observeMotionChanges() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val resolver = instrumentation.targetContext.contentResolver
        val original = Settings.Global.getString(resolver, Settings.Global.ANIMATOR_DURATION_SCALE)
        val device = UiDevice.getInstance(instrumentation)
        val current = AtomicReference<MotionSettings>()
        val changed = AtomicReference(CountDownLatch(1))
        val appSwitch = mutableStateOf(true)
        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            try {
                device.executeShellCommand("settings put global animator_duration_scale 1")
                scenario.onActivity { host ->
                    host.setContent {
                        ZaparaTheme(ThemeChoice.Dark, MotionSettings(appSwitch.value, 1f)) {
                            val motion = Zapara.motion
                            SideEffect { current.set(motion); changed.get().countDown() }
                        }
                    }
                }
                assertTrue(changed.get().await(10, TimeUnit.SECONDS))
                assertTrue(current.get().enabled)
                changed.set(CountDownLatch(1))
                device.executeShellCommand("settings put global animator_duration_scale 0")
                assertTrue(changed.get().await(10, TimeUnit.SECONDS))
                assertFalse(current.get().enabled)
                assertEquals(0, current.get().ms(Durations.theme))
                changed.set(CountDownLatch(1))
                device.executeShellCommand("settings put global animator_duration_scale 1")
                assertTrue(changed.get().await(10, TimeUnit.SECONDS))
                assertTrue(current.get().enabled)
                changed.set(CountDownLatch(1))
                instrumentation.runOnMainSync { appSwitch.value = false }
                assertTrue(changed.get().await(10, TimeUnit.SECONDS))
                assertFalse(current.get().enabled)
            } finally {
                device.executeShellCommand(if (original == null) "settings delete global animator_duration_scale"
                    else "settings put global animator_duration_scale $original")
                assertEquals(original, Settings.Global.getString(resolver, Settings.Global.ANIMATOR_DURATION_SCALE))
                runCatching { scenario.onActivity { it.finish() } }
            }
        }
    }
