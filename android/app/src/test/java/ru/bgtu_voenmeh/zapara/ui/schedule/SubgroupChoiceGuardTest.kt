package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Subgroups

class SubgroupChoiceGuardTest {
    private val streams = listOf(Subgroups.Stream("stream-a", "Лабораторная", listOf(
        Subgroups.Option("one", "1"), Subgroups.Option("two", "2")), false))

    @Test fun old_stream_or_option_cannot_be_applied_to_a_new_group_catalog() {
        assertTrue(subgroupOptionExists(streams, "stream-a", "one"))
        assertFalse(subgroupOptionExists(streams, "stream-a", "stale"))
        assertFalse(subgroupOptionExists(emptyList(), "stream-a", "one"))
    }

    @Test fun undo_requires_exact_owner_group_and_current_choice_including_none() {
        val undo = SubgroupUndoUi("profile-a", "group-a", "stream-a", "one", "two")
        assertTrue(undo.allows("profile-a", "group-a", "two", streams))
        assertFalse(undo.allows("profile-a", "group-b", "two", streams))
        assertFalse(undo.allows("profile-b", "group-a", "two", streams))
        assertFalse(undo.allows("profile-a", "group-a", "one", streams))
        assertFalse(undo.allows("profile-a", "group-a", "two", emptyList()))
        val clear = SubgroupUndoUi("profile-a", "group-a", "stream-a", "one", null)
        assertTrue(clear.allows("profile-a", "group-a", null, streams))
    }
}
