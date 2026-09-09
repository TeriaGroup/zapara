package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.*
import org.junit.Test
import java.io.File
import java.time.LocalDate

class RussianNotificationTest {
    @Test fun notification_locale_is_not_selected_from_stored_language() {
        val source = File("src/main/java/ru/bgtu_voenmeh/zapara/data/Notifications.kt").readText()
        assertFalse(source.contains("s.language"))
        assertFalse(source.contains("No lessons"))
        assertFalse(source.contains("[HW!]"))
    }

    @Test fun intersection_has_no_english_presentation_api() {
        assertFalse(Intersection::class.java.methods.any { it.name == "scoreToTextEn" })
    }

    @Test fun empty_schedule_retains_russian_and_original_data() {
        val text = NotificationText.build(
            LocalDate.of(2026, 9, 8), "3313", emptyList(), { it.subjectRaw }, { null },
            false, { "Вторник" }, { "чётная" }, "Нет занятий"
        )
        assertEquals("Вторник, чётная: Нет занятий", text)
        assertEquals("Вторник", NotificationText.localDayName(LocalDate.of(2026, 9, 8)))
    }
}
