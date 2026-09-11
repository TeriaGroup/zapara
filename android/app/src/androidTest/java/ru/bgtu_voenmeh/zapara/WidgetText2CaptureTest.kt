package ru.bgtu_voenmeh.zapara

import android.graphics.Bitmap
import android.graphics.Canvas
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import androidx.compose.ui.graphics.toArgb
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.DarkColors
import ru.bgtu_voenmeh.zapara.ui.theme.LightColors
import ru.bgtu_voenmeh.zapara.ui.widgets.*
import java.io.File

/** Actual RemoteViews with synthetic snapshots; no repository or launcher writes. */
class WidgetText2CaptureTest {
    @Test fun secondary_text_renders_in_both_widget_themes() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val identity = WidgetJobIdentity("guest", "synthetic", 0)
        instrumentation.runOnMainSync {
            for (dark in listOf(false, true)) {
                val palette = if (dark) DarkColors else LightColors
                val parent = LinearLayout(context).apply {
                    orientation = LinearLayout.VERTICAL
                    setBackgroundColor(palette.canvas.toArgb())
                }
                val schedule = ScheduleWidgetSnapshot(identity, "Расписание", "Гость · ИВТ-123", null,
                    listOf(ScheduleWidgetRow("Математика", "09:00 – 10:35 · ГК 493", false)), isDark = dark)
                val homework = HomeworkWidgetSnapshot(identity, "Домашка", "Гость · ИВТ-123", null,
                    listOf(HomeworkWidgetRow("Математика", "Решить задачу · 15 сентября", "text2")), isDark = dark)
                val scheduleView = WidgetRemoteViews.schedule(context, schedule).apply(context, parent)
                val homeworkView = WidgetRemoteViews.homework(context, homework).apply(context, parent)
                parent.addView(scheduleView)
                parent.addView(homeworkView)
                assertEquals(palette.text2.toArgb(), scheduleView.findViewById<TextView>(R.id.widget_schedule_meta1).currentTextColor)
                assertEquals(palette.text2.toArgb(), homeworkView.findViewById<TextView>(R.id.widget_homework_detail1).currentTextColor)
                val width = (360 * context.resources.displayMetrics.density).toInt()
                parent.measure(View.MeasureSpec.makeMeasureSpec(width, View.MeasureSpec.EXACTLY),
                    View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED))
                parent.layout(0, 0, width, parent.measuredHeight)
                assertTrue(parent.height > 0)
                val bitmap = Bitmap.createBitmap(width, parent.height, Bitmap.Config.ARGB_8888)
                parent.draw(Canvas(bitmap))
                val directory = File(context.getExternalFilesDir(null), "task2-fix-round1")
                check(directory.isDirectory || directory.mkdirs())
                val theme = if (dark) "dark" else "light"
                File(directory, "widget-$theme.png").outputStream().use {
                    check(bitmap.compress(Bitmap.CompressFormat.PNG, 100, it))
                }
                bitmap.recycle()
            }
        }
    }
}
