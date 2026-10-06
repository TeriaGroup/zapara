package ru.bgtu_voenmeh.zapara

import android.content.res.Configuration
import android.graphics.Color
import android.view.Gravity
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import androidx.test.platform.app.InstrumentationRegistry
import java.time.LocalDate
import java.time.LocalDateTime
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.widgets.*

/** Display screenshots of production RemoteViews, using fixtures without repository writes. */
class WidgetVisualMatrixTest {
    @Test fun narrow_homework_default_font_keeps_deadline_visible_and_full_spoken_task() {
        val base = InstrumentationRegistry.getInstrumentation().targetContext
        val ctx = base.createConfigurationContext(Configuration(base.resources.configuration).apply { fontScale = 1f })
        val task = "Задачи 14–28 из учебника, с подробным решением · завтра"
        val snapshot = HomeworkWidgetSnapshot(WidgetJobIdentity("guest", "synthetic", 0),
            "Домашка", "Гость · А863С", null, listOf(HomeworkWidgetRow("Математика", task, "warn", 1)))
        OwnedTestHost.launch().use { host ->
            lateinit var widget: android.view.View
            host.scenario.onActivity { activity ->
                val density = ctx.resources.displayMetrics.density
                val column = LinearLayout(activity).apply {
                    orientation = LinearLayout.VERTICAL
                    gravity = Gravity.CENTER
                    setBackgroundColor(Color.WHITE)
                }
                column.addView(TextView(activity).apply {
                    text = "Синтетическая домашка · 180×160 dp · font 1.0"
                    gravity = Gravity.CENTER
                    setTextColor(Color.DKGRAY)
                    textSize = 14f
                    setPadding(0, 0, 0, (24 * density).toInt())
                })
                widget = WidgetRemoteViews.homework(ctx, snapshot, 160).apply(ctx, FrameLayout(ctx))
                column.addView(widget, LinearLayout.LayoutParams((180 * density).toInt(), (160 * density).toInt()))
                activity.setContentView(column)
            }
            host.awaitForeground()
            host.scenario.onActivity {
                val detail = widget.findViewById<TextView>(R.id.widget_homework_detail1)
                assertTrue("Due date must precede long task text", detail.text.startsWith("завтра"))
                assertTrue("Due date is on the visible first line", detail.layout.getLineEnd(0) >= "завтра".length)
                assertTrue("Ellipsis cannot consume the due date", detail.layout.getEllipsisCount(0) == 0 ||
                    detail.layout.getEllipsisStart(0) >= "завтра".length)
                assertTrue("First line fits the rendered TextView", detail.layout.getLineBottom(0) <=
                    detail.height - detail.totalPaddingTop - detail.totalPaddingBottom)
                val rect = android.graphics.Rect()
                assertTrue(detail.getGlobalVisibleRect(rect))
                assertTrue(rect.height() >= detail.layout.getLineBottom(0))
                org.junit.Assert.assertEquals("Математика, $task",
                    widget.findViewById<android.view.View>(R.id.widget_homework_row1).contentDescription.toString())
            }
            assertTrue(Frames.capture(host.activity, "widget-homework-narrow-default-deadline").length() > 1000)
        }
    }

