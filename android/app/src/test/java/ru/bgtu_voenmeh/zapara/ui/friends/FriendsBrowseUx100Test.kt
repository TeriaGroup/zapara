package ru.bgtu_voenmeh.zapara.ui.friends

import org.junit.Assert.*
import org.junit.Test
import java.time.LocalDate

class FriendsBrowseUx100Test {
    private val rows = listOf(FriendUi(1, 0, "Н162С", "Семён", true, 0),
        FriendUi(2, 1, "Н162С", "Анна", false, 1))

    @Test fun searchAndEnabledFiltersComposeWithoutChangingStoredRows() {
        assertEquals(listOf(1L), browseFriends(rows, "н162 семен", 1).map { it.id })
        assertEquals(listOf(2L), browseFriends(rows, "н162", 2).map { it.id })
        assertEquals(2, rows.size)
        assertTrue(browseFriends(rows, "нет совпадений", 0).isEmpty())
    }

    @Test fun encounterDayFiltersUseActualDatesAcrossYearBoundary() {
        val today = LocalDate.of(2026, 12, 31)
        val first = FriendEncounter(today, "09:00", "Math", "G", "A", "493", "#000000", 100)
        val second = first.copy(date = today.plusDays(1))
        assertEquals(listOf(first), browseEncounters(listOf(first, second), 1, today))
        assertEquals(listOf(second), browseEncounters(listOf(first, second), 2, today))
    }

    @Test fun draftDiscardProtectionOnlyTriggersForActualChanges() {
        val draft = FriendEditorUi(1, 0, "Н162С", "Семён", 0)
        assertFalse(friendEditorDirty(draft, rows[0]))
        assertTrue(friendEditorDirty(draft.copy(members = "Анна"), rows[0]))
        assertFalse(friendEditorDirty(FriendEditorUi(null, null, "", "", 0), null))
        assertTrue(friendEditorDirty(FriendEditorUi(null, null, "Н162С", "", 0), null))
        assertFalse(friendEditorDirty(FriendEditorUi(null, null, "", "", 2), null, 2))
        assertTrue(friendEditorDirty(FriendEditorUi(null, null, "", "", 3), null, 2))
    }
}
