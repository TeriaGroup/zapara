package ru.bgtu_voenmeh.zapara.ui.chat

import org.junit.Assert.*
import org.junit.Test

class ChatViewportAnchorTest {
    @Test fun keyboard_and_multiline_resize_keep_latest_in_view() {
        assertTrue(shouldKeepLatestOnResize(ChatViewport(500, 24, 23, false), ChatViewport(200, 24, 18, false)))
        assertTrue(shouldKeepLatestOnResize(ChatViewport(200, 24, 23, false), ChatViewport(170, 24, 22, false)))
        assertTrue(shouldKeepLatestOnResize(ChatViewport(170, 24, 23, false), ChatViewport(500, 24, 23, false)))
    }

    @Test fun an_older_reader_or_active_drag_keeps_their_position() {
        assertFalse(shouldKeepLatestOnResize(ChatViewport(500, 24, 8, false), ChatViewport(200, 24, 5, false)))
        assertFalse(shouldKeepLatestOnResize(ChatViewport(500, 24, 23, true), ChatViewport(200, 24, 18, true)))
    }

    @Test fun data_loading_and_zero_height_are_not_keyboard_resizes() {
        assertFalse(shouldKeepLatestOnResize(ChatViewport(200, 24, 23, false), ChatViewport(200, 25, 23, false)))
        assertFalse(shouldKeepLatestOnResize(ChatViewport(0, 0, -1, false), ChatViewport(200, 24, 23, false)))
        assertFalse(shouldKeepLatestOnResize(ChatViewport(200, 24, 23, false), ChatViewport(0, 24, -1, false)))
    }
}
