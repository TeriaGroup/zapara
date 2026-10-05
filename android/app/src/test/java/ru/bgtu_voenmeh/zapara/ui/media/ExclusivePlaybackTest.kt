package ru.bgtu_voenmeh.zapara.ui.media

import org.junit.Assert.assertEquals
import org.junit.Test

class ExclusivePlaybackTest {
    @Test fun playback_speed_is_limited_to_accessible_supported_steps() {
        assertEquals(listOf(1f, 1.5f, 2f), chatMediaPlaybackSpeeds)
        assertEquals(true, isChatMediaPlaybackSpeed(1.5f))
        assertEquals(false, isChatMediaPlaybackSpeed(1.25f))
    }

    @Test fun activating_a_new_player_stops_only_the_previous_active_player() {
        val stopped = mutableListOf<String>()
        val playback = ExclusivePlayback<String> { stopped.add(it) }
        playback.activate("voice-a")
        playback.activate("voice-b")
        playback.release("voice-a")
        playback.activate("circle")
        assertEquals(listOf("voice-a", "voice-b"), stopped)
    }
}
