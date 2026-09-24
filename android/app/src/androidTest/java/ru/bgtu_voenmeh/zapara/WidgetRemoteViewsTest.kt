package ru.bgtu_voenmeh.zapara

import android.view.View
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.ui.AndroidUiCopy
import ru.bgtu_voenmeh.zapara.ui.widgets.HomeworkWidgetComposer
import ru.bgtu_voenmeh.zapara.ui.widgets.ScheduleWidgetComposer
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetJobIdentity
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetJobs
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetRemoteViews
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetExtraViews
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetIntents
import ru.bgtu_voenmeh.zapara.ui.widgets.WayfinderWidgetSnapshot
import ru.bgtu_voenmeh.zapara.ui.widgets.WeekWidgetSnapshot
import ru.bgtu_voenmeh.zapara.ui.widgets.*
import java.time.LocalDate
import java.time.LocalDateTime

class WidgetRemoteViewsTest {
    @Test fun homework_wrapped_rows_fit_180dp_width_at_normal_and_enlarged_font_scale() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val base = instrumentation.targetContext
        instrumentation.runOnMainSync {
            for (scale in listOf(1f, 1.3f)) {
                val ctx = base.createConfigurationContext(android.content.res.Configuration(base.resources.configuration).apply { fontScale = scale })
                for (height in listOf(160, 400)) {
                    val count = if (height == 160) 2 else 4
                    val homework = HomeworkWidgetSnapshot(extraIdentity, "Домашка", "Гость · А863С", null,
                        (1..count).map { HomeworkWidgetRow("Математика $it", "Задачи 14–28 из учебника, с подробным решением · завтра", "warn", it.toLong()) })
                    val tree = measured(ctx, WidgetRemoteViews.homework(ctx, homework, height), 180, height)
                    captureRender(ctx, tree, "homework-$scale-$height")
                    var visible = 0
                    (1..4).forEach { index ->
                        val row = tree.named("widget_homework_row$index")
                        val detail = tree.named("widget_homework_detail$index") as TextView
                        if (row.visibility == View.VISIBLE) {
                            visible++
                            assertEquals("Synthetic long details must wrap", 2, detail.lineCount)
                            val rect = descendantBounds(tree, detail)
                            assertTrue("scale=$scale height=$height row=$index bottom=${rect.bottom} widget=${tree.height}",
                                rect.bottom <= tree.height - tree.findViewById<View>(R.id.widget_homework_body).paddingBottom)
                            assertTrue("Wrapped line must fit its TextView at scale=$scale row=$index",
                                detail.layout.getLineBottom(detail.lineCount - 1) <= detail.height - detail.totalPaddingTop - detail.totalPaddingBottom)
                        } else assertEquals("", detail.text.toString())
                    }
                    assertTrue("Keep the nearest due entry", visible >= 1)
                    if (height == 400) assertEquals("A tall instance keeps all four entries", 4, visible)
                }
            }
        }
    }

    @Test fun room_reel_masks_only_room_and_new_text_enters_below_in_both_themes() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val ctx = instrumentation.targetContext
        instrumentation.runOnMainSync {
            for (dark in listOf(false, true)) {
                val old = WayfinderWidgetSnapshot(extraIdentity, "Куда идти", "Сейчас", "Математика", "09:00 – 10:35",
                    ".", ".", LocalDate.of(2026, 9, 23), false, null, isDark = dark)
                val next = old.copy(room = "888888 Б", classroomRaw = "888888")
                val final = WidgetExtraViews.wayfinder(ctx, next, 903)
                val tree = measured(ctx, final, 280, 160)
                val rect = descendantBounds(tree, tree.findViewById(R.id.widget_wayfinder_room))
                val scene = WidgetFaceEffects.room(old, next, WidgetMotionPolicy.of(true, 1f, true))!!
                val first = scene.bitmapAt(ctx, 903, 280, 160, 0f)
                val middle = scene.bitmapAt(ctx, 903, 280, 160, 0.1f)
                val last = scene.bitmapAt(ctx, 903, 280, 160, 1f)
                val ratio = first.width.toFloat() / tree.width
                val card = ctx.getColor(if (dark) R.color.widget_dark_card else R.color.widget_light_card)
                val right = (rect.right * ratio).toInt() - 2
                val top = (rect.top * ratio).toInt() + 2
                val bottom = (rect.bottom * ratio).toInt() - 3
                val left = ((rect.left + 50 * ctx.resources.displayMetrics.density) * ratio).toInt()
                assertEquals("The initial overlay must hide the final room, including blank area", card,
                    first.getPixel(right, (top + bottom) / 2))
                for (y in 0 until first.height) for (x in 0 until first.width) {
                    if (x < rect.left * ratio - 1 || x > rect.right * ratio + 1 ||
                        y < rect.top * ratio - 1 || y > rect.bottom * ratio + 1)
                        assertEquals("No mask outside room at $x,$y", 0, android.graphics.Color.alpha(first.getPixel(x, y)))
                }
                fun inkTop(frame: android.graphics.Bitmap): Int = (top until bottom).firstOrNull { y ->
                    (left until right).any { x ->
                        val pixel = frame.getPixel(x, y)
                        android.graphics.Color.alpha(pixel) > 0 && kotlin.math.abs(android.graphics.Color.red(pixel) - android.graphics.Color.red(card)) > 12
                    }
                } ?: error("Incoming room ink missing")
                assertTrue("Incoming room must enter from below, not stay static behind old room",
                    inkTop(middle) > inkTop(last) + 2)
                val partial = android.widget.RemoteViews(ctx.packageName, R.layout.widget_wayfinder).apply {
                    setImageViewBitmap(R.id.widget_wayfinder_overlay, middle)
                    setViewVisibility(R.id.widget_wayfinder_overlay, View.VISIBLE)
                }
                partial.reapply(ctx, tree)
                captureRender(ctx, tree, "room-$dark-middle")
                assertEquals("888888 Б", tree.findViewById<TextView>(R.id.widget_wayfinder_room).text.toString())
                assertTrue(tree.contentDescription.contains("888888 Б"))
                final.reapply(ctx, tree)
                assertEquals(View.GONE, tree.findViewById<View>(R.id.widget_wayfinder_overlay).visibility)
                partial.reapply(ctx, tree)
                WidgetExtraViews.wayfinder(ctx, WayfinderWidgetComposer.cleared(extraIdentity.copy(generation = 2), AndroidUiCopy(ctx), dark), 903).reapply(ctx, tree)
                assertEquals("", tree.findViewById<TextView>(R.id.widget_wayfinder_room).text.toString())
                assertEquals(View.GONE, tree.findViewById<View>(R.id.widget_wayfinder_overlay).visibility)
            }
        }
    }

    private fun measured(ctx: android.content.Context, views: android.widget.RemoteViews, widthDp: Int, heightDp: Int): android.view.ViewGroup {
        val tree = views.apply(ctx, FrameLayout(ctx)) as android.view.ViewGroup
        val density = ctx.resources.displayMetrics.density
        tree.measure(View.MeasureSpec.makeMeasureSpec(kotlin.math.round(widthDp * density).toInt(), View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec(kotlin.math.round(heightDp * density).toInt(), View.MeasureSpec.EXACTLY))
        tree.layout(0, 0, tree.measuredWidth, tree.measuredHeight)
        return tree
    }

    private fun descendantBounds(tree: android.view.ViewGroup, child: View) = android.graphics.Rect().also {
        child.getDrawingRect(it)
        tree.offsetDescendantRectToMyCoords(child, it)
    }

    private fun captureRender(ctx: android.content.Context, tree: View, name: String) {
        if (InstrumentationRegistry.getArguments().getString("finalFixCapture") != "true") return
        // Reapply can reveal the initially GONE overlay; an unattached tree has no traversal.
        tree.measure(View.MeasureSpec.makeMeasureSpec(tree.width, View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec(tree.height, View.MeasureSpec.EXACTLY))
        tree.layout(0, 0, tree.measuredWidth, tree.measuredHeight)
        val bitmap = android.graphics.Bitmap.createBitmap(tree.width, tree.height, android.graphics.Bitmap.Config.ARGB_8888)
        tree.draw(android.graphics.Canvas(bitmap))
        java.io.File(ctx.getExternalFilesDir(null), "final-fix-$name.png").outputStream().use {
            bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it)
        }
    }

    @Test fun week_transition_covers_static_marker_edges_after_bitmap_downscaling() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val density = ctx.resources.displayMetrics.density
        val date = LocalDate.of(2026, 9, 21)
        val old = WeekWidgetSnapshot(extraIdentity, "Неделя", "Гость", (0..6).map {
            WeekWidgetDay(date.plusDays(it.toLong()), "День", 2, it == 6)
        }, null)
        val next = old.copy(days = old.days.mapIndexed { i, day -> day.copy(isToday = i == 0) })
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            val tree = WidgetExtraViews.week(ctx, next, 902).apply(ctx, FrameLayout(ctx)) as android.view.ViewGroup
            tree.measure(View.MeasureSpec.makeMeasureSpec((320 * density).toInt(), View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec((230 * density).toInt(), View.MeasureSpec.EXACTLY))
            tree.layout(0, 0, tree.measuredWidth, tree.measuredHeight)
            val chip = tree.findViewById<View>(R.id.widget_week_day1)
            val rect = android.graphics.Rect()
            chip.getDrawingRect(rect)
            tree.offsetDescendantRectToMyCoords(chip, rect)
            val frame = WidgetFaceEffects.week(old, next, WidgetMotionPolicy.of(true, 1f, true))!!
                .bitmapAt(ctx, 902, 320, 230, 0f)
            // Bilinear filtering samples across this edge. Its coverage must extend past the final chip.
            val x = ((rect.left - density / 2) * frame.width / tree.width).toInt()
            val y = (rect.centerY().toFloat() * frame.height / tree.height).toInt()
            assertEquals(ctx.getColor(R.color.widget_light_card), frame.getPixel(x, y))
        }
    }

    @Test fun live_host_phase_pulse_keeps_running_and_restores_the_fresh_clock_and_final_faces() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val ctx = instrumentation.targetContext
        val manager = android.appwidget.AppWidgetManager.getInstance(ctx)
        val app = ctx.applicationContext as ZaparaApplication
        val identity = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
        val policy = WidgetMotionPolicy(true, 500, 7)
        val delivered = java.util.concurrent.CountDownLatch(3)
        val seen = mutableSetOf<Int>()
        val host = object : android.appwidget.AppWidgetHost(ctx, 54107) {
            override fun onCreateView(context: android.content.Context, appWidgetId: Int, info: android.appwidget.AppWidgetProviderInfo) =
                object : android.appwidget.AppWidgetHostView(context) {
                    override fun updateAppWidget(views: android.widget.RemoteViews?) {
                        super.updateAppWidget(views)
                        val ready = listOf(R.id.widget_timer_phase, R.id.widget_wayfinder_empty, R.id.widget_wayfinder_subject,
                            R.id.widget_week_subtitle, R.id.widget_week_empty).any { !findViewById<TextView>(it)?.text.isNullOrBlank() }
                        if (ready && seen.add(appWidgetId)) delivered.countDown()
                    }
                }
        }
        val providers = listOf(TimerWidgetProvider::class.java, WayfinderWidgetProvider::class.java, WeekWidgetProvider::class.java)
        val ids = providers.map { host.allocateAppWidgetId() }
        val heights = listOf(190, 140, 230)
        val hosted = mutableListOf<android.appwidget.AppWidgetHostView>()
        val capture = androidx.test.platform.app.InstrumentationRegistry.getArguments().getString("task7Capture") == "true"
        fun screenshot(name: String) {
            if (!capture) return
            val file = java.io.File(ctx.getExternalFilesDir(null), "task7-$name.png")
            file.outputStream().use { instrumentation.uiAutomation.takeScreenshot().compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it) }
        }
        instrumentation.uiAutomation.adoptShellPermissionIdentity("android.permission.BIND_APPWIDGET")
        try {
            ids.forEachIndexed { i, id ->
                assertTrue(manager.bindAppWidgetIdIfAllowed(id, android.content.ComponentName(ctx, providers[i]), android.os.Bundle().apply {
                    putInt(android.appwidget.AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH, 320)
                    putInt(android.appwidget.AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, heights[i])
                }))
            }
            androidx.test.core.app.ActivityScenario.launch(Api37TestActivity::class.java).use { scenario ->
                scenario.onActivity { activity ->
                    val density = activity.resources.displayMetrics.density
                    val column = android.widget.LinearLayout(activity).apply {
                        orientation = android.widget.LinearLayout.VERTICAL
                        gravity = android.view.Gravity.CENTER
                        setBackgroundColor(android.graphics.Color.WHITE)
                    }
                    host.startListening()
                    ids.forEachIndexed { i, id ->
                        val view = host.createView(activity, id, manager.getAppWidgetInfo(id))
                        view.setPadding(0, 0, 0, 0)
                        column.addView(view, android.widget.LinearLayout.LayoutParams((320 * density).toInt(), (heights[i] * density).toInt()))
                        hosted += view
                    }
                    activity.setContentView(column)
                }
                assertTrue("All providers must publish initial local faces", delivered.await(5, java.util.concurrent.TimeUnit.SECONDS))
                val date = LocalDate.of(2026, 9, 21)
                val oldTimer = TimerWidgetSnapshot(identity, "00:02", "Пара", "Математика", "493 ГК",
                    TimerPhaseKind.Lesson, 0.02f, LocalDateTime.now().plusSeconds(2))
                val nextTimer = oldTimer.copy(timeText = "10:00", phaseText = "Перемена", kind = TimerPhaseKind.Break,
                    fraction = 0.9f, endsAt = LocalDateTime.now().plusMinutes(10))
                val oldRoom = WayfinderWidgetSnapshot(identity, "Куда идти", "Сейчас", "Математика", "09:00 – 10:35",
                    "493а ГК", "493а", date, true, null)
                val nextRoom = oldRoom.copy(room = "201 Б")
                val oldWeek = WeekWidgetSnapshot(identity, "Неделя", "Гость", (0..6).map {
                    WeekWidgetDay(date.plusDays(it.toLong()), listOf("Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс")[it], 2, it == 6)
                }, null)
                val nextWeek = oldWeek.copy(days = oldWeek.days.mapIndexed { i, day -> day.copy(isToday = i == 0, lessonCount = if (i == 3) 4 else 2) })
                instrumentation.runOnMainSync {
                    WidgetRemoteViews.clearMotion()
                    WidgetRemoteViews.dropTimerFaces()
                    WidgetMotionPlayer.updatePolicy(identity, policy, WidgetMotionPlayer.beginPolicyObservation())
                    WidgetRemoteViews.pushTimer(ctx, oldTimer, policy)
                    WidgetRemoteViews.pushWayfinder(ctx, oldRoom, policy)
                    WidgetRemoteViews.pushWeek(ctx, oldWeek, policy)
                }
                instrumentation.waitForIdleSync()
                screenshot("before")
                instrumentation.runOnMainSync {
                    WidgetRemoteViews.pushTimer(ctx, nextTimer, policy)
                    WidgetRemoteViews.pushWayfinder(ctx, nextRoom, policy)
                    WidgetRemoteViews.pushWeek(ctx, nextWeek, policy)
                    assertTrue(ids.all { WidgetMotionPlayer.isRunning(it) })
                }
                android.os.SystemClock.sleep(90)
                var pulseText = ""
                instrumentation.runOnMainSync {
                    WidgetRemoteViews.pushTimer(ctx, nextTimer.copy(timeText = "09:59", fraction = 0.89f), policy)
                    assertTrue("An ordinary timer pulse must keep the phase scene alive", WidgetMotionPlayer.isRunning(ids[0]))
                    // A deliberately newer clock is supplied through the player API while the semantic scene stays the same.
                    val fresh = WidgetRemoteViews.timer(ctx, nextTimer, 320).apply {
                        setTextViewText(R.id.widget_timer_time, "09:58")
                        setContentDescription(R.id.widget_timer_root, "Перемена, 09:58, Математика, 493 ГК")
                    }
                    assertTrue(WidgetMotionPlayer.refreshFinal(ids[0], identity, fresh))
                    manager.partiallyUpdateAppWidget(ids[0], android.widget.RemoteViews(ctx.packageName, R.layout.widget_timer).apply {
                        setTextViewText(R.id.widget_timer_time, "09:58")
                    })
                    assertTrue(WidgetMotionPlayer.isRunning(ids[0]))
                    pulseText = "09:58"
                }
                screenshot("during")
                android.os.SystemClock.sleep(550)
                instrumentation.runOnMainSync {
                    assertTrue(ids.none { WidgetMotionPlayer.isRunning(it) })
                    assertEquals(pulseText, hosted[0].findViewById<TextView>(R.id.widget_timer_time).text.toString())
                    assertTrue(hosted[0].findViewById<View>(R.id.widget_timer_root).contentDescription.contains(pulseText))
                    assertEquals("201 Б", hosted[1].findViewById<TextView>(R.id.widget_wayfinder_room).text.toString())
                    assertTrue(hosted[2].findViewById<View>(R.id.widget_week_day1).contentDescription.contains("Сегодня", ignoreCase = true))
                    listOf(R.id.widget_timer_overlay, R.id.widget_wayfinder_overlay, R.id.widget_week_overlay).forEachIndexed { i, overlay ->
                        assertEquals(View.GONE, hosted[i].findViewById<View>(overlay).visibility)
                    }
                    // The ordinary provider pulse must not create another scene.
                    WidgetRemoteViews.pushTimer(ctx, nextTimer.copy(fraction = 0.89f), policy)
                    assertFalse(WidgetMotionPlayer.isRunning(ids[0]))
                }
                instrumentation.waitForIdleSync()
                screenshot("after")
            }
        } finally {
            instrumentation.runOnMainSync { ids.forEach(WidgetMotionPlayer::cancel); host.stopListening() }
            ids.forEach(host::deleteAppWidgetId)
            instrumentation.uiAutomation.dropShellPermissionIdentity()
        }
    }

    @Test fun phase_room_and_week_frames_preserve_final_accessibility_in_both_themes() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val enabled = WidgetMotionPolicy.of(true, 1f, true)
        val date = LocalDate.of(2026, 9, 21)
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            for (dark in listOf(false, true)) {
                val timer = TimerWidgetSnapshot(extraIdentity, "10:00", "Пара", "Математика", "493 ГК",
                    TimerPhaseKind.Lesson, 0.5f, LocalDateTime.now().plusMinutes(10), isDark = dark)
                val nextTimer = timer.copy(kind = TimerPhaseKind.Break, phaseText = "Перемена", fraction = 0.8f)
                val room = WayfinderWidgetSnapshot(extraIdentity, "Куда идти", "Сейчас", "Математика", "09:00 – 10:35",
                    "493а ГК", "493а", date, true, null, isDark = dark)
                val nextRoom = room.copy(room = "201 Б")
                val week = WeekWidgetSnapshot(extraIdentity, "Неделя", "Гость", (0..6).map {
                    WeekWidgetDay(date.plusDays(it.toLong()), "День", 2, it == 6)
                }, null, isDark = dark)
                val nextWeek = week.copy(days = week.days.mapIndexed { i, day -> day.copy(isToday = i == 0,
                    lessonCount = if (i == 3) 4 else 2) })
                val cases = listOf(
                    Triple(WidgetFaceEffects.timer(timer, nextTimer, enabled)!!, WidgetRemoteViews.timer(ctx, nextTimer, 180), R.id.widget_timer_overlay),
                    Triple(WidgetFaceEffects.room(room, nextRoom, enabled)!!, WidgetExtraViews.wayfinder(ctx, nextRoom, 901), R.id.widget_wayfinder_overlay),
                    Triple(WidgetFaceEffects.week(week, nextWeek, enabled)!!, WidgetExtraViews.week(ctx, nextWeek, 902), R.id.widget_week_overlay))
                cases.forEach { (scene, final, overlayId) ->
                    val tree = final.apply(ctx, FrameLayout(ctx))
                    val description = tree.contentDescription.toString()
                    for (progress in listOf(0f, 0.35f, 0.7f, 1f)) {
                        val bitmap = scene.bitmapAt(ctx, 901, 280, 200, progress)
                        assertEquals(android.graphics.Bitmap.Config.ARGB_8888, bitmap.config)
                        assertTrue(bitmap.width <= 640 && bitmap.height <= 640)
                        val partial = android.widget.RemoteViews(ctx.packageName, final.layoutId).apply {
                            setImageViewBitmap(overlayId, bitmap)
                            setViewVisibility(overlayId, View.VISIBLE)
                        }
                        partial.reapply(ctx, tree)
                        assertEquals(description, tree.contentDescription.toString())
                        assertEquals(View.IMPORTANT_FOR_ACCESSIBILITY_NO, tree.findViewById<View>(overlayId).importantForAccessibility)
                    }
                    final.reapply(ctx, tree)
                    assertEquals(View.GONE, tree.findViewById<View>(overlayId).visibility)
                    when (scene) {
                        is WidgetFaceScene.Timer -> {
                            assertTrue(description.contains("Перемена"))
                            assertEquals("Перемена", tree.findViewById<TextView>(R.id.widget_timer_phase).text.toString())
                            val ring = (tree.findViewById<android.widget.ImageView>(R.id.widget_timer_ring).drawable as android.graphics.drawable.BitmapDrawable).bitmap
                            val expected = TimerRing.bitmap(ctx, 180, 0.8f,
                                ctx.getColor(if (dark) R.color.widget_dark_text3 else R.color.widget_light_text3),
                                ctx.getColor(if (dark) R.color.widget_dark_warn else R.color.widget_light_warn))
                            assertTrue("Final ring must have the current fraction and phase color", ring.sameAs(expected))
                        }
                        is WidgetFaceScene.Room -> {
                            assertTrue(description.contains("201 Б"))
                            assertEquals("201 Б", tree.findViewById<TextView>(R.id.widget_wayfinder_room).text.toString())
                        }
                        is WidgetFaceScene.Week -> {
                            assertTrue(tree.findViewById<View>(R.id.widget_week_day1).contentDescription.contains("Сегодня", ignoreCase = true))
                            assertTrue(tree.findViewById<TextView>(R.id.widget_week_day4).text.contains("4"))
                        }
                    }
                }
                assertEquals(null, WidgetFaceEffects.timer(timer, nextTimer, WidgetMotionPolicy.Disabled))
                assertEquals(null, WidgetFaceEffects.room(room, nextRoom, WidgetMotionPolicy.Disabled))
                assertEquals(null, WidgetFaceEffects.week(week, nextWeek, WidgetMotionPolicy.Disabled))
            }
        }
    }

    @Test fun compact_schedule_and_homework_show_one_line_inside_80dp_and_clear_hidden_rows() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val copy = AndroidUiCopy(ctx)
        val schedule = ru.bgtu_voenmeh.zapara.ui.widgets.ScheduleWidgetSnapshot(extraIdentity, "Расписание", "Гость", null,
            (1..4).map { ru.bgtu_voenmeh.zapara.ui.widgets.ScheduleWidgetRow("Предмет $it", "09:00 – 10:35 · 493 ГК", false, it) })
        val homework = ru.bgtu_voenmeh.zapara.ui.widgets.HomeworkWidgetSnapshot(extraIdentity, "Домашка", "Гость", null,
            (1..4).map { ru.bgtu_voenmeh.zapara.ui.widgets.HomeworkWidgetRow("Предмет $it", "Задание · завтра", "text2", it.toLong()) })
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            fun measured(tree: View): View {
                val px = (80 * ctx.resources.displayMetrics.density).toInt()
                tree.measure(View.MeasureSpec.makeMeasureSpec((240 * ctx.resources.displayMetrics.density).toInt(), View.MeasureSpec.EXACTLY),
                    View.MeasureSpec.makeMeasureSpec(px, View.MeasureSpec.EXACTLY))
                tree.layout(0, 0, tree.measuredWidth, tree.measuredHeight)
                return tree
            }
            val scheduleTree = measured(WidgetRemoteViews.schedule(ctx, schedule, 80).apply(ctx, FrameLayout(ctx)))
            assertEquals(View.GONE, scheduleTree.named("widget_schedule_subtitle").visibility)
            assertTrue((scheduleTree.named("widget_schedule_name1") as TextView).text.contains("09:00"))
            assertEquals(View.VISIBLE, scheduleTree.named("widget_schedule_row1").visibility)
            assertTrue(scheduleTree.named("widget_schedule_row1").bottom <= scheduleTree.height)
            (2..4).forEach { assertEquals(View.GONE, scheduleTree.named("widget_schedule_row$it").visibility) }
            WidgetRemoteViews.schedule(ctx, ScheduleWidgetComposer.cleared(extraIdentity.copy(generation = 1), copy), 80).reapply(ctx, scheduleTree)
            (1..4).forEach { assertEquals("", (scheduleTree.named("widget_schedule_name$it") as TextView).text.toString()) }
            val scheduleOverlay = scheduleTree.named("widget_schedule_toss")
            assertEquals(View.IMPORTANT_FOR_ACCESSIBILITY_NO, scheduleOverlay.importantForAccessibility)

            val homeworkTree = measured(WidgetRemoteViews.homework(ctx, homework, 80).apply(ctx, FrameLayout(ctx)))
            assertEquals(View.GONE, homeworkTree.named("widget_homework_subtitle").visibility)
            assertTrue((homeworkTree.named("widget_homework_subject1") as TextView).text.contains("завтра"))
            assertTrue(homeworkTree.named("widget_homework_row1").bottom <= homeworkTree.height)
            (2..4).forEach { assertEquals(View.GONE, homeworkTree.named("widget_homework_row$it").visibility) }
            WidgetRemoteViews.homework(ctx, HomeworkWidgetComposer.cleared(extraIdentity.copy(generation = 1), copy), 80).reapply(ctx, homeworkTree)
            (1..4).forEach { assertEquals("", (homeworkTree.named("widget_homework_subject$it") as TextView).text.toString()) }
            val homeworkOverlay = homeworkTree.named("widget_homework_overlay")
            assertEquals(View.IMPORTANT_FOR_ACCESSIBILITY_NO, homeworkOverlay.importantForAccessibility)
        }
    }

    @Test fun extra_faces_fill_tall_hosts_and_keep_content_inside_minimum_sizes() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val density = ctx.resources.displayMetrics.density
        val date = LocalDate.of(2026, 9, 23)
        val way = WayfinderWidgetSnapshot(extraIdentity, "Куда идти", "Сейчас", "Математика",
            "09:00 – 10:35", "493 ГК", "493;", date, true, null)
        val week = WeekWidgetSnapshot(extraIdentity, "Неделя", "Гость", (0..6).map {
            ru.bgtu_voenmeh.zapara.ui.widgets.WeekWidgetDay(date.plusDays(it.toLong()), "Ср", 3, it == 0)
        }, null)
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            fun sized(views: android.widget.RemoteViews, width: Int, height: Int): View {
                val tree = views.apply(ctx, FrameLayout(ctx))
                tree.measure(View.MeasureSpec.makeMeasureSpec((width * density).toInt(), View.MeasureSpec.EXACTLY),
                    View.MeasureSpec.makeMeasureSpec((height * density).toInt(), View.MeasureSpec.EXACTLY))
                tree.layout(0, 0, tree.measuredWidth, tree.measuredHeight)
                return tree
            }
            fun bounds(tree: View, name: String): android.graphics.Rect {
                val child = tree.named(name)
                return android.graphics.Rect().also {
                    child.getDrawingRect(it)
                    (tree as android.view.ViewGroup).offsetDescendantRectToMyCoords(child, it)
                }
            }
            val weekTree = sized(WidgetExtraViews.week(ctx, week, 907), 320, 300)
            assertTrue("Week today chip must remain compact", bounds(weekTree, "widget_week_day1").height() <= 64 * density)
            assertTrue("Week must distribute the second row down the host", bounds(weekTree, "widget_week_day7").centerY() >= weekTree.height * 0.7f)
            val wayTree = sized(WidgetExtraViews.wayfinder(ctx, way, 908), 240, 200)
            val room = bounds(wayTree, "widget_wayfinder_room")
            assertTrue("Room area must use the middle of the card", room.centerY() >= wayTree.height * 0.35f)
            assertTrue("Time must anchor the bottom of a tall host", bounds(wayTree, "widget_wayfinder_time").bottom >= wayTree.height - 12 * density)
            val minWeek = sized(WidgetExtraViews.week(ctx, week, 907), 250, 170)
            (1..7).forEach {
                val cell = bounds(minWeek, "widget_week_day$it")
                assertTrue(cell.width() >= 48 * density - 1)
                assertTrue(cell.height() >= 48 * density - 1)
                assertTrue(cell.bottom <= minWeek.height)
            }
            val minWay = sized(WidgetExtraViews.wayfinder(ctx, way, 908), 180, 110)
            listOf("room", "subject", "time").forEach {
                val rect = bounds(minWay, "widget_wayfinder_$it")
                assertTrue("$it must remain visible", rect.height() >= 12 * density)
                assertTrue("$it must fit the minimum height", rect.bottom <= minWay.height)
            }
        }
    }

    @Test fun picker_previews_show_sample_room_and_seven_days_without_waiting_for_data() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val providers = android.appwidget.AppWidgetManager.getInstance(ctx).installedProviders
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            val way = providers.single { it.provider.className.endsWith(".WayfinderWidgetProvider") }
            val week = providers.single { it.provider.className.endsWith(".WeekWidgetProvider") }
            val wayTree = android.widget.RemoteViews(ctx.packageName, way.previewLayout).apply(ctx, FrameLayout(ctx))
            assertEquals("493 ГК", (wayTree.named("widget_wayfinder_room") as TextView).text.toString())
            assertEquals("Сейчас", (wayTree.named("widget_wayfinder_status") as TextView).text.toString())
            assertEquals("Математика", (wayTree.named("widget_wayfinder_subject") as TextView).text.toString())
            assertTrue((wayTree.named("widget_wayfinder_time") as TextView).text.contains("09:00"))
            val weekTree = android.widget.RemoteViews(ctx.packageName, week.previewLayout).apply(ctx, FrameLayout(ctx))
            listOf("Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс").forEachIndexed { index, label ->
                val cell = weekTree.named("widget_week_day${index + 1}") as TextView
                assertTrue(cell.text.toString(), cell.text.contains(label))
                assertTrue(cell.text.toString(), cell.text.any(Char::isDigit))
            }
            assertTrue(weekTree.named("widget_week_day3").background != null)
            assertFalse(way.previewLayout == way.initialLayout)
            assertFalse(week.previewLayout == week.initialLayout)
        }
    }

    @Test fun day_clicks_have_distinct_pending_intents_per_widget_and_slot() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        fun pending(id: Int, slot: Int) = WidgetIntents.open(ctx, id, slot, "schedule", "2026-09-${21 + slot}")
        val first = (0..6).map { pending(905, it) }
        assertEquals(7, first.toSet().size)
        assertEquals(first[0], pending(905, 0))
        assertFalse(first[0] == pending(906, 0))
        first.forEach { assertTrue(it.isImmutable) }
    }

    @Test fun picker_registers_both_faces_with_supported_minimum_sizes() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val providers = android.appwidget.AppWidgetManager.getInstance(ctx).installedProviders
        listOf("Wayfinder" to (180 to 110), "Week" to (250 to 170)).forEach { (name, size) ->
            val provider = providers.firstOrNull { it.provider.className.endsWith(".${name}WidgetProvider") }
            org.junit.Assert.assertNotNull("$name must appear in the widget picker", provider)
            val density = ctx.resources.displayMetrics.density
            assertEquals(size.first.toFloat(), provider!!.minResizeWidth / density, 0.5f)
            assertEquals(size.second.toFloat(), provider.minResizeHeight / density, 0.5f)
            assertTrue(provider.initialLayout != 0)
            assertTrue(provider.previewLayout != 0)
        }
    }

    private val extraIdentity = WidgetJobIdentity.of(ProfileDescriptor.guest(), 0)

    private fun extraViews(name: String, snapshot: Any, widgetId: Int = 904): android.widget.RemoteViews {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        return when (name) {
            "wayfinder" -> WidgetExtraViews.wayfinder(ctx, snapshot as WayfinderWidgetSnapshot, widgetId)
            "week" -> WidgetExtraViews.week(ctx, snapshot as WeekWidgetSnapshot, widgetId)
            else -> error("Unknown face $name")
        }
    }

    private fun View.named(name: String): View = findViewById(
        resources.getIdentifier(name, "id", context.packageName).also { assertTrue("Missing view $name", it != 0) }
    )

    @Test fun wayfinder_renders_room_status_and_spoken_date_then_clears_all_private_text() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val copy = AndroidUiCopy(ctx)
        val snapshot = ru.bgtu_voenmeh.zapara.ui.widgets.WayfinderWidgetSnapshot(
            extraIdentity, "Куда идти", "Сейчас", "Математика", "09:00 – 10:35", "493 ГК", "493;",
            LocalDate.of(2026, 9, 23), true, null
        )
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            val tree = extraViews("wayfinder", snapshot).apply(ctx, FrameLayout(ctx))
            assertEquals("493 ГК", (tree.named("widget_wayfinder_room") as TextView).text.toString())
            assertEquals("Сейчас", (tree.named("widget_wayfinder_status") as TextView).text.toString())
            assertEquals("Математика", (tree.named("widget_wayfinder_subject") as TextView).text.toString())
            val spoken = tree.contentDescription.toString()
            listOf("Математика", "493 ГК", "09:00", "23").forEach { assertTrue(spoken, spoken.contains(it)) }
            assertEquals(View.GONE, tree.named("widget_wayfinder_empty").visibility)
            val overlay = tree.named("widget_wayfinder_overlay")
            assertFalse(overlay.isClickable)
            assertFalse(overlay.isFocusable)
            assertEquals(View.IMPORTANT_FOR_ACCESSIBILITY_NO, overlay.importantForAccessibility)
            val b = extraIdentity.copy(profileId = "account-b", generation = 1)
            extraViews("wayfinder", ru.bgtu_voenmeh.zapara.ui.widgets.WayfinderWidgetComposer.cleared(b, copy)).reapply(ctx, tree)
            listOf("status", "room", "subject", "time", "empty").forEach {
                assertEquals("", (tree.named("widget_wayfinder_$it") as TextView).text.toString())
                assertEquals(View.GONE, tree.named("widget_wayfinder_$it").visibility)
            }
            assertEquals("Куда идти", tree.contentDescription.toString())
        }
    }

    @Test fun week_renders_seven_accessible_days_at_minimum_size_then_clears_them() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val copy = AndroidUiCopy(ctx)
        val names = listOf("Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс")
        val monday = LocalDate.of(2026, 9, 21)
        val snapshot = ru.bgtu_voenmeh.zapara.ui.widgets.WeekWidgetSnapshot(
            extraIdentity, "Неделя", "Гость · А863С", names.mapIndexed { i, name ->
                ru.bgtu_voenmeh.zapara.ui.widgets.WeekWidgetDay(monday.plusDays(i.toLong()), name, if (i == 2) 3 else 0, i == 2)
            }, null
        )
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            val tree = extraViews("week", snapshot).apply(ctx, FrameLayout(ctx))
            val density = ctx.resources.displayMetrics.density
            tree.measure(View.MeasureSpec.makeMeasureSpec((250 * density).toInt(), View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec((170 * density).toInt(), View.MeasureSpec.EXACTLY))
            tree.layout(0, 0, tree.measuredWidth, tree.measuredHeight)
            names.forEachIndexed { i, name ->
                val cell = tree.named("widget_week_day${i + 1}") as TextView
                assertTrue(cell.text.toString(), cell.text.contains(name))
                assertTrue(cell.text.contains((21 + i).toString()))
                assertTrue("Day ${i + 1} touch width", cell.width / density >= 48)
                assertTrue("Day ${i + 1} touch height", cell.height / density >= 48)
                assertTrue("Day ${i + 1} must fit the widget", cell.bottom <= tree.height)
                assertTrue(cell.contentDescription.toString().contains((21 + i).toString()))
            }
            assertTrue(tree.named("widget_week_day3").contentDescription.toString().contains("3"))
            assertEquals(View.GONE, tree.named("widget_week_empty").visibility)
            val overlay = tree.named("widget_week_overlay")
            assertFalse(overlay.isClickable)
            assertEquals(View.IMPORTANT_FOR_ACCESSIBILITY_NO, overlay.importantForAccessibility)
            extraViews("week", ru.bgtu_voenmeh.zapara.ui.widgets.WeekWidgetComposer.cleared(extraIdentity.copy(generation = 1), copy)).reapply(ctx, tree)
            (1..7).forEach {
                val cell = tree.named("widget_week_day$it") as TextView
                assertEquals("", cell.text.toString())
                assertEquals("", cell.contentDescription.toString())
                assertEquals(View.GONE, cell.visibility)
            }
            assertEquals("", (tree.named("widget_week_subtitle") as TextView).text.toString())
        }
    }

    @Test fun new_faces_show_empty_group_guidance() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val copy = AndroidUiCopy(ctx)
        val settings = ScheduleRepository.SettingsState()
        val way = ru.bgtu_voenmeh.zapara.ui.widgets.WayfinderWidgetComposer.fromSchedule(
            extraIdentity, settings, emptyList(), LocalDateTime.of(2026, 9, 23, 9, 0), { "" }, copy)
        val week = ru.bgtu_voenmeh.zapara.ui.widgets.WeekWidgetComposer.fromSchedule(
            extraIdentity, settings, emptyList(), LocalDate.of(2026, 9, 23), null, copy, false)
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            val wayTree = extraViews("wayfinder", way).apply(ctx, FrameLayout(ctx))
            assertEquals(View.VISIBLE, wayTree.named("widget_wayfinder_empty").visibility)
            assertEquals("Группа не выбрана", (wayTree.named("widget_wayfinder_empty") as TextView).text.toString())
            val weekTree = extraViews("week", week).apply(ctx, FrameLayout(ctx))
            assertEquals(View.VISIBLE, weekTree.named("widget_week_empty").visibility)
            assertTrue((weekTree.named("widget_week_empty") as TextView).text.contains("Выберите группу"))
        }
    }

    @Test
    fun reapply_cleared_b_on_same_tree_drops_account_a_text() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val copy = AndroidUiCopy(ctx)
        val key = "A".repeat(64)
        val userA = "11111111-1111-4111-8111-111111111111"
        val userB = "22222222-2222-4222-8222-222222222222"
        val a = WidgetJobIdentity.of(ProfileDescriptor.account(key, userA), 1)
        val b = WidgetJobIdentity.of(ProfileDescriptor.account(key, userB), 2)
        val today = LocalDate.of(2026, 9, 8)
        val cachedA = HomeworkWidgetComposer.fromHomework(
            identity = a,
            settings = ScheduleRepository.SettingsState(myGroupId = "3313"),
            homework = listOf(
                Homework(
                    9, "лек высш. математ", "secret-account-A-homework",
                    LocalDate.of(2026, 9, 1), 1, today.plusDays(5), "far", false
                )
            ),
            lessons = emptyList(),
            today = today,
            groupName = "А863С",
            displayName = { "Матан" },
            copy = copy
        )
        val scheduleA = ScheduleWidgetComposer.fromSchedule(
            identity = a,
            settings = ScheduleRepository.SettingsState(myGroupId = "3313"),
            allLessons = listOf(
                Lesson(
                    groupId = "3313", dayOfWeek = 2, parity = 0, index = 1,
                    timeStart = "09:00", timeEnd = "10:35",
                    subjectRaw = "лек ВЫСШ. МАТЕМАТ", subjectNormalized = "лек высш. математ",
                    typeRaw = "лек", roomRaw = "493", buildingRaw = "ГК", classroomRaw = "493;"
                )
            ),
            now = LocalDateTime.of(2026, 9, 8, 8, 0),
            groupName = "А863С",
            displayName = { "Матан" },
            copy = copy
        )
        assertTrue(cachedA.rows.any { it.detail.contains("secret-account-A-homework") })
        assertTrue(scheduleA.rows.any { it.name == "Матан" })
        assertFalse(WidgetJobs.accept(cachedA.identity, b))

        var hwHidden = false
        var hwLeftover = "unset"
        var hwSubject = "unset"
        var hwTitle = ""
        var schHidden = false
        var schLeftover = "unset"
        var schMeta = "unset"
        var guestTitle = ""
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            val hwParent = FrameLayout(ctx)
            val hwTree = WidgetRemoteViews.homework(ctx, cachedA).apply(ctx, hwParent)
            assertTrue(
                hwTree.findViewById<TextView>(R.id.widget_homework_detail1).text.contains("secret-account-A-homework")
            )
            WidgetRemoteViews.homework(ctx, HomeworkWidgetComposer.cleared(b, copy)).reapply(ctx, hwTree)
            hwHidden = hwTree.findViewById<View>(R.id.widget_homework_row1).visibility == View.GONE
            hwLeftover = hwTree.findViewById<TextView>(R.id.widget_homework_detail1).text.toString()
            hwSubject = hwTree.findViewById<TextView>(R.id.widget_homework_subject1).text.toString()
            hwTitle = hwTree.findViewById<TextView>(R.id.widget_homework_title).text.toString()

            val schParent = FrameLayout(ctx)
            val schTree = WidgetRemoteViews.schedule(ctx, scheduleA).apply(ctx, schParent)
            assertEquals("Матан", schTree.findViewById<TextView>(R.id.widget_schedule_name1).text.toString())
            WidgetRemoteViews.schedule(ctx, ScheduleWidgetComposer.cleared(b, copy)).reapply(ctx, schTree)
            schHidden = schTree.findViewById<View>(R.id.widget_schedule_row1).visibility == View.GONE
            schLeftover = schTree.findViewById<TextView>(R.id.widget_schedule_name1).text.toString()
            schMeta = schTree.findViewById<TextView>(R.id.widget_schedule_meta1).text.toString()

            val guest = WidgetRemoteViews.schedule(
                ctx,
                ScheduleWidgetComposer.cleared(WidgetJobIdentity.of(ProfileDescriptor.guest(), 0), copy)
            ).apply(ctx, FrameLayout(ctx))
            guestTitle = guest.findViewById<TextView>(R.id.widget_schedule_title).text.toString()
        }
        assertTrue(hwHidden)
        assertEquals("", hwLeftover)
        assertEquals("", hwSubject)
        assertFalse(hwLeftover.contains("secret-account-A-homework"))
        assertEquals(ctx.getString(R.string.nav_homework), hwTitle)
        assertTrue(schHidden)
        assertEquals("", schLeftover)
        assertEquals("", schMeta)
        assertEquals(ctx.getString(R.string.nav_schedule), guestTitle)
        assertEquals("Гость", ctx.getString(R.string.widget_guest))
    }
}
