package ru.bgtu_voenmeh.zapara.ui.media

import org.junit.Assert.assertEquals
import org.junit.Test

class ChatMediaLoadPolicyTest {
    @Test fun remote_media_waits_for_a_tap_then_reports_loading_retry_or_cached_state() {
        assertEquals(ChatMediaLoadState.NeedsTap, chatMediaLoadState(fileAvailable = false, loading = false, error = false))
        assertEquals(ChatMediaLoadState.Loading, chatMediaLoadState(fileAvailable = false, loading = true, error = false))
        assertEquals(ChatMediaLoadState.Retry, chatMediaLoadState(fileAvailable = false, loading = false, error = true))
        assertEquals(ChatMediaLoadState.Ready, chatMediaLoadState(fileAvailable = true, loading = false, error = false))
    }

    @Test fun not_found_is_distinct_from_transient_retry_and_cached_media_stays_ready() {
        assertEquals(ChatMediaLoadState.NotFound,
            chatMediaLoadState(fileAvailable = false, loading = false, error = true, notFound = true))
        assertEquals(ChatMediaLoadState.Loading,
            chatMediaLoadState(fileAvailable = false, loading = true, error = false, notFound = false))
        assertEquals(ChatMediaLoadState.Ready,
            chatMediaLoadState(fileAvailable = true, loading = false, error = true, notFound = true))
    }
}
