package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.nio.charset.Charset
import java.nio.charset.CodingErrorAction

class SupportLogsTest {
    @Test fun short_text_is_one_log_file() {
        val packed = SupportLogs.pack("line\n")
        assertEquals(1, packed.size)
        assertEquals("android-1.log", packed[0].first)
        assertEquals("line\n", packed[0].second.toString(Charsets.UTF_8))
    }

    @Test fun empty_text_adds_nothing() {
        assertTrue(SupportLogs.pack("").isEmpty())
        assertTrue(SupportLogs.pack("\u0000").isEmpty())
    }

    @Test fun oversized_text_keeps_the_newest_tail_as_valid_utf8() {
        val marker = "END-OF-LOG\n"
        val body = "\u0000" + "\u044F".repeat(900_000) + "\n" + marker
        val packed = SupportLogs.pack(body)
        assertTrue(packed.size in 1..SupportLogs.maxFiles)
        val strict = Charset.forName("UTF-8").newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
        val joined = packed.joinToString("") { part ->
            assertTrue(part.second.size <= SupportLogs.maxBytes)
            assertTrue(part.first.endsWith(".log"))
            strict.decode(java.nio.ByteBuffer.wrap(part.second)).toString()
        }
        assertTrue(joined.endsWith(marker))
        assertTrue(!joined.contains('\u0000'))
    }
}
