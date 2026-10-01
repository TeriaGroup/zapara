package ru.bgtu_voenmeh.zapara.ui.shell

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GroupInfo

class ShellGroupSelectionTest {
    @Test fun delayed_pre_switch_projection_cannot_replace_acknowledged_group_even_after_aba() {
        val gate = ShellProjectionGate()
        val oldA = gate.begin()
        gate.invalidate() // B group selection starts
        val b = gate.begin()
        assertTrue(!gate.mayPublish(oldA, "A", "B", ownerCurrent = true))
        assertTrue(gate.mayPublish(b, "B", "B", ownerCurrent = true))
        gate.invalidate() // later return to A must still reject the first A result
        assertTrue(!gate.mayPublish(oldA, "A", "A", ownerCurrent = true))
        assertTrue(!gate.mayPublish(gate.begin(), "A", "A", ownerCurrent = false))
    }
    @Test fun second_tap_cannot_replace_pending_group_and_failure_keeps_previous_selection() {
        val original = ShellUiState(groupId = "3313", groupName = "О3313",
            groups = listOf(GroupInfo("3313", "О3313"), GroupInfo("3314", "О3314")))
        val pending = original.beginGroupPick("3314")!!
        assertTrue(pending.groupPickPending)
        assertEquals("3313", pending.groupId)
        assertNull(pending.beginGroupPick("3313"))
        val failed = pending.copy(groupPickPending = false, groupPickError = "Не удалось")
        assertEquals("3313", failed.groupId)
        assertEquals("3314", failed.beginGroupPick("3314")?.pendingGroupId)
    }
}
