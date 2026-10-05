package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import org.junit.After
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.runners.Parameterized
import ru.bgtu_voenmeh.zapara.ui.theme.*

@RunWith(Parameterized::class)
class RenderedTextEvidenceTest(private val theme: ThemeChoice, private val scale: Float) {
    @get:Rule val rule = createEmptyComposeRule()
    private var host: OwnedTestHost? = null
    private var controlIndex = 0
    private val framePrefix get() = "text-control-${theme.name.lowercase()}-${(scale * 100).toInt()}"

    companion object {
        @JvmStatic @Parameterized.Parameters(name = "{0}-{1}")
        fun variants() = listOf(ThemeChoice.Dark, ThemeChoice.Light).flatMap { theme ->
            listOf(1f, 2f).map { arrayOf<Any>(theme, it) }
        }
    }

    @After fun destroyHost() { host?.close() }

    private fun fixture(body: @Composable () -> Unit) {
        val current = host ?: OwnedTestHost.launch().also { host = it }
        current.scenario.onActivity { activity -> activity.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, scale)) {
                ZaparaTheme(theme, MotionSettings.Off) { Box(Modifier.fillMaxSize()) { body() } }
            }
        } }
        rule.waitForIdle()
        current.awaitForeground()
    }

    private fun snapshot(): RenderedTextEvidence.Snapshot {
        val node = rule.onNodeWithTag("Probe", useUnmergedTree = true).fetchSemanticsNode()
        return rule.runOnIdle { RenderedTextEvidence.capture(node) }
    }

    private fun check() {
        val evidence = snapshot()
        rule.runOnIdle { RenderedTextEvidence.verify(evidence, scale) }
    }

    private fun rejected(defect: TextDefect, body: @Composable () -> Unit) {
        fixture(body)
        try { check(); fail("Intentionally broken fixture accepted: $defect") }
        catch (failure: TextEvidenceFailure) { assertEquals(failure.message, defect, failure.defect) }
        Frames.capture(requireNotNull(host).activity, "$framePrefix-${defect.name.lowercase().replace('_', '-')}-${controlIndex++}")
    }

    @Test fun synthetic_width_is_not_clipping() {
        fixture { Text("Расписание", Modifier.testTag("Probe"), style = Zapara.typography.body) }
        val evidence = snapshot()
        assertTrue("Must reproduce the known synthetic artifact", evidence.raw.didOverflowWidth)
        rule.runOnIdle { RenderedTextEvidence.verify(evidence, scale) }
        Frames.capture(requireNotNull(host).activity, "$framePrefix-synthetic-fit")
    }

    @Test fun alignments_and_word_boundary_wrapping_fit() {
        listOf(TextAlign.Start, TextAlign.Center, TextAlign.End).forEach { align ->
            fixture { Text("Расписание", Modifier.testTag("Probe"), style = Zapara.typography.body, textAlign = align) }
            check()
        }
        fixture { CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Rtl) {
            Text("Расписание", Modifier.testTag("Probe"), style = Zapara.typography.body, textAlign = TextAlign.End)
        } }
        check()
        fixture { Text("Нечётная неделя", Modifier.width(190.dp).testTag("Probe"), style = Zapara.typography.body) }
        check()
        Frames.capture(requireNotNull(host).activity, "$framePrefix-fit")
    }

    @Test fun horizontal_ellipsis_and_max_lines_are_rejected() {
        rejected(TextDefect.HORIZONTAL) { Text("Расписание", Modifier.width(30.dp).testTag("Probe"), softWrap = false, style = Zapara.typography.body) }
        rejected(TextDefect.ELLIPSIS) { Text("Нечётная неделя", Modifier.width(50.dp).testTag("Probe"), maxLines = 1, overflow = TextOverflow.Ellipsis, style = Zapara.typography.body) }
        rejected(TextDefect.LOST_TEXT) { Text("Нечётная неделя", Modifier.width(50.dp).testTag("Probe"), maxLines = 1, overflow = TextOverflow.Clip, style = Zapara.typography.body) }
    }

    @Test fun vertical_parent_clip_and_word_split_are_rejected() {
        rejected(TextDefect.VERTICAL) { Text("Расписание", Modifier.height(3.dp).testTag("Probe"), style = Zapara.typography.body) }
        rejected(TextDefect.PARENT_CLIP) {
            Box(Modifier.size(20.dp).clipToBounds().wrapContentSize(Alignment.TopStart, unbounded = true)) {
                Text("Расписание", Modifier.testTag("Probe"), style = Zapara.typography.body)
            }
        }
        rejected(TextDefect.PARENT_CLIP) {
            Box(Modifier.size(20.dp).graphicsLayer { clip = true }.wrapContentSize(Alignment.TopStart, unbounded = true)) {
                Text("Расписание", Modifier.testTag("Probe"), style = Zapara.typography.body)
            }
        }
        rejected(TextDefect.WORD_SPLIT) { Text("Расписание", Modifier.width(35.dp).testTag("Probe"), style = Zapara.typography.body) }
    }

    @Test fun missing_empty_and_stale_evidence_are_rejected() {
        rejected(TextDefect.MISSING) { Box(Modifier.size(30.dp).testTag("Probe")) }
        rejected(TextDefect.MISSING) { Box(Modifier.size(30.dp).testTag("Probe").semantics {
            this[SemanticsActions.GetTextLayoutResult] = androidx.compose.ui.semantics.AccessibilityAction(null) { true }
        }) }
        var width by mutableStateOf(200.dp)
        fixture { Text("Расписание", Modifier.width(width).testTag("Probe"), style = Zapara.typography.body) }
        val old = snapshot()
        rule.runOnIdle { width = 250.dp }
        rule.waitForIdle()
        try { rule.runOnIdle { RenderedTextEvidence.verify(old, scale) }; fail("Stale layout accepted") }
        catch (failure: TextEvidenceFailure) { assertEquals(TextDefect.STALE, failure.defect) }
        val fresh = snapshot()
        try { rule.runOnIdle { RenderedTextEvidence.verify(fresh, scale + 1f) }; fail("Wrong scale accepted") }
        catch (failure: TextEvidenceFailure) { assertEquals(TextDefect.SCALE, failure.defect) }
    }

    @Test fun real_field_layout_and_scrolled_clipping() {
        fixture { androidx.compose.foundation.text.BasicTextField("Решить задачу", {},
            Modifier.width(250.dp).testTag("Probe"), textStyle = Zapara.typography.body) }
        check()
        assertFalse("Programmatic field text does not prove a visible IME", KeyboardEvidence.visible())
        try { KeyboardEvidence.requireVisible(); fail("Hidden IME accepted") }
        catch (failure: TextEvidenceFailure) { assertEquals(TextDefect.CONDITION, failure.defect) }
        Frames.capture(requireNotNull(host).activity, "$framePrefix-field-fit")
        rejected(TextDefect.PARENT_CLIP) { androidx.compose.foundation.text.BasicTextField("Расписание", {},
            Modifier.width(30.dp).testTag("Probe"), singleLine = true, textStyle = Zapara.typography.body) }
    }

    @Test fun background_root_is_not_current_evidence() {
        fixture { Text("Расписание", Modifier.testTag("Probe"), style = Zapara.typography.body) }
        val background = snapshot()
        OwnedTestHost.launch().use { other ->
            other.scenario.onActivity { activity -> activity.setContent { Text("Другое окно") } }
            rule.waitForIdle()
            other.awaitForeground()
            try { rule.runOnIdle { RenderedTextEvidence.verify(background, scale) }; fail("Background root accepted") }
            catch (failure: TextEvidenceFailure) { assertEquals(TextDefect.MISSING, failure.defect) }
        }
    }
}
