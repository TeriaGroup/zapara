package ru.bgtu_voenmeh.zapara.ui.groups

import java.time.Instant
import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.GroupForm
import ru.bgtu_voenmeh.zapara.data.communities.GroupFormQuestion

class GroupFormAckTest {
    private val time = Instant.parse("2026-10-01T10:00:00Z")
    private fun form(id: String, title: String = id, answered: Boolean = false) = GroupForm(
        formId = id, topicId = "topic", title = title, description = "", deadlineAt = null,
        anonymous = true, questions = listOf(GroupFormQuestion("question", "Question", "shortText", true, emptyList())),
        createdBy = "author", createdAt = time, canRespond = true, canViewResponses = false,
        ownResponse = if (answered) ru.bgtu_voenmeh.zapara.data.communities.GroupFormResponse(
            null, emptyList(), time) else null,
        responseCount = if (answered) 1 else 0
    )

    @Test fun acknowledged_form_replaces_only_its_matching_current_row() {
        val current = listOf(form("a", "Old A"), form("b", "Keep B"))
        val acknowledged = form("a", "Acknowledged A", answered = true)

        val merged = mergeFormSubmitAck(current, acknowledged)

        assertEquals(listOf("Acknowledged A", "Keep B"), merged.map { it.title })
        assertEquals(1, merged.first().responseCount)
        assertEquals(acknowledged.ownResponse, merged.first().ownResponse)
    }

    @Test fun acknowledged_form_is_visible_even_if_it_was_not_in_the_current_page() {
        assertEquals(listOf("new"), mergeFormSubmitAck(emptyList(), form("new", answered = true)).map { it.formId })
    }

    @Test fun newer_refreshed_response_is_not_replaced_by_an_older_post_receipt() {
        val acknowledged = form("a", "Post receipt", answered = true)
        val newer = acknowledged.copy(title = "Fresh form", ownResponse = acknowledged.ownResponse!!.copy(
            updatedAt = time.plusSeconds(60)))
        assertEquals(newer, mergeFormSubmitAck(listOf(newer), acknowledged).single())
    }

    @Test fun refreshed_form_metadata_is_kept_while_the_post_receipt_fills_a_stale_response() {
        val acknowledged = form("a", "Acknowledged title", answered = true)
        val refreshed = form("a", "Newer title").copy(description = "Fresh description")
        val merged = mergeFormRefreshWithAck(listOf(refreshed), acknowledged).single()
        assertEquals("Newer title", merged.title)
        assertEquals("Fresh description", merged.description)
        assertEquals(acknowledged.ownResponse, merged.ownResponse)
    }

    @Test fun successful_refresh_that_no_longer_contains_the_form_does_not_resurrect_it() {
        val acknowledged = form("a", "Acknowledged", answered = true)
        assertEquals(emptyList<GroupForm>(), mergeFormRefreshWithAck(emptyList(), acknowledged))
    }
}
