package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.communities.CatalogRules
import java.io.File

/** #109 / AN-22: каталог сообществ. */
class CommunityCatalogTest {
    private val section = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/communities/CommunitiesSection.kt").readText()

    @Test fun one_status_slot_role_first_then_request_state() {
        assertEquals(R.string.group_role_headman, CatalogRules.status("headman", "pending", joining = true))
        assertEquals(R.string.group_role_member, CatalogRules.status("member", null, joining = false))
        assertEquals(R.string.ux30_community_joining, CatalogRules.status(null, "pending", joining = true))
        assertEquals(R.string.community_pending, CatalogRules.status(null, "pending", joining = false))
        assertEquals(R.string.ux30_community_join_accepted, CatalogRules.status(null, "accepted", joining = false))
        assertNull(CatalogRules.status(null, null, joining = false))
        assertEquals("один чип на строку", 1, Regex("ZChip\\(stringResource\\(it\\), tag = \"Community.Status.").findAll(section).count())
        assertFalse(section.contains("Community.Role.") || section.contains("Community.Pending."))
    }

    @Test fun join_is_the_only_button_and_only_without_a_status() {
        assertTrue(section.contains("if (status == null && item.canJoin) {"))
    }

    @Test fun counters_only_from_ten_items() {
        assertFalse(CatalogRules.showCounters(9))
        assertTrue(CatalogRules.showCounters(10))
        assertTrue(section.contains("if (CatalogRules.showCounters(state.communities.size) || query.isNotBlank()) item(\"result\")"))
    }
}
