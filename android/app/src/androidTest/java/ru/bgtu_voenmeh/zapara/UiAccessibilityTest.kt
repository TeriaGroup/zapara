package ru.bgtu_voenmeh.zapara

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.activity.compose.setContent
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import androidx.compose.ui.platform.ViewRootForTest
import org.junit.After
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.runners.Parameterized
import ru.bgtu_voenmeh.zapara.ui.components.*
import ru.bgtu_voenmeh.zapara.ui.friends.*
import ru.bgtu_voenmeh.zapara.ui.homework.*
import ru.bgtu_voenmeh.zapara.ui.shell.*
import ru.bgtu_voenmeh.zapara.ui.theme.*

@RunWith(Parameterized::class)
class UiAccessibilityTest(private val theme: ThemeChoice, private val scale: Float) {
    @get:Rule val rule = createEmptyComposeRule()
    @get:Rule val testName = org.junit.rules.TestName()
    private var host: OwnedTestHost? = null
    private var frameIndex = 0

    @After fun checkedCleanup() {
        host?.close()
    }

    companion object {
        // One instrumentation scope owns the global setting. Per-case restoration races
        // the system's asynchronous persistent write with the next case's requested scale.
        @JvmField @org.junit.ClassRule val fontSetting = object : org.junit.rules.ExternalResource() {
            private var original: String? = null
            override fun before() {
                original = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
                    .executeShellCommand("settings get system font_scale").trim()
                require(original?.toFloatOrNull() != null)
            }
            override fun after() { original?.let(FontScaleReadiness::setAndAwait) }
        }
        @JvmStatic @Parameterized.Parameters(name = "{0}-{1}")
        fun variants() = listOf(ThemeChoice.Dark, ThemeChoice.Light).flatMap { theme ->
            listOf(1f, 1.5f, 2f).map { arrayOf<Any>(theme, it) }
        }
    }

    private fun content(body: @Composable () -> Unit) {
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        FontScaleReadiness.setAndAwait(scale.toString())
        val current = OwnedTestHost.launch().also { host = it }
        FontScaleReadiness.awaitHost(current, scale)
        var composedScale = Float.NaN
        current.scenario.onActivity { activity ->
            assertEquals(scale, activity.resources.configuration.fontScale)
            activity.setContent {
                val density = androidx.compose.ui.platform.LocalDensity.current
                SideEffect { composedScale = density.fontScale }
                ZaparaTheme(theme, MotionSettings.Off, content = body)
            }
        }
        rule.waitForIdle()
        rule.runOnIdle { assertEquals("Current Compose density", scale, composedScale) }
    }

    private fun target(tag: String) = rule.onNodeWithTag(tag)
        .assertHeightIsAtLeast(48.dp).assertWidthIsAtLeast(48.dp)

    private fun noOverflow(keyboard: Boolean = false) {
        val all = rule.onAllNodes(hasTextLayout() or SemanticsMatcher.keyIsDefined(SemanticsProperties.Text) or
            SemanticsMatcher.keyIsDefined(SemanticsProperties.EditableText), useUnmergedTree = true).fetchSemanticsNodes()
        val activeRoots = rule.runOnIdle { all.mapNotNull { it.root as? ViewRootForTest }.distinct().filter { it.view.hasWindowFocus() } }
        assertEquals("One current foreground Compose root required", 1, activeRoots.size)
        assertTrue("Foreground root belongs to current host", ownsContext(requireNotNull(host).activity, activeRoots.single().view.context))
        val window = activeRoots.single().view
        android.util.Log.i("RenderedText", "foreground window density=${window.resources.displayMetrics.density} fontScale=${window.resources.configuration.fontScale}")
        val nodes = all.filter { it.root === activeRoots.single() }
        assertTrue("Expected text layout evidence", nodes.isNotEmpty())
        nodes.forEach { node ->
            val interaction = rule.onNode(SemanticsMatcher("current text ${node.id}") { it.id == node.id }, useUnmergedTree = true)
            var parent = node.parent
            var scrollable = false
            while (parent != null) {
                if (parent.config.contains(SemanticsActions.ScrollBy)) scrollable = true
                parent = parent.parent
            }
            if (scrollable) interaction.performScrollTo()
            val fresh = interaction.fetchSemanticsNode()
            rule.runOnIdle { RenderedTextEvidence.check(fresh, scale) }
        }
        val name = testName.methodName.lowercase().replace(Regex("[^a-z0-9]+"), "-").trim('-')
        if (keyboard) KeyboardEvidence.requireVisible()
        Frames.capture(requireNotNull(host).activity, "strict-$name-${frameIndex++}")
        if (keyboard) KeyboardEvidence.requireVisible()
    }

