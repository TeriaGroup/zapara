package ru.bgtu_voenmeh.zapara.ui.media

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

internal val chatMediaPlaybackSpeeds = listOf(1f, 1.5f, 2f)

internal fun isChatMediaPlaybackSpeed(speed: Float): Boolean = speed in chatMediaPlaybackSpeeds

internal fun chatMediaPlaybackSpeedLabel(speed: Float): String = when (speed) {
    1f -> "1×"
    1.5f -> "1,5×"
    2f -> "2×"
    else -> "${speed}×"
}

internal class PlaybackSpeedPolicy(
    private val isPlaying: () -> Boolean,
    private val activateExclusive: () -> Unit,
    private val applySpeed: (Float) -> Boolean,
    private val startPlayback: () -> Unit,
    private val releaseExclusive: () -> Unit,
    private val onPlayingChanged: (Boolean) -> Unit
) {
    var speed by mutableStateOf(1f)
        private set
    var failed by mutableStateOf(false)
        private set
    private var pendingSpeed = 1f
    private var appliedSpeed = 1f

    fun select(next: Float) {
        if (!isChatMediaPlaybackSpeed(next)) return
        pendingSpeed = next
        speed = next
        failed = false
        if (!isPlaying()) return
        activateExclusive()
        applyPendingSpeed()
        syncPlayingState()
    }

    fun start() {
        activateExclusive()
        if (pendingSpeed != appliedSpeed) applyPendingSpeed()
        if (!isPlaying()) startPlayback()
        syncPlayingState()
    }

    private fun applyPendingSpeed() {
        try {
            if (applySpeed(pendingSpeed)) {
                appliedSpeed = pendingSpeed
                speed = appliedSpeed
                failed = false
            } else rejectPendingSpeed()
        } catch (_: Exception) { rejectPendingSpeed() }
    }

    private fun rejectPendingSpeed() {
        pendingSpeed = appliedSpeed
        speed = appliedSpeed
        failed = true
    }

    private fun syncPlayingState() {
        val playing = isPlaying()
        onPlayingChanged(playing)
        if (!playing) releaseExclusive()
    }
}
