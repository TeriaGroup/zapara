package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class RequestTokensTest {
    @Test fun old_a_terminal_after_a_to_b_to_a_cannot_clear_new_a() {
        val pending = RequestTokens<String>()
        val oldA = pending.begin("owner/group-a/task")!!
        assertNull(pending.begin("owner/group-a/task"))
        assertNotNull(pending.begin("owner/group-b/task"))
        pending.clear()
        val newA = pending.begin("owner/group-a/task")!!
        assertFalse(pending.finish("owner/group-a/task", oldA))
        assertTrue(pending.current("owner/group-a/task", newA))
        assertTrue(pending.finish("owner/group-a/task", newA))
    }
}
