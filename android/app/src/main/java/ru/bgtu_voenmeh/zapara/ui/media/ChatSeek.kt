package ru.bgtu_voenmeh.zapara.ui.media

internal fun chatSeekPosition(positionMs: Int, deltaMs: Int, durationMs: Int): Int =
    (positionMs.toLong() + deltaMs).coerceIn(0L, durationMs.coerceAtLeast(0).toLong()).toInt()
