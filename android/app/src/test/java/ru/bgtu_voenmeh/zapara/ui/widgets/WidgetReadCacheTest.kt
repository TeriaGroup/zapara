package ru.bgtu_voenmeh.zapara.ui.widgets

import java.time.LocalDateTime
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class WidgetReadCacheTest {
    @Test fun scope_sample_rejects_group_switch_or_failed_settings_read() {
        assertEquals("group-a", stableWidgetGroup("group-a", "group-a"))
        assertNull(stableWidgetGroup("group-a", "group-b"))
        assertNull(stableWidgetGroup(null, "group-b"))
        assertNull(stableWidgetGroup("group-a", null))
    }

    @Test fun failed_read_retains_exact_last_good_but_never_crosses_owner_or_generation() {
        val cache = WidgetReadCache<String>()
        val a = WidgetJobIdentity("owner-a", "db-a", 3)
        val at = LocalDateTime.of(2026, 10, 1, 9, 15)
        cache.enterGroup("group-a")
        cache.accept(a, "group-a", "Пары A", at)
        assertEquals("Пары A", cache.lastFor(a, "group-a")?.value)
        assertEquals(at, cache.lastFor(a, "group-a")?.readAt)
        assertNull(cache.lastFor(a, "group-b"))
        assertNull(cache.lastFor(a, null))
        assertNull(cache.lastFor(WidgetJobIdentity("owner-b", "db-b", 3), "group-a"))
        assertNull(cache.lastFor(a.copy(generation = 4), "group-a"))
        cache.enterGroup("group-b") // A→B: known group change invalidates all A faces.
        assertNull(cache.lastFor(a, "group-b"))
        cache.enterGroup("group-a")
        assertNull(cache.lastFor(a, "group-a")) // B→A failed read cannot revive old A.
        cache.enterGroup(null)
        assertNull(cache.lastFor(a, null)) // Unknown settings scope is never a cache hit.
        cache.enterGroup("group-a")
        cache.accept(a.copy(generation = 4), "group-a", "Пары A новые", at.plusMinutes(1))
        assertNull(cache.lastFor(a, "group-a"))
        assertEquals("Пары A новые", cache.lastFor(a.copy(generation = 4), "group-a")?.value)
        cache.clear()
        assertNull(cache.lastFor(a.copy(generation = 4), "group-a"))
    }
}
