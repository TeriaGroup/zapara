package ru.bgtu_voenmeh.zapara.ui.maps
import org.junit.Assert.*
import org.junit.Test

class RouteSessionMemoryTest {
    @Test fun memories_are_bounded_distinct_and_scope_isolated() {
        val memory = RouteSessionMemory(); memory.enter("owner:group")
        repeat(12) { memory.remember("a$it", "b$it"); memory.togglePin("a$it") }
        assertEquals(6, memory.history().size); assertEquals(8, memory.pinned().size)
        memory.remember("a11", "b11"); assertEquals(6, memory.history().size)
        memory.cleared(RouteField.From,"a11"); assertNotNull(memory.undo)
        memory.enter("other:group")
        assertTrue(memory.history().isEmpty()); assertTrue(memory.pinned().isEmpty()); assertNull(memory.undo)
    }
    @Test fun removed_graph_nodes_and_later_edits_invalidate_recovery() {
        val memory = RouteSessionMemory(); memory.enter("scope")
        memory.remember("a","b"); memory.togglePin("b"); memory.cleared(RouteField.To,"b")
        memory.prune(setOf("a")); assertTrue(memory.history().isEmpty()); assertNull(memory.undo); assertTrue(memory.pinned().isEmpty())
        memory.cleared(RouteField.From,"a"); memory.changed(); assertNull(memory.undo)
    }
}
