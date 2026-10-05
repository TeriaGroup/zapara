package ru.bgtu_voenmeh.zapara.ui.media

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MediaPlayerLifecycleTest {
    @Test fun surface_operations_after_release_are_contained_as_a_player_failure() {
        var reached = false
        assertFalse(safeMediaPlayerLifecycleCall { error("released player") })
        assertTrue(safeMediaPlayerLifecycleCall { reached = true })
        assertTrue(reached)
    }
}
