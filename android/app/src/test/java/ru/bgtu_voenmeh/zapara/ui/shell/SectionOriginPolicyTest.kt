package ru.bgtu_voenmeh.zapara.ui.shell

import org.junit.Assert.*
import org.junit.Test

class SectionOriginPolicyTest {
    @Test fun contextual_routes_keep_their_origin_but_tab_and_external_launches_do_not() {
        assertTrue(keepsSectionOrigin(Section.Maps, "493", false))
        assertTrue(keepsSectionOrigin(Section.Group, "group-id", false))
        assertTrue(keepsSectionOrigin(Section.Settings, "account", false))
        assertTrue(keepsSectionOrigin(Section.Schedule, "2026-10-01", false))
        assertTrue(keepsSectionOrigin(Section.Homework, "математика", false))
        assertTrue(keepsSectionOrigin(Section.Teachers, "Иванов", false))
        assertFalse(keepsSectionOrigin(Section.Maps, null, false))
        assertFalse(keepsSectionOrigin(Section.Schedule, "2026-10-01", true))
        assertFalse(keepsSectionOrigin(Section.Teachers, null, false))
    }
}
