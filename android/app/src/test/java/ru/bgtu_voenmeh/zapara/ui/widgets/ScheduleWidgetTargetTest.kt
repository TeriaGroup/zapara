package ru.bgtu_voenmeh.zapara.ui.widgets

import java.time.LocalDate
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchScope

class ScheduleWidgetTargetTest {
    @Test fun row_target_retains_displayed_pair_and_owner_instead_of_current_widget_slot() {
        val date = LocalDate.of(2027, 1, 4)
        val row = ScheduleWidgetRow("Математика", "09:00 – 10:35", false, 1,
            date, "09:00", "математика", "group-a")
        val a = scheduleWidgetTarget(row, WidgetLaunchScope("owner-a", "db-a"))!!
        assertEquals(date, a.date)
        assertEquals("09:00", a.time)
        assertEquals("математика", a.subjectNorm)
        assertEquals("group-a", a.groupId)
        assertNotEquals(a, scheduleWidgetTarget(row, WidgetLaunchScope("owner-b", "db-b")))
        assertNotEquals(a, scheduleWidgetTarget(row.copy(date = date.plusDays(1)), a.scope))
        assertNull(scheduleWidgetTarget(row.copy(subjectNorm = null), a.scope))
    }
}
