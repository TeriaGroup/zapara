package ru.bgtu_voenmeh.zapara.ui.media

import java.io.IOException
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class ChatCircleMuxFlagsTest {
    @Test
    fun extractor_partial_frame_is_not_mistaken_for_codec_end_of_stream() {
        // Android 34: extractor PARTIAL=4, codec PARTIAL=8, codec EOS=4.
        assertEquals(8, ChatCircleMux.codecFlags(4))
        assertEquals(9, ChatCircleMux.codecFlags(5))
    }

    @Test
    fun encrypted_sample_is_rejected_before_remuxing() {
        assertThrows(IOException::class.java) { ChatCircleMux.codecFlags(2) }
    }
}
