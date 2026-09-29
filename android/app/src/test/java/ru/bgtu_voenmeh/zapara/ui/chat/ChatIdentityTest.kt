package ru.bgtu_voenmeh.zapara.ui.chat

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

class ChatIdentityTest {
    @Test fun initials_respect_words_unicode_and_empty_names() {
        assertEquals("МБ", avatarInitials("  Максим   Бова  "))
        assertEquals("DU", avatarInitials("dufa14"))
        assertEquals("А", avatarInitials("А"))
        assertEquals("?", avatarInitials("😀 ✨"))
        assertEquals("АИ", avatarInitials("😀 Анна Иванова"))
    }
    @Test fun dates_use_local_day_and_keep_old_years_visible() {
        val today = LocalDate.of(2026, 9, 28)
        assertEquals("Сегодня", chatDayLabel(today, today, "Сегодня", "Вчера"))
        assertEquals("Вчера", chatDayLabel(today.minusDays(1), today, "Сегодня", "Вчера"))
        assertTrue(chatDayLabel(today.minusYears(1), today, "Сегодня", "Вчера").contains("2025"))
        assertEquals("00:30", chatListTime(Instant.parse("2026-09-27T21:30:00Z"), today, ZoneId.of("Europe/Moscow"), "Вчера"))
        assertEquals("Вчера", chatListTime(Instant.parse("2026-09-27T12:00:00Z"), today, ZoneId.of("Europe/Moscow"), "Вчера"))
    }
    @Test fun message_clusters_stop_at_author_date_time_and_reply_boundaries() {
        val zone = ZoneId.of("UTC")
        fun message(time: String, sender: String = "a", reply: String? = null) = SocialMessage("id", sender, "Имя", "Текст", "text",
            Instant.parse(time), reply, null, false, false, false, emptyList(), null, null)
        val first = message("2026-09-28T12:00:00Z")
        assertTrue(samePersonalCluster(first, message("2026-09-28T12:01:00Z"), zone))
        assertFalse(samePersonalCluster(first, message("2026-09-28T12:06:00Z"), zone))
        assertFalse(samePersonalCluster(first, message("2026-09-28T12:01:00Z", "b"), zone))
        assertFalse(samePersonalCluster(first, message("2026-09-28T12:01:00Z", reply = "quoted"), zone))
        assertFalse(samePersonalCluster(message("2026-09-28T23:59:00Z"), message("2026-09-29T00:00:00Z"), zone))
    }
}
