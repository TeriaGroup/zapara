package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertEquals
import org.junit.Test

class HomeworkBatchTest {
    @Test fun selected_batch_ignores_completed_stale_and_duplicate_ids() {
        val group = HomeworkGroupUi(GroupStatus.Soon, "Скоро", listOf(
            HomeworkItemUi(1, "А", "Сдать", "завтра", "soon", false),
            HomeworkItemUi(2, "Б", "Готово", "сегодня", "done", true),
            HomeworkItemUi(3, "В", "Решить", "позже", "later", false)
        ), false)
        assertEquals(listOf(3L, 1L), bulkEligibleIds(listOf(group), listOf(3, 2, 3, 99, 1)))
    }
}
