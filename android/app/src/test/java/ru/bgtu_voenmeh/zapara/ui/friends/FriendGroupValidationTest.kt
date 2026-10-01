package ru.bgtu_voenmeh.zapara.ui.friends

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GroupInfo

class FriendGroupValidationTest {
    private val groups = listOf(GroupInfo("3313", "А863С"))

    @Test fun catalog_rejects_typo_but_resolves_known_id_or_name() {
        assertNull(validFriendGroupName("А863СС", groups, manualOffline = false))
        assertEquals("А863С", validFriendGroupName("3313", groups, manualOffline = false))
        assertEquals("А863С", validFriendGroupName(" а863с ", groups, manualOffline = false))
    }

    @Test fun explicit_offline_mode_can_accept_manual_name_without_catalog() {
        assertNull(validFriendGroupName("А863С", emptyList(), manualOffline = false))
        assertEquals("А863С", validFriendGroupName(" А863С ", emptyList(), manualOffline = true))
        assertNull(validFriendGroupName(" А ", emptyList(), manualOffline = true))
    }
}
