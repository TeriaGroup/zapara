package ru.bgtu_voenmeh.zapara.ui.media

internal fun safeMediaPlayerLifecycleCall(action: () -> Unit): Boolean = try {
    action()
    true
} catch (_: Exception) { false }
