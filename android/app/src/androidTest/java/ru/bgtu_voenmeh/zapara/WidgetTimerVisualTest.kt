package ru.bgtu_voenmeh.zapara

import android.view.Gravity
import android.view.View
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import androidx.compose.ui.graphics.toArgb
import androidx.test.platform.app.InstrumentationRegistry
import java.time.LocalDateTime
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.DarkColors
import ru.bgtu_voenmeh.zapara.ui.theme.LightColors
import ru.bgtu_voenmeh.zapara.ui.widgets.TimerPhaseKind
import ru.bgtu_voenmeh.zapara.ui.widgets.TimerWidgetSnapshot
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetJobIdentity
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetRemoteViews

/** Native RemoteViews on an owned activity; no provider, widget ID, repository, or alarm. */
class WidgetTimerVisualTest {
    @Test fun timer_faces_render_in_both_themes_without_a_widget_host() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val identity = WidgetJobIdentity("guest", "synthetic", 0)
        OwnedTestHost.launch().use { host ->
            for (dark in listOf(false, true)) {
                val theme = if (dark) "dark" else "light"
                val canvas = if (dark) DarkColors.canvas else LightColors.canvas
                for (running in listOf(false, true)) {
                    val state = if (running) "running" else "idle"
                    val snapshot = TimerWidgetSnapshot(
                        identity = identity,
                        timeText = "42:00",
                        phaseText = "Пара",
                        subject = "Математика",
                        detail = "493 ГК",
                        kind = TimerPhaseKind.Lesson,
                        fraction = 0.5f,
                        endsAt = LocalDateTime.now().plusMinutes(42),
                        isDark = dark
                    )
                    host.scenario.onActivity { activity ->
                        val density = activity.resources.displayMetrics.density
                        val size = (180 * density).toInt()
                        val parent = LinearLayout(activity).apply {
                            orientation = LinearLayout.VERTICAL
                            gravity = Gravity.CENTER
                            setBackgroundColor(canvas.toArgb())
                        }
                        val widget = WidgetRemoteViews.timer(context, snapshot, 180,
                            liveCountdown = running, heightDp = 180).apply(context, FrameLayout(activity))
                        parent.addView(widget, LinearLayout.LayoutParams(size, size))
                        activity.setContentView(parent)
                        assertTrue(widget.contentDescription.toString().contains("Математика"))
                        assertEquals(if (running) View.VISIBLE else View.GONE,
                            widget.findViewById<View>(R.id.widget_timer_time).visibility)
                        assertEquals(if (running) View.GONE else View.VISIBLE,
                            widget.findViewById<TextView>(R.id.widget_timer_fallback).visibility)
                    }
                    host.awaitForeground()
                    assertTrue(Frames.capture(host.activity, "fullqa-widget-timer-$theme-$state").length() > 1000)
                }
            }
        }
    }
}
