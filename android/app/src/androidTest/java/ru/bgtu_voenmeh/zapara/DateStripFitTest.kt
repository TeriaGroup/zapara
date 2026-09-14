package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.hasAnyAncestor
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.unit.Density
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.runners.Parameterized
import ru.bgtu_voenmeh.zapara.ui.schedule.DateStrip
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import java.time.LocalDate

/** Painted date cells at 200%; fixtures never touch Room. */
@RunWith(Parameterized::class)
class DateStripFitTest(private val width: Int, private val scale: Float, private val theme: ThemeChoice) {
    @get:Rule val rule = createAndroidComposeRule<ComponentActivity>()
    private var density = 1f

    @Test fun selected_today_digits_keep_vertical_inset() {
        val today = LocalDate.of(2026, 9, 16)
        show { DateStrip(today, today, {}) }
        val cell = rule.onNodeWithTag("Schedule.Date.20260916").assertIsDisplayed().fetchSemanticsNode()
        assertTrue("48dp height", cell.boundsInRoot.height + .5f >= 48 * density)
        val day = rule.onNode(hasText("16") and hasAnyAncestor(hasTestTag("Schedule.Date.20260916")), true)
            .assertIsDisplayed().fetchSemanticsNode()
        val inset = 8 * density
        assertTrue("top inset", day.boundsInRoot.top + .5f >= cell.boundsInRoot.top + inset)
        assertTrue("bottom inset", day.boundsInRoot.bottom <= cell.boundsInRoot.bottom - inset + .5f)
        rule.runOnIdle {
            val snapshot = RenderedTextEvidence.capture(day)
            RenderedTextEvidence.verify(snapshot, scale)
        }
    }

    private fun show(content: @Composable () -> Unit) {
        rule.setContent {
            BoxWithConstraints(Modifier.fillMaxSize()) {
                val controlled = Density(constraints.maxWidth.toFloat() / width, scale)
                SideEffect { density = controlled.density }
                CompositionLocalProvider(LocalDensity provides controlled) {
                    ZaparaTheme(theme, MotionSettings.Off) {
                        Box(Modifier.fillMaxSize().testTag("DateStrip.Host")) { content() }
                    }
                }
            }
        }
        rule.waitUntil(10_000) { rule.activity.hasWindowFocus() }
        rule.waitForIdle()
        val bounds = rule.onNodeWithTag("DateStrip.Host").fetchSemanticsNode().boundsInRoot
        assertEquals(width.toFloat(), bounds.width / density, .5f)
    }

    companion object {
        @JvmStatic @Parameterized.Parameters(name = "{0}dp-{1}-{2}")
        fun cases(): List<Array<Any>> = listOf(360, 411).flatMap { width ->
            listOf(2f).flatMap { scale ->
                listOf(ThemeChoice.Light, ThemeChoice.Dark).map { arrayOf(width, scale, it) }
            }
        }
    }
}
