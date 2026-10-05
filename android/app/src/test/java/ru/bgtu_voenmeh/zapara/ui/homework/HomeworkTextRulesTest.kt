package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HomeworkTextRulesTest {
    @Test fun scalar_limit_accepts_supplementary_characters_without_truncation() {
        val exact = "😀".repeat(4000)
        assertEquals(4000, HomeworkTextRules.scalars(exact))
        assertTrue(HomeworkTextRules.valid(exact))
        val over = exact + "А"
        assertEquals(4001, HomeworkTextRules.scalars(over))
        assertFalse(HomeworkTextRules.valid(over))
        assertEquals(8001, over.length)
        assertFalse(HomeworkTextRules.valid("А".repeat(4000) + " "))
    }

    @Test fun invalid_surrogate_and_blank_drafts_never_report_save_ready() {
        assertFalse(HomeworkTextRules.valid("  "))
        assertFalse(HomeworkTextRules.valid("Задание\uD83D"))
        assertFalse(HomeworkTextRules.valid("Задание\u0000"))
    }
}
