package ru.bgtu_voenmeh.zapara.ui.shell

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class WidgetLaunchInboxTest {
    @Test fun repeated_destination_is_delivered_again() {
        val inbox = WidgetLaunchInbox()
        val first = inbox.accept("schedule", "2026-09-23")!!
        assertEquals(Section.Schedule, first.section)
        assertEquals("2026-09-23", first.argument)
        assertEquals(first, inbox.state.value)

        inbox.consume(first.id)
        assertNull(inbox.state.value)

        val second = inbox.accept("schedule", "2026-09-23")!!
        assertTrue(second.id > first.id)
        assertEquals(second, inbox.state.value)
    }

    @Test fun invalid_schedule_date_opens_schedule_without_date() {
        val launch = WidgetLaunchInbox().accept("schedule", "2026-02-30")!!
        assertEquals(Section.Schedule, launch.section)
        assertNull(launch.argument)
    }

    @Test fun blank_maps_room_opens_map_browsing() {
        val launch = WidgetLaunchInbox().accept("maps", "   ")!!
        assertEquals(Section.Maps, launch.section)
        assertNull(launch.argument)
    }

    @Test fun maps_room_is_trimmed_and_limited_to_one_hundred_characters() {
        val launch = WidgetLaunchInbox().accept("maps", "  ${"A".repeat(101)}  ")!!
        assertEquals("A".repeat(100), launch.argument)
    }

    @Test fun unsupported_section_is_rejected_without_replacing_pending_launch() {
        val inbox = WidgetLaunchInbox()
        val pending = inbox.accept("homework", "ignored")!!
        assertNull(pending.argument)
        assertNull(inbox.accept("community", null))
        assertEquals(pending, inbox.state.value)
    }

    @Test fun consuming_old_id_does_not_clear_newer_launch() {
        val inbox = WidgetLaunchInbox()
        val old = inbox.accept("maps", "101")!!
        val newer = inbox.accept("maps", "102")!!
        inbox.consume(old.id)
        assertEquals(newer, inbox.state.value)
    }
}
