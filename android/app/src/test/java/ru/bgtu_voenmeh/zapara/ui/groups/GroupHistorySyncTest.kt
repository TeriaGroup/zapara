package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.*
import org.junit.Test
import java.time.Instant

class GroupHistorySyncTest {
    private fun row(id: Int, text: String = "Текст") = GroupMessageUi(id.toString(), "Имя", text, "12:00", false,
        createdAt = Instant.EPOCH.plusSeconds(id.toLong()))
    @Test fun own_send_during_catchup_does_not_discard_intermediate_history() {
        val before = listOf(row(1))
        val current = before + row(101, "Моё сообщение")
        val result = mergeGroupHistory(current, before, (2..100).map { row(it) }, emptyList())
        assertEquals((1..101).map(Int::toString), result.map { it.id })
        assertEquals("Моё сообщение", result.last().body)
    }
    @Test fun recent_page_only_amends_known_rows_and_preserves_local_receipts() {
        val before = listOf(row(1))
        val current = listOf(row(1, "Новая локальная правка"))
        val result = mergeGroupHistory(current, before, emptyList(), listOf(row(1, "Старый ответ"), row(99)))
        assertEquals(1, result.size)
        assertEquals("Новая локальная правка", result.single().body)
    }
}
