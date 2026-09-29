package ru.bgtu_voenmeh.zapara.data.sync

import java.time.Duration

/** Server cooldown uses a monotonic clock; local edits cannot bypass Retry-After. */
internal class SyncRetryWindow(private val nanos: () -> Long = System::nanoTime) {
    private var notBefore: Long? = null
    @Synchronized fun defer(duration: Duration) {
        val millis = runCatching { duration.toMillis() }.getOrDefault(86_400_000L).coerceIn(0, 86_400_000L)
        if (millis > 0) {
            val deadline = nanos() + millis * 1_000_000L
            notBefore = notBefore?.let { maxOf(it, deadline) } ?: deadline
        }
    }
    @Synchronized fun remainingMillis(): Long {
        val remaining = (notBefore ?: return 0L) - nanos()
        return if (remaining <= 0) 0 else (remaining + 999_999L) / 1_000_000L
    }
}
