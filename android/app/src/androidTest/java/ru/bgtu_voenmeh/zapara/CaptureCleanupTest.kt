package ru.bgtu_voenmeh.zapara

import org.junit.Assert.*
import org.junit.Test

class CaptureCleanupTest {
    @Test fun fontFailureCannotSkipNetworkRestore() {
        val calls = mutableListOf<String>()
        val fontError = IllegalStateException("injected font restore")
        val actual = runCatching {
            restoreCaptureState(null, { calls += "network" }, { calls += "font"; throw fontError })
        }.exceptionOrNull()
        assertSame(fontError, actual)
        assertEquals(listOf("network", "font"), calls)
    }

    @Test fun readbackFailureCannotSkipFontRestore() {
        var fontRestored = false
        val actual = runCatching {
            restoreCaptureState(null, { throw IllegalStateException("network readback") }, { fontRestored = true })
        }.exceptionOrNull()
        assertEquals("network readback", actual?.message)
        assertTrue(fontRestored)
    }

    @Test fun originalBodyFailureRetainsBothCleanupFailures() {
        val body = IllegalArgumentException("original body")
        val actual = runCatching {
            try { throw body } finally {
                restoreCaptureState(body,
                    { throw IllegalStateException("network readback") },
                    { throw IllegalStateException("font restore") })
            }
        }.exceptionOrNull()
        assertSame(body, actual)
        assertEquals(listOf("network readback", "font restore"), body.suppressed.map { it.message })
    }
}
