package ru.bgtu_voenmeh.zapara

import org.junit.Assert.assertEquals
import org.junit.Test

class StartupRecoveryTest {
    @Test fun failed_profile_restore_still_restarts_widgets_and_restores_notifications() {
        val steps = mutableListOf<String>()
        val failures = mutableListOf<String>()
        runStartupRecovery(
            restoreProfile = { steps += "profile"; throw IllegalStateException("vault unavailable") },
            restartWidgets = { steps += "widgets" },
            restoreNotifications = { steps += "notifications" },
            onFailure = { step, _ -> failures += step }
        )
        assertEquals(listOf("profile", "widgets", "notifications"), steps)
        assertEquals(listOf("profile"), failures)
    }

    @Test fun a_widget_failure_does_not_skip_notifications_or_recover_the_profile_twice() {
        val steps = mutableListOf<String>()
        val failures = mutableListOf<String>()
        runStartupRecovery(
            restoreProfile = { steps += "profile" },
            restartWidgets = { steps += "widgets"; throw IllegalStateException("launcher unavailable") },
            restoreNotifications = { steps += "notifications" },
            onFailure = { step, _ -> failures += step }
        )
        assertEquals(listOf("profile", "widgets", "notifications"), steps)
        assertEquals(listOf("widgets"), failures)
    }
}
