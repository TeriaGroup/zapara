package ru.bgtu_voenmeh.zapara

import android.view.View
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchScope
import ru.bgtu_voenmeh.zapara.ui.widgets.HomeworkWidgetRow
import ru.bgtu_voenmeh.zapara.ui.widgets.HomeworkWidgetSnapshot
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetIntents
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetJobIdentity
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetRemoteViews

class WidgetHomeworkActionsTest {
    @Test fun task_actions_cannot_be_retargeted_by_another_slot_id_or_profile() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val scope = WidgetLaunchScope("account-a", "profiles/a/zapara.db")
        fun action(widget: Int = 801, slot: Int = 0, id: Long = 41, target: WidgetLaunchScope = scope) =
            WidgetIntents.homework(context, widget, slot, id, target)
        val original = action()
        assertEquals(original, action())
        assertNotEquals(original, action(widget = 802))
        assertNotEquals(original, action(slot = 1))
        assertNotEquals(original, action(id = 42))
        assertNotEquals(original, action(target = scope.copy(profileId = "account-b")))
        assertNotEquals(original, action(target = scope.copy(databaseName = "profiles/b/zapara.db")))
        assertTrue(original.isImmutable)
    }

    @Test fun removing_or_clearing_rows_removes_their_text_and_click_action_on_reapply() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val identity = WidgetJobIdentity("account-a", "profiles/a/zapara.db", 7)
        val full = HomeworkWidgetSnapshot(identity, "Домашка", "Группа", null,
            listOf(HomeworkWidgetRow("Физика", "Читать", "text2", 41)))
        instrumentation.runOnMainSync {
            val tree = WidgetRemoteViews.homework(context, full, 160, 801).apply(context, FrameLayout(context))
            val row = tree.findViewById<View>(R.id.widget_homework_row1)
            assertTrue(row.hasOnClickListeners())
            assertTrue(tree.findViewById<View>(R.id.widget_homework_root).hasOnClickListeners())
            for (next in listOf(full.copy(rows = emptyList()), full.copy(cleared = true))) {
                WidgetRemoteViews.homework(context, next, 160, 801).reapply(context, tree)
                assertEquals(View.GONE, row.visibility)
                assertFalse(row.hasOnClickListeners())
                assertNull(row.contentDescription)
                assertEquals("", tree.findViewById<TextView>(R.id.widget_homework_subject1).text.toString())
                assertEquals("", tree.findViewById<TextView>(R.id.widget_homework_detail1).text.toString())
            }
        }
    }
}
