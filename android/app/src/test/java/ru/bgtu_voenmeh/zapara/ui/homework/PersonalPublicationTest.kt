package ru.bgtu_voenmeh.zapara.ui.homework
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import java.time.LocalDate
import java.time.Instant

class PersonalPublicationTest {
    private val homework = Homework(1,"math","Task",LocalDate.of(2026,10,1),1,LocalDate.of(2026,10,2),"soon",false)
    @Test fun frozen_retry_preserves_id_payload_and_moscow_deadline() {
        val row=PersonalPublicationRow(homework,"Math",personalPublicationDeadline(homework.due))
        assertEquals(Instant.parse("2026-10-02T20:59:59Z"),row.deadline)
        val failed=row.copy(attempted=true,failed=true)
        assertEquals(row.operationId,failed.operationId); assertEquals(row.before,failed.before)
        assertTrue(row.matches(homework)); assertFalse(row.matches(homework.copy(text="Changed")))
        assertFalse(row.matches(homework.copy(done=true))); assertNull(personalPublicationDeadline(null))
    }
}
