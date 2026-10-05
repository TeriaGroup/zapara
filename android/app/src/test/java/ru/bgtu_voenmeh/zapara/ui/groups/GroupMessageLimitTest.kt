package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class GroupMessageLimitTest {
    @Test fun composed_limit_counts_normalized_context_separator_and_unicode_scalars() {
        val context = "К".repeat(1995)
        val preview = groupMessagePreview("😀\r\nБ", context, editing = false)
        assertEquals(2000, preview.scalars)
        assertTrue(preview.ready)
        assertEquals(context + "\n\n😀\nБ", preview.body)
        assertEquals(GroupMessageProblem.TooLong,
            groupMessagePreview("😀\nБВ", context, editing = false).problem)
    }

    @Test fun overlimit_and_invalid_input_stay_available_for_editing() {
        val draft = "А".repeat(2001)
        val preview = groupMessagePreview(draft, null, editing = false)
        assertFalse(preview.ready)
        assertEquals(2001, preview.body.length)
        assertEquals(GroupMessageProblem.Invalid,
            groupMessagePreview("Текст\u0000", null, editing = false).problem)
    }

    @Test fun multiline_text_keeps_user_line_breaks_in_wire_body() {
        val preview = groupMessagePreview("Первая\r\n\r\nВторая\n", null, editing = false)
        assertTrue(preview.ready)
        assertEquals("Первая\n\nВторая\n", preview.body)
    }
}
