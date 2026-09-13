@file:Suppress("INVISIBLE_MEMBER", "INVISIBLE_REFERENCE")

package ru.bgtu_voenmeh.zapara

import android.graphics.BitmapFactory
import android.os.Build
import androidx.activity.compose.setContent
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.ViewRootForTest
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import java.io.File
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice

/** No MainActivity, profiles or Room. Host resources supply the real system font scale. */
class UiMapsCaptureTest {
    @get:Rule val rule = createEmptyComposeRule()

    @Test fun captureScenario() {
        val args = InstrumentationRegistry.getArguments()
        val scenario = requireNotNull(args.getString("scenario")) { "scenario is mandatory" }
        val themeName = requireNotNull(args.getString("theme")) { "theme is mandatory" }
        val theme = when (themeName) {
            "dark" -> ThemeChoice.Dark
            "light" -> ThemeChoice.Light
            else -> throw AssertionError("Unknown theme: $themeName")
        }
        val scaleText = requireNotNull(args.getString("fontScale")) { "fontScale is mandatory" }
        assertTrue("Unknown fontScale: $scaleText", scaleText in listOf("1.0", "1.5", "2.0"))
        assertEquals("Only approved API37", 37, Build.VERSION.SDK_INT)
        assertTrue("Unknown or not yet implemented scenario", scenario in listOf("maps-no-route", "primitives", "summary", "homework"))
        val oldNetwork = ScheduleRepository.networkEnabled
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        val original = device.executeShellCommand("settings get system font_scale").trim()
        require(original.toFloatOrNull()?.let { it.isFinite() && it > 0f } == true) { "Invalid baseline font: $original" }
        var failure: Throwable? = null
        try {
            ScheduleRepository.networkEnabled = false
            FontScaleReadiness.setAndAwait(scaleText)
            UiMapsCaptureFixtures().use { fixture ->
                fixture.prepare(scenario)
                OwnedTestHost.launch().use { host ->
                    FontScaleReadiness.awaitHost(host, scaleText.toFloat())
                    var composed = Float.NaN
                    host.scenario.onActivity { activity ->
                        assertEquals(scaleText.toFloat(), activity.resources.configuration.fontScale, 0.001f)
                        activity.setContent {
                            val density = LocalDensity.current
                            SideEffect { composed = density.fontScale }
                            fixture.content(scenario, theme)
                        }
                    }
                    rule.waitForIdle()
                    rule.runOnIdle { assertEquals(scaleText.toFloat(), composed) }
                    host.awaitForeground()
                    if (scenario == "primitives") {
                        capturePrimitives(host, fixture, themeName, scaleText)
                    } else if (scenario == "summary") {
                        val summary = SummaryCaptureActions(rule)
                        summary.capture(fixture) { state, action ->
                            capture(host, fixture, scenario, themeName, scaleText, state, action, summary)
                        }
                    } else if (scenario == "homework") {
                        val homework = HomeworkCaptureActions(rule)
                        homework.capture(fixture) { state, action ->
                            capture(host, fixture, scenario, themeName, scaleText, state, action, homework = homework)
                        }
                    } else {
                    rule.onNodeWithTag("Maps.From").assertExists().assertHasClickAction()
                    rule.onNodeWithTag("Maps.StepNext").assertDoesNotExist()
                    rule.onNodeWithTag("Maps.MapUnavailable").assertDoesNotExist()
                    assertNull(fixture.current.route)
                    capture(host, fixture, scenario, themeName, scaleText, "normal")
                    rule.onNodeWithTag("Maps.From").performScrollTo().performClick()
                    rule.runOnIdle { assertNotNull(fixture.current.picker); assertNull(fixture.current.route) }
                    capture(host, fixture, scenario, themeName, scaleText, "picker")
                    }
                }
            }
        } catch (error: Throwable) {
            failure = error
            throw error
        } finally {
            restoreCaptureState(failure,
                network = {
                    ScheduleRepository.networkEnabled = oldNetwork
                    assertEquals(oldNetwork, ScheduleRepository.networkEnabled)
                },
                font = { FontScaleReadiness.setAndAwait(original) })
        }
    }

