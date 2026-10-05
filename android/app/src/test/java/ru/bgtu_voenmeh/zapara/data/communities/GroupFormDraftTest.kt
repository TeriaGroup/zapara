package ru.bgtu_voenmeh.zapara.data.communities
import org.junit.Test
import org.junit.Assert.*
import java.time.Instant
import java.util.UUID
class GroupFormDraftTest {
    private val now = Instant.parse("2026-09-26T12:00:00Z")
    private fun question(kind:String = "shortText", options:List<String> = emptyList()) = GroupFormQuestion(UUID.randomUUID().toString(), "Question", kind, true, options)
    private fun draft(q:List<GroupFormQuestion> = listOf(question())) = GroupFormDraft("Title", "", null, false, q)
    @Test fun choice_to_text_clears_hidden_options_in_the_payload() {
        val value = draft(listOf(question("singleChoice", listOf("Yes","No")).copy(kind="shortText"))).normalized()
        assertTrue(value.questions.single().options.isEmpty()); assertNull(value.problem(now)); assertTrue(value.body().contains("\"options\":[]"))
    }
    @Test fun server_length_count_and_deadline_boundaries_are_enforced_before_publish() {
        assertNull(draft().copy(title="x".repeat(120), description="x".repeat(2000), questions=(1..30).map { question().copy(title="x".repeat(400)) }).problem(now))
        assertEquals("title", draft().copy(title="x".repeat(121)).problem(now)); assertEquals("description", draft().copy(description="x".repeat(2001)).problem(now))
        assertEquals("questions", draft((1..31).map { question() }).problem(now)); assertEquals("deadline", draft().copy(deadline=now).problem(now))
        assertEquals("options", draft(listOf(question("singleChoice",(1..17).map { it.toString() }))).problem(now))
        assertEquals("options", draft(listOf(question("singleChoice",listOf("x".repeat(121),"b")))).problem(now))
    }
    @Test fun utf8_request_budget_is_checked_using_actual_encoded_body() {
        val huge = draft((1..30).map { question("multipleChoice", (1..16).map { i -> "$i" + "я".repeat(118) }) }).normalized()
        assertTrue(huge.body().toByteArray().size > 64*1024); assertEquals("size",huge.problem(now))
    }
}