    @Test fun held_motion_frames_keep_final_native_text_readable() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val identity = WidgetJobIdentity("guest", "synthetic", 0)
        val date = LocalDate.of(2026, 10, 5)
        val policy = WidgetMotionPolicy.of(true, 1f, true)
        for (dark in listOf(false, true)) {
            val theme = if (dark) "dark" else "light"
            val timer = TimerWidgetSnapshot(identity, "10:00", "Пара", "Математика", "493 ГК",
                TimerPhaseKind.Lesson, 0.2f, date.atTime(10, 35), isDark = dark)
            val nextTimer = timer.copy(phaseText = "Перемена", kind = TimerPhaseKind.Break, fraction = 0.8f)
            val room = WayfinderWidgetSnapshot(identity, "Куда идти", "Сейчас", "Математика", "09:00 – 10:35",
                "493а ГК", "493а", date, true, null, isDark = dark)
            val nextRoom = room.copy(room = "201 Б")
            val week = WeekWidgetSnapshot(identity, "Неделя", "Гость · синтетические данные", (0..6).map {
                WeekWidgetDay(date.plusDays(it.toLong()), listOf("Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс")[it], 2, it == 6)
            }, null, isDark = dark)
            val nextWeek = week.copy(days = week.days.mapIndexed { i, day -> day.copy(isToday = i == 0, lessonCount = if (i == 3) 4 else 2) })
            val scenes = listOf(WidgetFaceEffects.timer(timer, nextTimer, policy)!!,
                WidgetFaceEffects.room(room, nextRoom, policy)!!, WidgetFaceEffects.week(week, nextWeek, policy)!!)
            val heights = listOf(180, 140, 210)
            val overlays = listOf(R.id.widget_timer_overlay, R.id.widget_wayfinder_overlay, R.id.widget_week_overlay)
            for (progress in listOf(-1f, 0.35f, 0.7f, 1f)) {
                val step = when (progress) { -1f -> "before"; 0.35f -> "p35"; 0.7f -> "p70"; else -> "final" }
                val faces = listOf(
                    WidgetRemoteViews.timer(ctx, if (progress < 0) timer else nextTimer, 320, liveCountdown = false, heightDp = heights[0]),
                    WidgetExtraViews.wayfinder(ctx, if (progress < 0) room else nextRoom, 95401),
                    WidgetExtraViews.week(ctx, if (progress < 0) week else nextWeek, 95402))
                OwnedTestHost.launch().use { host ->
                    host.scenario.onActivity { activity ->
                        val density = ctx.resources.displayMetrics.density
                        val column = LinearLayout(activity).apply {
                            orientation = LinearLayout.VERTICAL
                            gravity = Gravity.CENTER
                            setBackgroundColor(if (dark) Color.rgb(13, 13, 12) else Color.WHITE)
                        }
                        column.addView(TextView(activity).apply {
                            text = "Синтетическая анимация · $theme · $step"
                            gravity = Gravity.CENTER
                            setTextColor(if (dark) Color.LTGRAY else Color.DKGRAY)
                            textSize = 14f
                        })
                        faces.forEachIndexed { i, face ->
                            val widget = face.apply(ctx, FrameLayout(ctx))
                            if (progress >= 0) {
                                android.widget.RemoteViews(ctx.packageName, face.layoutId).apply {
                                    setImageViewBitmap(overlays[i], scenes[i].bitmapAt(ctx, 95401 + i, 320, heights[i], progress))
                                    setViewVisibility(overlays[i], android.view.View.VISIBLE)
                                }.reapply(ctx, widget)
                            }
                            column.addView(widget, LinearLayout.LayoutParams((320 * density).toInt(), (heights[i] * density).toInt()).apply {
                                topMargin = (8 * density).toInt()
                            })
                        }
                        activity.setContentView(column)
                    }
                    host.awaitForeground()
                    assertTrue(Frames.capture(host.activity, "widget-held-$theme-$step").length() > 1000)
                }
            }
        }
    }

    @Test fun five_faces_in_wide_light_and_compact_dark_large_text() {
        val base = InstrumentationRegistry.getInstrumentation().targetContext
        val identity = WidgetJobIdentity("guest", "synthetic", 0)
        val date = LocalDate.of(2026, 10, 5)
        for (compact in listOf(false, true)) {
            val scale = if (compact) 1.3f else 1f
            val theme = if (compact) "dark-compact-large" else "light-wide"
            val ctx = base.createConfigurationContext(Configuration(base.resources.configuration).apply {
                fontScale = scale
                uiMode = (uiMode and Configuration.UI_MODE_NIGHT_MASK.inv()) or
                    if (compact) Configuration.UI_MODE_NIGHT_YES else Configuration.UI_MODE_NIGHT_NO
            })
            for (kind in listOf("schedule", "homework", "timer", "wayfinder", "week")) {
                val width = if (!compact) 320 else if (kind == "week") 250 else 180
                val height = when (kind) {
                    "timer" -> if (compact) 180 else 240
                    "wayfinder" -> if (compact) 110 else 180
                    "week" -> if (compact) 170 else 230
                    else -> if (compact) 160 else 230
                }
                val face = when (kind) {
                    "schedule" -> WidgetRemoteViews.schedule(ctx, ScheduleWidgetSnapshot(identity,
                        "Расписание", "Гость · А863С", null, listOf(
                            ScheduleWidgetRow("Математический анализ", "09:00 – 10:35 · 493 ГК", true),
                            ScheduleWidgetRow("Теоретическая механика", "10:45 – 12:20 · 201 Б", false),
                            ScheduleWidgetRow("Физика", "13:00 – 14:35 · 305 ГК", false)),
                        isDark = compact, dayLabel = "Сегодня"), height)
                    "homework" -> WidgetRemoteViews.homework(ctx, HomeworkWidgetSnapshot(identity,
                        "Домашка", "Гость · А863С", null, listOf(
                            HomeworkWidgetRow("Математический анализ", "Задачи 14–28 с подробным решением · завтра", "warn", 1),
                            HomeworkWidgetRow("Физика", "Подготовить лабораторную работу · 8 октября", "text2", 2)),
                        isDark = compact), height)
                    "timer" -> WidgetRemoteViews.timer(ctx, TimerWidgetSnapshot(identity,
                        "42:00", "Пара", "Математический анализ", "493 ГК", TimerPhaseKind.Lesson,
                        0.5f, LocalDateTime.now().plusMinutes(42), isDark = compact), width,
                        liveCountdown = true, heightDp = height)
                    "wayfinder" -> WidgetExtraViews.wayfinder(ctx, WayfinderWidgetSnapshot(identity,
                        "Куда идти", "Сейчас", "Математический анализ", "09:00 – 10:35",
                        "493а ГК", "493а", date, true, null, isDark = compact), 95401)
                    else -> WidgetExtraViews.week(ctx, WeekWidgetSnapshot(identity, "Неделя", "Гость · А863С",
                        (0..6).map { WeekWidgetDay(date.plusDays(it.toLong()),
                            listOf("Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс")[it],
                            listOf(3, 4, 2, 4, 3, 0, 0)[it], it == 0) }, null, isDark = compact), 95402)
                }
                OwnedTestHost.launch().use { host ->
                    host.scenario.onActivity { activity ->
                        val density = ctx.resources.displayMetrics.density
                        val column = LinearLayout(activity).apply {
                            orientation = LinearLayout.VERTICAL
                            gravity = Gravity.CENTER
                            setBackgroundColor(if (compact) Color.rgb(13, 13, 12) else Color.WHITE)
                        }
                        column.addView(TextView(activity).apply {
                            text = "Синтетический $kind · $width×$height dp\n$theme · font $scale"
                            gravity = Gravity.CENTER
                            setTextColor(if (compact) Color.LTGRAY else Color.DKGRAY)
                            textSize = 14f
                            setPadding(0, 0, 0, (24 * density).toInt())
                        })
                        val widget = face.apply(ctx, FrameLayout(ctx))
                        column.addView(widget, LinearLayout.LayoutParams((width * density).toInt(), (height * density).toInt()))
                        activity.setContentView(column)
                    }
                    host.awaitForeground()
                    assertTrue(Frames.capture(host.activity, "widget-matrix-$kind-$theme").length() > 1000)
                }
            }
        }
    }
}