    private fun capturePrimitives(host: OwnedTestHost, fixture: UiMapsCaptureFixtures, theme: String, scale: String) {
        fun shot(state: String, action: () -> Unit = {}) = capture(host, fixture, "primitives", theme, scale, state, action)
        val portion = InstrumentationRegistry.getArguments().getString("portion") ?: "top"
        require(portion in listOf("top", "remaining"))
        if (portion == "top") {
            shot("top") {
                rule.onNodeWithTag("Top.GroupChip").assertIsDisplayed()
                rule.onNodeWithTag("Accessibility.Segments.0").assertIsSelected()
            }
            return
        }
        shot("selected") {
        listOf("Nav.Schedule", "Nav.Maps", "Nav.Homework", "Nav.Sections").forEach {
            rule.onNodeWithTag(it).performClick().assertIsSelected()
        }
        (0..2).forEach { rule.onNodeWithTag("Accessibility.Segments.$it").performScrollTo().performClick().assertIsSelected() }
        }
        shot("chip") {
        rule.onNodeWithTag("Capture.Chip").performScrollTo().performClick()
        rule.runOnIdle { assertTrue(fixture.chip) }
        rule.onNodeWithText("Группа выбрана").assertIsDisplayed()
        }
        shot("button") {
        rule.onNodeWithTag("Capture.Button").performScrollTo().performClick()
        rule.runOnIdle { assertEquals(1, fixture.clicks) }
        }
        shot("focus") {
        rule.onNodeWithTag("Capture.Ghost").performScrollTo().performSemanticsAction(SemanticsActions.RequestFocus)
        rule.onNodeWithTag("Capture.Ghost").assertIsFocused()
        }
        shot("switch") {
        rule.onNodeWithTag("Capture.Switch").performScrollTo().assertIsOff().performClick().assertIsOn()
        rule.runOnIdle { assertTrue(fixture.switched) }
        }
        shot("icon") {
        rule.onNodeWithTag("Capture.BottomIcon").performScrollTo().performClick()
        rule.runOnIdle { assertEquals(2, fixture.clicks) }
        }
        shot("bottom") {
        rule.onNodeWithTag("Capture.BottomIcon").assertIsDisplayed()
        rule.onNodeWithTag("Capture.Disabled").assertIsNotEnabled()
        }
    }

