package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.ui.shell.GroupPickerLogic
import ru.bgtu_voenmeh.zapara.ui.shell.GroupPickerLogic.Header
import ru.bgtu_voenmeh.zapara.ui.shell.Section
import ru.bgtu_voenmeh.zapara.ui.shell.SectionsGrid
import java.io.File

/** #108: «Разделы» (AN-18) и выбор группы (AN-19). */
class SectionsPickerTest {
    private fun src(path: String) = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/$path").readText()

    @Test fun sections_sheet_is_two_columns_then_settings_without_tab_duplicates() {
        val rows = SectionsGrid.rows(SectionsGrid.tiles + Section.Settings, columns = 2)
        assertEquals(listOf(listOf(Section.Week, Section.Summary), listOf(Section.Teachers, Section.Friends),
            listOf(Section.Community, Section.Group), listOf(Section.Settings)), rows)
        assertTrue(SectionsGrid.tiles.none { it in Section.bar })
        assertEquals("даже если вкладку передали, в сетке её нет", rows,
            SectionsGrid.rows(listOf(Section.Schedule) + SectionsGrid.tiles + Section.Settings, columns = 2))
        assertEquals(7, SectionsGrid.rows(SectionsGrid.tiles + Section.Settings, columns = 1).size)
    }

    @Test fun every_section_has_its_own_icon() {
        assertEquals(Section.entries.size, Section.entries.map { it.icon }.toSet().size)
    }

    @Test fun picker_drops_the_currently_selected_line() {
        assertFalse(src("shell/Sheets.kt").contains("R.string.ux100_common_current_group"))
    }

    private val groups = listOf(GroupInfo("1", "И831Б"), GroupInfo("2", "А863С"), GroupInfo("3", "И832Б"),
        GroupInfo("4", "О711Б"), GroupInfo("5", "123"))

    @Test fun recent_first_then_faculties_in_order() {
        val blocks = GroupPickerLogic.blocks(groups, currentId = "3", recentIds = listOf("4", "3"), searching = false)
        assertEquals(listOf(Header.Recent, Header.Faculty("А"), Header.Faculty("И"), Header.Other), blocks.map { it.header })
        assertEquals(listOf("И832Б", "О711Б"), blocks[0].groups.map { it.name })
        assertEquals(listOf("И831Б"), blocks[2].groups.map { it.name })
        assertEquals("группа не повторяется", groups.size, blocks.sumOf { it.groups.size })
    }

    @Test fun search_shows_found_groups_by_faculty_without_recent() {
        val blocks = GroupPickerLogic.blocks(groups.filter { it.name.startsWith("И") }, "3", listOf("4"), searching = true)
        assertEquals(listOf(Header.Faculty("И")), blocks.map { it.header })
    }

    @Test fun recent_list_keeps_three_latest_without_repeats() {
        assertEquals(listOf("4", "1", "2"), GroupPickerLogic.pushRecent(listOf("1", "2", "3"), "4"))
        assertEquals(listOf("2", "1", "3"), GroupPickerLogic.pushRecent(listOf("1", "2", "3"), "2"))
        assertEquals("Факультет И", XmlCopy.get("group_picker_faculty", "И"))
    }
}
