package ru.bgtu_voenmeh.zapara.ui.shell

import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GroupInfo

class ShellSearchUx100Test {
    @Test fun groupSearchMatchesWordsAcrossNameAndCodeWithoutReordering() {
        val rows = listOf(GroupInfo("3313", "Н162С"), GroupInfo("3314", "Н163С"))
        assertEquals(listOf(rows[0]), searchGroups(rows, "н162 3313"))
        assertEquals(rows, searchGroups(rows, "  "))
        assertEquals(emptyList<GroupInfo>(), searchGroups(rows, "н162 3314"))
    }
}
