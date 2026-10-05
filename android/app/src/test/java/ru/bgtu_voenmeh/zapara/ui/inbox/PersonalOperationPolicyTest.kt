package ru.bgtu_voenmeh.zapara.ui.inbox

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.social.*
import java.time.Instant

class PersonalOperationPolicyTest {
    private fun message(id: String, text: String = id, deleted: Boolean = false) = SocialMessage(id, "peer", "Имя", text, "text",
        Instant.EPOCH.plusSeconds(id.filter(Char::isDigit).toLongOrNull() ?: 0), null, null, deleted, false, false, emptyList(), null, null)

    @Test fun refresh_does_not_consume_send_but_duplicate_send_is_blocked() {
        assertTrue(canStartPersonalOperation(composing = true, refreshing = true, sending = false))
        assertFalse(canStartPersonalOperation(composing = true, refreshing = false, sending = true))
        assertFalse(canStartPersonalOperation(composing = false, refreshing = true, sending = false))
    }
    @Test fun late_refresh_retains_missing_history_and_newer_local_ack() {
        val current = listOf(message("1", "Правка"), message("101", "Отправлено"))
        val incoming = (1..100).map { message(it.toString()) }
        val merged = mergePersonalHistory(current, incoming, refreshIsCurrent = false)
        assertEquals(101, merged.size)
        assertEquals("Правка", merged.first().body)
        assertEquals("Отправлено", merged.last().body)
    }
    @Test fun late_edit_never_revives_deleted_message() {
        val removed = message("1", deleted = true)
        val edit = message("1", "Правка").copy(edited = true)
        assertTrue(personalReceipt(removed, edit, editing = true).deleted)
        assertTrue(mergePersonalHistory(listOf(removed), listOf(edit), true).single().deleted)
        assertTrue(mergePersonalHistory(listOf(edit), listOf(removed), false).single().deleted)
    }
    @Test fun reaction_and_edit_receipts_do_not_revert_each_other() {
        val edited = message("1", "Новый текст").copy(edited = true)
        val reacted = message("1", "Старый текст").copy(reactions = listOf(SocialReaction("like", 1, true)))
        val result = personalReceipt(edited, reacted, reactionOnly = true)
        assertEquals("Новый текст", result.body)
        assertTrue(result.edited)
        assertEquals(1, result.reactions.single().count)
        assertEquals(reacted.reactions, personalReceipt(reacted, edited, editing = true).reactions)
    }
}
