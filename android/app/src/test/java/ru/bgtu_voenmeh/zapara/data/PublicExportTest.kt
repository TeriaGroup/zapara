package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class PublicExportTest {
    @Test
    fun planHasMapsCoordsLecturersAndGroups() {
        val plan = PublicExport.plan()
        assertEquals(12, plan.size)
        assertEquals(9, plan.count { it.name.endsWith(".jpg") })
        assertTrue(plan.any { it.name == "coords.json" && it.sub == "maps" })
        assertTrue(plan.any { it.name == PublicExport.LECTURER_FILE && it.sub == "" })
        assertTrue(plan.any { it.name == PublicExport.SCHEDULE_FILE && it.sub == "" })
        assertEquals("военмех", PublicExport.FOLDER)
    }
}
