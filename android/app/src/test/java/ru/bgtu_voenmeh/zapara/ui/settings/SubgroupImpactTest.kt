package ru.bgtu_voenmeh.zapara.ui.settings

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.*
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.time.LocalDate

class SubgroupImpactTest {
    @Test fun preview_does_not_change_choice_and_refuses_changed_scope_source_or_choices() {
        val date = LocalDate.of(2026, 9, 28)
        val ctx = SchedCtx("a", date, 2, false)
        val row = Lesson(groupId = "a", dayOfWeek = 1, timeStart = "09:00", timeEnd = "10:30", subjectRaw = "пр Математика",
            subjectNormalized = "математика", teacherRaw = "Первый", classroomRaw = "320")
        val raw = listOf(row, row.copy(index = 1, teacherRaw = "Второй", classroomRaw = "321"))
        val stream = Subgroups.index(raw).streams.single()
        val choices = emptyMap<String, String>()
        val event = SettingsEvent.Subgroup(stream.id, stream.options.first().id, "a", "owner")
        val preview = subgroupImpact(event, ctx, choices, raw, date, XmlCopy)
        assertTrue(choices.isEmpty())
        assertEquals(1, preview.changes.single().removed.size)
        assertTrue(preview.stillMatches("owner", ctx, choices, raw))
        assertFalse(preview.stillMatches("other", ctx, choices, raw))
        assertFalse(preview.stillMatches("owner", ctx, choices + (stream.id to stream.options.last().id), raw))
        assertFalse(preview.stillMatches("owner", ctx, choices, raw.dropLast(1)))
    }
    @Test fun diagnostics_allowlist_rejects_dynamic_identity_content() {
        val text = safeSupportDiagnostics("token/path/name", 37, true, true, 5, 2, XmlCopy)
        assertFalse(text.contains("token/path/name"))
        assertTrue(text.contains("Версия: неизвестна"))
        assertTrue(text.contains("Android API 37"))
        assertTrue(text.contains("синхронизации: 5"))
    }
}
