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
import java.time.LocalDate
import java.time.LocalDateTime

class WidgetRemoteViewsTest {
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