    private fun uniqueName(tag: String, name: String) {
        rule.onNodeWithTag(tag).assertContentDescriptionEquals(name)
        rule.onAllNodes(hasContentDescription(name) or hasText(name), useUnmergedTree = true)
            .assertCountEquals(1)
    }

    private fun bounds(tag: String) = rule.onNodeWithTag(tag, useUnmergedTree = true)
        .fetchSemanticsNode().boundsInRoot

    private fun grid(tags: List<String>, columns: Int) {
        val cells = tags.map(::bounds)
        cells.forEachIndexed { index, cell ->
            assertTrue("$tags: nonempty cell $index", cell.width > 0f && cell.height > 0f)
            assertEquals("$tags: equal widths", cells.first().width, cell.width, 1f)
            val column = index % columns
            val rowStart = cells[index - column]
            assertEquals("$tags: aligned row top", rowStart.top, cell.top, 1f)
            assertEquals("$tags: aligned row bottom", rowStart.bottom, cell.bottom, 1f)
            assertEquals("$tags: aligned column", cells[column].left, cell.left, 1f)
            if (column > 0) assertEquals("$tags: adjacent columns", cells[index - 1].right, cell.left, 1f)
            if (index >= columns) assertEquals("$tags: adjacent rows", cells[index - columns].bottom, cell.top, 1f)
        }
    }

    private fun indicatorInside(tag: String, label: String) {
        rule.waitForIdle()
        val cell = bounds(tag)
        rule.onAllNodesWithTag("Nav.Indicator", useUnmergedTree = true).assertCountEquals(1)
        val indicator = bounds("Nav.Indicator")
        assertTrue("Indicator inside selected cell: $tag", indicator.left >= cell.left &&
            indicator.right <= cell.right && indicator.top >= cell.top && indicator.bottom <= cell.bottom)
        assertEquals("Indicator centered: $tag", cell.center.x, indicator.center.x, 1f)
        val text = rule.onNodeWithText(label, useUnmergedTree = true).fetchSemanticsNode().boundsInRoot
        assertTrue("Indicator below label: $tag", indicator.top >= text.bottom)
        val reportedX = rule.onNodeWithTag("Nav.Indicator", useUnmergedTree = true)
            .fetchSemanticsNode().config[IndicatorXKey]
        assertEquals("IndicatorXKey follows actual X", indicator.left / rule.density.density, reportedX, 1f)
    }

    @Test fun primitives_and_all_navigation_actions() {
        content { AccessibilityShowcase() }
        target("Top.GroupChip")
        val nav = listOf("Nav.Schedule", "Nav.Maps", "Nav.Homework", "Nav.Sections")
        val labels = listOf("Расписание", "Карты", "Домашка", "Разделы")
        val segments = (0..2).map { "Accessibility.Segments.$it" }
        nav.forEachIndexed { index, tag ->
            target(tag).performClick().assertIsSelected()
            nav.filter { it != tag }.forEach { rule.onNodeWithTag(it).assertIsNotSelected() }
            assertNull(rule.onNodeWithTag(tag).fetchSemanticsNode().config.getOrElseNullable(SemanticsProperties.ContentDescription) { null })
            rule.onAllNodes(hasText(labels[index]), useUnmergedTree = true).assertCountEquals(1)
            grid(nav, if (scale >= 1.5f) 2 else 4)
            indicatorInside(tag, labels[index])
        }
        segments.forEach { tag ->
            target(tag).performClick().assertIsSelected()
            segments.filter { it != tag }.forEach { rule.onNodeWithTag(it).assertIsNotSelected() }
            grid(segments, if (scale >= 1.5f) 1 else 3)
        }
        rule.onNodeWithTag("Accessibility.Icon").performScrollTo()
        target("Accessibility.Icon").assertHasClickAction()
        uniqueName("Accessibility.Icon", "Добавить группу")
        noOverflow()
    }

