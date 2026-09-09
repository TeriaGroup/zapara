package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.*

class MotionSettingsTest {
    @Test fun both_switches_required() {
        for (switch in listOf(false, true)) for (scale in listOf(-1f, 0f, 0.5f, 1f, 2f)) {
            val motion = MotionSettings(switch, scale)
            assertEquals(switch && scale > 0, motion.enabled)
            assertEquals(if (motion.enabled) 220 else 0, motion.ms(220))
        }
    }
    @Test fun durations_match_mobile_contract() {
        assertArrayEquals(intArrayOf(180,200,120,220,200,30,400,1400,1600,1200,200),
            intArrayOf(Durations.section,Durations.indicator,Durations.press,Durations.theme,Durations.cascade,
                Durations.cascadeStep,Durations.cascadeWindow,Durations.skeleton,Durations.homework,Durations.map,Durations.zoom))
        assertEquals(8, Durations.cascadeCount)
        assertEquals(0, MotionSettings.Off.ms(Int.MAX_VALUE))
        assertEquals(0, MotionSettings.On.ms(0))
    }
    @Test fun disabled_zeroes_every_table_duration() {
        val table = intArrayOf(
            Durations.section, Durations.indicator, Durations.press, Durations.theme,
            Durations.cascade, Durations.cascadeStep, Durations.cascadeWindow,
            Durations.skeleton, Durations.homework, Durations.map, Durations.zoom
        )
        for (base in table) {
            assertEquals("Off must zero $base", 0, MotionSettings.Off.ms(base))
            assertEquals(0, MotionSettings(true, 0f).ms(base))
            assertEquals(0, MotionSettings(false, 1f).ms(base))
            assertEquals(base, MotionSettings.On.ms(base))
        }
    }
    @Test(expected = IllegalArgumentException::class) fun negative_duration_rejected() { MotionSettings.On.ms(-1) }
}
