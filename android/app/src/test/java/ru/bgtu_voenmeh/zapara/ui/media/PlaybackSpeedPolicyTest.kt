package ru.bgtu_voenmeh.zapara.ui.media

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PlaybackSpeedPolicyTest {
    @Test fun speed_labels_use_russian_decimal_and_accessible_display_values() {
        assertEquals("1×", chatMediaPlaybackSpeedLabel(1f))
        assertEquals("1,5×", chatMediaPlaybackSpeedLabel(1.5f))
        assertEquals("2×", chatMediaPlaybackSpeedLabel(2f))
    }

    @Test fun choosing_speed_while_paused_defers_player_params_until_after_exclusive_activation() {
        var playing = false
        val calls = mutableListOf<String>()
        val policy = PlaybackSpeedPolicy(
            isPlaying = { playing },
            activateExclusive = { calls += "activate" },
            applySpeed = { calls += "apply:$it"; true },
            startPlayback = { calls += "start"; playing = true },
            releaseExclusive = { calls += "release" },
            onPlayingChanged = { calls += "playing:$it" }
        )

        policy.select(1.5f)
        assertEquals(emptyList<String>(), calls)
        assertEquals(1.5f, policy.speed)
        assertFalse(playing)

        policy.start()
        assertEquals(listOf("activate", "apply:1.5", "start", "playing:true"), calls)
        assertTrue(playing)
        assertFalse(policy.failed)
    }

    @Test fun changing_speed_during_playback_keeps_active_state_and_failure_reverts_to_applied_speed() {
        var playing = true
        var shouldApply = true
        val calls = mutableListOf<String>()
        val policy = PlaybackSpeedPolicy(
            isPlaying = { playing },
            activateExclusive = { calls += "activate" },
            applySpeed = { calls += "apply:$it"; shouldApply },
            startPlayback = { calls += "start"; playing = true },
            releaseExclusive = { calls += "release" },
            onPlayingChanged = { calls += "playing:$it" }
        )
        policy.select(2f)
        assertEquals(listOf("activate", "apply:2.0", "playing:true"), calls)
        assertEquals(2f, policy.speed)
        assertTrue(playing)

        calls.clear()
        shouldApply = false
        policy.select(1.5f)
        assertEquals(listOf("activate", "apply:1.5", "playing:true"), calls)
        assertEquals(2f, policy.speed)
        assertTrue(policy.failed)
        assertTrue(playing)
    }
}