    @Test fun friend_switches_have_unique_names_and_toggle_state() {
        val events = mutableListOf<FriendsEvent>()
        content {
            var state by remember { mutableStateOf(FriendsUiState(
                friends = listOf(FriendUi(42L, 7, "ИВТ-123", "Тестовый друг", true, 0)))) }
            FriendsSection(state) { event ->
                events += event
                state = when (event) {
                    is FriendsEvent.Toggle -> state.copy(friends = state.friends.map { it.copy(enabled = event.enabled) })
                    is FriendsEvent.AlwaysShow -> state.copy(alwaysShow = event.value)
                    is FriendsEvent.Invert -> state.copy(invert = event.value)
                    else -> state
                }
            }
        }
        val context = androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().targetContext
        val cases = listOf(
            Triple("Friends.Enabled.7", context.getString(R.string.friends_enabled_label, "ИВТ-123"), true),
            Triple("Friends.AlwaysShow", context.getString(R.string.friends_always_show), false),
            Triple("Friends.Invert", context.getString(R.string.friends_invert), false)
        )
        cases.forEach { (tag, name, initiallyOn) ->
            rule.onNodeWithTag(tag).performScrollTo()
            uniqueName(tag, name)
            val control = target(tag).assertIsToggleable().assertHasClickAction()
                .assert(SemanticsMatcher.expectValue(SemanticsProperties.Role, androidx.compose.ui.semantics.Role.Switch))
            if (initiallyOn) control.assertIsOn() else control.assertIsOff()
            control.performClick()
            if (initiallyOn) control.assertIsOff() else control.assertIsOn()
            uniqueName(tag, name)
        }
        rule.runOnIdle {
            assertEquals(listOf(FriendsEvent.Toggle(7, false), FriendsEvent.AlwaysShow(true), FriendsEvent.Invert(true)), events)
        }
    }

    @Test fun friend_colors_are_named_selected_targets() {
        var showEditor by mutableStateOf(false)
        content {
            var editor by remember(showEditor) { mutableStateOf(if (showEditor) FriendEditorUi(null, null, "ИВТ-123", "", 0) else null) }
            FriendsSection(FriendsUiState(editor = editor)) { event ->
                if (event is FriendsEvent.EditorColor) editor = requireNotNull(editor).copy(colorIndex = event.index)
            }
        }
        noOverflow()
        rule.runOnIdle { showEditor = true }
        listOf("Оранжевый", "Зелёный", "Синий", "Фиолетовый", "Розовый").forEachIndexed { index, name ->
            target("Editor.Color.$index").assertContentDescriptionEquals("Цвет группы: $name")
                .performClick().assertIsSelected()
                .assert(SemanticsMatcher.expectValue(SemanticsProperties.StateDescription, "Выбран"))
        }
        noOverflow()
    }

    @Test fun homework_stepper_with_keyboard() {
        var increments = 0
        var decrements = 0
        content {
            var text by remember { mutableStateOf("") }
                HomeworkEditorSheet(HomeworkEditorState(null, "Математика", "Математика", text, 1, false) { _, _ -> null },
                { text = it }, { increments++ }, { decrements++ }, {}, {})
        }
        rule.onNodeWithTag("Editor.Text").performClick().performTextInput("Решить задачу")
        rule.waitUntil(5000) { KeyboardEvidence.visible() }
        target("Editor.Dec").assertContentDescriptionEquals("Уменьшить число занятий до срока").performClick()
        target("Editor.Inc").assertContentDescriptionEquals("Увеличить число занятий до срока").performClick()
        rule.runOnIdle { assertEquals(1, increments); assertEquals(1, decrements) }
        KeyboardEvidence.requireVisible()
        noOverflow(keyboard = true)
    }
}

private fun hasTextLayout() = SemanticsMatcher.keyIsDefined(SemanticsActions.GetTextLayoutResult)

@OptIn(ExperimentalComposeUiApi::class)
@Composable
internal fun AccessibilityShowcase(extraControls: @Composable () -> Unit = {}) {
    var selected by remember { mutableIntStateOf(0) }
    var section by remember { mutableStateOf(Section.Schedule) }
    var sections by remember { mutableStateOf(false) }
    CompositionLocalProvider(LocalShellChrome provides ShellChrome("ИВТ-123 — учебная группа", false, true) {}) {
        Column(Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }.background(Zapara.colors.canvas)) {
            ZTopBar("Преподаватели")
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()).padding(Zapara.space.l)) {
                ZSegmented(listOf("Обе недели", "Нечётная неделя", "Чётная неделя"), selected,
                    { selected = it }, "Accessibility.Segments")
                ZIconButton(R.drawable.ic_plus, "Добавить группу", {}, "Accessibility.Icon")
                extraControls()
            }
            ZBottomBar(section, sections, 0, false, { section = it; sections = false }, { sections = true })
        }
    }
}
