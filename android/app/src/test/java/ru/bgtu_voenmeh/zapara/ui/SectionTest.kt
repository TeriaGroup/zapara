package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.shell.Section

class SectionTest {
    @Test fun three_in_the_bar_six_in_the_sheet() {
        assertEquals(listOf(Section.Schedule, Section.Maps, Section.Homework), Section.bar)
        assertEquals(
            listOf(Section.Week, Section.Summary, Section.Teachers, Section.Friends, Section.Community, Section.Settings),
            Section.sheet
        )
        assertEquals(9, Section.entries.size)
    }

    @Test fun community_is_sheet_not_bar() {
        assertEquals("community", Section.Community.route)
        assertFalse(Section.Community.inBar)
        assertEquals("Sections.Community", Section.Community.tag)
        assertEquals(R.string.nav_community, Section.Community.title)
        assertEquals(R.drawable.ic_community, Section.Community.icon)
        assertEquals(Section.Community, Section.byRoute("community"))
    }

    @Test fun routes_are_unique_and_parse_with_arguments() {
        assertEquals(Section.entries.size, Section.entries.map { it.route }.toSet().size)
        assertEquals(Section.Schedule, Section.byRoute("schedule?date={date}"))
        assertEquals(Section.Maps, Section.byRoute("maps?room={room}"))
        assertEquals(null, Section.byRoute(null))
    }

    @Test fun tags_follow_the_scheme() {
        assertEquals("Nav.Schedule", Section.Schedule.tag)
        assertEquals("Sections.Settings", Section.Settings.tag)
        assertEquals("Sections.Community", Section.Community.tag)
    }
}
