package ru.bgtu_voenmeh.zapara.ui.settings

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import java.time.LocalDateTime

class SettingsLogicTest {
    @Test fun notification_navigation_waits_for_save_but_other_sections_remain_available() {
        assertFalse(SettingsLogic.canLeaveSection("notifications", true))
        assertTrue(SettingsLogic.canLeaveSection("notifications", false))
        assertTrue(SettingsLogic.canLeaveSection("study", true))
    }
    private val now = LocalDateTime.of(2026, 9, 8, 12, 0)

    @Test fun updated_line() {
        assertEquals("Обновлено 06.09 14:20 · 2 дня назад", SettingsLogic.updatedLine("2026-09-06T14:20:00", now, XmlCopy))
        assertEquals("Расписание ещё не загружено", SettingsLogic.updatedLine(null, now, XmlCopy))
        assertEquals("Обновлено 08.09 09:15 · сегодня", SettingsLogic.updatedLine("2026-09-08T09:15:00", now, XmlCopy))
        assertEquals("Пары на устройстве", SettingsLogic.updatedLine(null, now, XmlCopy, hasLocal = true))
    }

    @Test fun validate_times() {
        assertNull(SettingsLogic.validateTimes("20:00", "07:30", XmlCopy))
        assertEquals("Время в формате ЧЧ:ММ", SettingsLogic.validateTimes("25:00", "07:30", XmlCopy))
        assertEquals("Время в формате ЧЧ:ММ", SettingsLogic.validateTimes("20:00", "7:30", XmlCopy))
    }

    @Test fun intermediate_time_remains_draft_and_cannot_be_saved() {
        val initial = SettingsUiState(time1 = "20:00", time2 = "07:30",
            savedTime1 = "20:00", savedTime2 = "07:30")
        val editing = SettingsLogic.editNotificationTime(initial, true, "20:", XmlCopy)
        assertEquals("20:", editing.time1)
        assertEquals("20:00", editing.savedTime1)
        assertEquals(true, editing.timeDirty)
        assertEquals("Время в формате ЧЧ:ММ", editing.timeError)
        val restored = SettingsLogic.editNotificationTime(editing, true, "20:00", XmlCopy)
        assertEquals(false, restored.timeDirty)
        assertNull(restored.timeError)
        val cancelled = SettingsLogic.cancelNotificationTimeDraft(editing)
        assertEquals("20:00", cancelled.time1)
        assertEquals("07:30", cancelled.time2)
        assertEquals(false, cancelled.timeDirty)
        assertNull(cancelled.timeError)
    }

    @Test fun support_send_waits_for_file_picker_and_async_attachment_read() {
        assertEquals(false, SettingsLogic.supportSendReady(false, true, 0))
        assertEquals(false, SettingsLogic.supportSendReady(false, false, 1))
        assertEquals(false, SettingsLogic.supportSendReady(true, false, 0))
        assertEquals(true, SettingsLogic.supportSendReady(false, false, 0))
    }
    @Test fun failed_switch_restores_only_its_persisted_field_without_losing_another_pending_choice() {
        val current = SettingsUiState(notifyEnabled = false, mapsAlpha = true,
            preferencePending = setOf("notify", "maps"))
        val saved = ScheduleRepository.SettingsState(notifyEnabled = true, mapsAlpha = false)
        val rolled = SettingsLogic.persistedPreference(current, "notify", saved)
        assertEquals(true, rolled.notifyEnabled)
        assertEquals(true, rolled.mapsAlpha)
        assertEquals(setOf("notify", "maps"), rolled.preferencePending)
    }
    @Test fun support_paste_counts_scalars_but_never_claims_server_length_is_safe_for_emoji() {
        val emoji = "🙂".repeat(2001)
        val status = SupportInputLimits.evaluate("Ошибка", emoji, reply = false)
        assertEquals(2001, status.bodyScalars)
        assertEquals(4002, status.bodyWireUnits)
        assertTrue(status.bodyOverLimit)
        assertFalse(status.canSend)
        assertTrue(SupportInputLimits.evaluate("", "Ок!", reply = true).canSend)
        assertFalse(SupportInputLimits.evaluate("", "\uD800", reply = true).canSend)
    }
    @Test fun support_draft_restore_keeps_thread_text_and_uri_without_bundling_file_bytes() {
        val source = mapOf("thread-a" to SupportLocalDraft("Тема", "Черновик ответа",
            photos = listOf(SupportAttachmentDraft("content://provider/photo", "a.png", byteArrayOf(1, 2, 3))),
            revision = 7))
        val saved = SupportDraftCodec.encode(source)
        val restored = SupportDraftCodec.decode(saved)["thread-a"]!!
        assertEquals("Тема", restored.subject)
        assertEquals("Черновик ответа", restored.body)
        assertEquals("content://provider/photo", restored.photos.single().uri)
        assertNull(restored.photos.single().bytes)
        assertEquals(7L, restored.revision)
        assertTrue(restored.copy(body = "Позже", revision = 8).revision != restored.revision)
    }
}
