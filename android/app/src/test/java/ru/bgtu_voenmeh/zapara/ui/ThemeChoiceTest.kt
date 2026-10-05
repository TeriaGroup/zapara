package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice

class ThemeChoiceTest {
    @Test fun keys_round_trip() {
        ThemeChoice.entries.forEach { assertEquals(it, ThemeChoice.fromKey(it.key)) }
    }
    @Test fun unknown_keys_follow_system() {
        listOf(null, "", "unknown", "DARK").forEach { assertEquals(ThemeChoice.System, ThemeChoice.fromKey(it)) }
    }
    @Test fun choice_resolves_both_system_modes() {
        for (system in listOf(false, true)) {
            assertEquals(system, ThemeChoice.System.isDark(system))
            assertTrue(ThemeChoice.Dark.isDark(system))
            assertFalse(ThemeChoice.Light.isDark(system))
        }
    }
}
