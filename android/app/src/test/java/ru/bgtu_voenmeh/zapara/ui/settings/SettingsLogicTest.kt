package ru.bgtu_voenmeh.zapara.ui.settings

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.time.LocalDateTime

class SettingsLogicTest {
    private val now = LocalDateTime.of(2026, 9, 8, 12, 0)

    @Test fun updated_line() {
        assertEquals("Обновлено 06.09 14:20 · 2 дня назад", SettingsLogic.updatedLine("2026-09-06T14:20:00", now, XmlCopy))
        assertEquals("Расписание ещё не загружено", SettingsLogic.updatedLine(null, now, XmlCopy))
        assertEquals("Обновлено 08.09 09:15 · сегодня", SettingsLogic.updatedLine("2026-09-08T09:15:00", now, XmlCopy))
    }

    @Test fun validate_times() {
        assertNull(SettingsLogic.validateTimes("20:00", "07:30", XmlCopy))
        assertEquals("Время в формате ЧЧ:ММ", SettingsLogic.validateTimes("25:00", "07:30", XmlCopy))
        assertEquals("Время в формате ЧЧ:ММ", SettingsLogic.validateTimes("20:00", "7:30", XmlCopy))
    }
}
