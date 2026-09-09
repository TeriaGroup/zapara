package ru.bgtu_voenmeh.zapara.ui.friends

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class FriendPaletteTest {
    @Test fun five_canonical_keys() {
        assertEquals(5, FriendPalette.keys.size)
        assertEquals("#F2A33C", FriendPalette.keys[0])
    }

    @Test fun index_of_known_and_unknown() {
        assertEquals(0, FriendPalette.indexOf("#F2A33C"))
        val unknown = FriendPalette.indexOf("#unknown")
        assertTrue(unknown in 0..4)
        assertEquals(unknown, FriendPalette.indexOf("#unknown"))
    }

    @Test fun first_free_skips_used_and_wraps() {
        assertEquals(FriendPalette.keys[2], FriendPalette.firstFree(listOf(FriendPalette.keys[0], FriendPalette.keys[1])))
        assertEquals(FriendPalette.keys[0], FriendPalette.firstFree(FriendPalette.keys))
    }
}
