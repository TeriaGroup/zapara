package ru.bgtu_voenmeh.zapara.ui.settings

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.SupportThread
import ru.bgtu_voenmeh.zapara.data.accounts.SupportMessage
import java.time.Instant

class SupportThreadSelectionTest {
    private val a = SupportThread("a", "Первое обращение", emptyList())
    private val b = SupportThread("b", "Второе обращение", emptyList())

    @Test fun retry_keeps_exact_selected_thread_or_explicit_new_draft() {
        assertEquals("a", selectedSupportThreadId(listOf(a, b), "a", true))
        assertNull(selectedSupportThreadId(listOf(a, b), null, true))
        assertEquals("b", selectedSupportThreadId(listOf(a, b), null, false))
    }

    @Test fun ack_replaces_only_the_target_thread() {
        val updated = a.copy(subject = "Исправленное обращение")
        assertEquals(listOf(b, updated), mergeSupportThread(listOf(a, b), updated))
        assertEquals("Второе обращение", b.subject)
    }

    @Test fun stale_history_get_cannot_remove_acknowledged_reply_or_new_thread() {
        val note = SupportMessage("user", "Новый ответ", Instant.parse("2026-10-01T09:00:00Z"))
        val acknowledged = a.copy(messages = listOf(note))
        val stale = mergeSupportAcknowledged(listOf(a, b), listOf(acknowledged))
        assertEquals(acknowledged, stale.first { it.id == "a" })
        assertEquals(b, stale.first { it.id == "b" })
        assertEquals(listOf(b, acknowledged), mergeSupportAcknowledged(listOf(b), listOf(acknowledged)))
        assertEquals(listOf(acknowledged, b), mergeSupportAcknowledged(listOf(acknowledged, b), listOf(acknowledged)))
    }
}