    private fun capture(host: OwnedTestHost, fixture: UiMapsCaptureFixtures,
        scenario: String, theme: String, scale: String, substate: String, action: () -> Unit = {}, summary: SummaryCaptureActions? = null,
        homework: HomeworkCaptureActions? = null) {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val device = UiDevice.getInstance(instrumentation)
        val args = InstrumentationRegistry.getArguments()
        val runId = requireNotNull(args.getString("captureId"))
        require(runId.matches(Regex("[a-zA-Z0-9-]+")))
        val directory = File(instrumentation.targetContext.getExternalFilesDir(null), "ui-maps-batch4/$runId")
        check(directory.isDirectory || directory.mkdirs())
        val stem = "$theme-$scale-$scenario-$substate"
        val png = File(directory, "$stem.png")
        check(!png.exists()) { "Refusing to overwrite evidence: $stem" }
        val captured = System.currentTimeMillis()
        val observations = org.json.JSONArray()
        val metadata = JSONObject().put("scenario", scenario).put("substate", substate)
            .put("theme", theme).put("fontScale", scale.toFloat()).put("captureId", runId)
            .put("testApkSha256", requireNotNull(args.getString("testApkSha256")))
            .put("hostId", host.activity.hostId).put("capturedEpochMillis", captured)
            .put("api", Build.VERSION.SDK_INT).put("textEvidence", observations)
            .put("configurationFontScale", host.activity.resources.configuration.fontScale)
        captureFrameEvidence(metadata, observe = {
            action()
            rule.waitForIdle()
            val all = rule.onAllNodes(SemanticsMatcher.keyIsDefined(SemanticsActions.GetTextLayoutResult) or
                SemanticsMatcher.keyIsDefined(SemanticsProperties.Text) or
                SemanticsMatcher.keyIsDefined(SemanticsProperties.EditableText), useUnmergedTree = true).fetchSemanticsNodes()
            val roots = rule.runOnIdle { all.mapNotNull { it.root as? ViewRootForTest }.distinct().filter { it.view.hasWindowFocus() } }
            assertEquals("One current focused root", 1, roots.size)
            val root = roots.single()
            assertTrue("Owned current host", ownsContext(host.activity, root.view.context))
            rule.runOnIdle {
                val metrics = root.view.resources.displayMetrics
                metadata.put("rootWidthPx", root.view.width).put("rootHeightPx", root.view.height)
                    .put("rootWidthDp", root.view.width / metrics.density).put("density", metrics.density)
                    .put("decorWidth", host.activity.window.decorView.width).put("decorHeight", host.activity.window.decorView.height)
                    .put("insets", root.view.rootWindowInsets.toString())
                    .put("orientation", host.activity.resources.configuration.orientation)
                assertEquals("Intended 360dp current window", 360f, root.view.width / metrics.density, 1f)
            }
            metadata.put("wmSize", device.executeShellCommand("wm size").trim())
                .put("wmDensity", device.executeShellCommand("wm density").trim())
            val nodes = all.filter { it.root === root }
            val summaryChecks = summary?.let { SummaryCaptureAssertions(rule) }
            val summaryViewport = summaryChecks?.viewport()
            val homeworkViewport = homework?.takeIf { !it.editor }?.checks?.viewport()
            assertTrue("Expected text evidence", nodes.isNotEmpty())
            var visible = 0
            nodes.forEach { node ->
                val fresh = rule.onNode(SemanticsMatcher("fresh ${node.id}") { it.id == node.id && it.root === root },
                    useUnmergedTree = true).fetchSemanticsNode()
                rule.runOnIdle {
                    val entry = JSONObject().put("nodeId", fresh.id).put("bounds", fresh.boundsInRoot.toString())
                        .put("text", fresh.config.getOrElseNullable(SemanticsProperties.Text) { null }.toString())
                    observations.put(entry)
                    if (fresh.boundsInRoot.width <= 0f || fresh.boundsInRoot.height <= 0f) {
                        entry.put("excluded", "fully offscreen; not coverage")
                    } else {
                        val snapshot = RenderedTextEvidence.capture(fresh)
                        entry.put("rawOverflow", snapshot.raw.hasVisualOverflow).put("rawSize", snapshot.raw.size.toString())
                            .put("rawConstraints", snapshot.raw.layoutInput.constraints.toString())
                            .put("paintedWidth", snapshot.paragraph.width).put("paintedHeight", snapshot.paragraph.height)
                            .put("lines", snapshot.paragraph.lineCount).put("box", snapshot.box.toString())
                            .put("renderer", snapshot.renderer.javaClass.name)
                        if (summaryChecks != null && summaryChecks.insideList(fresh) && requireNotNull(summaryViewport)
                            .crossesVerticalBoundary(SummaryBounds(snapshot.box.left, snapshot.box.top, snapshot.box.right, snapshot.box.bottom))) {
                            entry.put("excluded", "partial lazy boundary; pending, not coverage")
                        } else if (homework != null && !homework.editor && homework.checks.insideList(fresh) &&
                            requireNotNull(homeworkViewport).crossesVerticalBoundary(SummaryBounds(snapshot.box.left, snapshot.box.top, snapshot.box.right, snapshot.box.bottom))) {
                            entry.put("excluded", "partial lazy boundary; pending, not coverage")
                        } else {
                            visible++
                            RenderedTextEvidence.check(fresh, scale.toFloat())
                            entry.put("checked", true)
                        }
                    }
                }
            }
            assertTrue("Nonempty visible text evidence", visible > 0)
            if (homework != null) metadata.put("homeworkFrame", homework.observeFrame(scale.toFloat()))
            if (summary != null) metadata.put("summaryFrame", summary.observeFrame(SummaryFrameKey(
                SummaryCaptureModel.cases.single { it.segment == fixture.summary.segment }.id, theme, scale,
                "phone360-" + if (root.view.resources.configuration.orientation == 1) "portrait" else "landscape",
                metadata.getString("testApkSha256"), stem, requireNotNull(summaryChecks).rootViewport())))
        }, png = {
            assertTrue(device.takeScreenshot(png))
            val bitmap = requireNotNull(BitmapFactory.decodeFile(png.absolutePath))
            try {
            assertEquals(device.displayWidth, bitmap.width)
            assertEquals(device.displayHeight, bitmap.height)
            val colors = mutableSetOf<Int>()
            for (x in 0 until bitmap.width step 16) for (y in 0 until bitmap.height step 16) colors.add(bitmap.getPixel(x, y))
            assertTrue("Blank/compositor frame", colors.size > 32)
            metadata.put("widthPx", bitmap.width).put("heightPx", bitmap.height)
            } finally { bitmap.recycle() }
        }, xml = { device.dumpWindowHierarchy(File(directory, "$stem.xml")) }, writeMetadata = {
            val state = fixture.current
            metadata
                .put("activeFloor", JSONObject().put("building", state.building).put("floor", state.floor))
                .put("stepId", state.activeStepId ?: JSONObject.NULL).put("source", when (scenario) {
                    "primitives" -> "actual-primitives-isolated-state"
                    "summary" -> "actual-summary-section-composer-memory-fixture"
                    "homework" -> "actual-homework-section-editor-memory-fixture-no-persistence"
                    else -> "fixture-real-bundled-raster-empty-picker"
                })
                .put("versionCode", BuildConfig.VERSION_CODE).put("versionName", BuildConfig.VERSION_NAME)
                .put("capturedEpochMillis", captured).put("api", Build.VERSION.SDK_INT)
                .put("chipSelected", fixture.chip).put("switchOn", fixture.switched).put("clicks", fixture.clicks)
            if (scenario == "summary") {
                metadata.remove("activeFloor")
                metadata.remove("stepId")
                metadata.put("summarySegment", fixture.summary.segment)
                    .put("summaryTiles", JSONObject().put("total", fixture.summary.tiles.total)
                        .put("byDay", fixture.summary.tiles.byDay.toString()).put("byType", fixture.summary.tiles.byType.toString())
                        .put("bySubject", fixture.summary.tiles.bySubject.toString()).put("byTeacher", fixture.summary.tiles.byTeacher.toString())
                        .put("byRoom", fixture.summary.tiles.byRoom.toString()))
            }
            if (scenario == "homework") {
                metadata.remove("activeFloor")
                metadata.remove("stepId")
                metadata.put("homeworkSaveCallbacks", fixture.homeworkSaveCallbacks)
                    .put("savePersistenceProven", false).put("fixtureToday", HomeworkCaptureModel.today.toString())
            }
            File(directory, "$stem.json").writeText(metadata.toString(2))
        })
    }
}
