package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class BallotDraftStoreTest {
    private val a = BallotDraftKey("owner", "community", "topic-a")
    private val b = BallotDraftKey("owner", "community", "topic-b")

    @Test fun channel_roundtrip_restores_each_draft_and_ack_clears_only_submitted_scope() {
        val store = BallotDraftStore()
        val submitted = store.edit(a, question = "Вопрос A", options = listOf("Да", "Нет"), composing = true)
        val other = store.edit(b, question = "Вопрос B", composing = true)
        assertEquals(submitted, store.get(a))
        assertEquals(other, store.get(b))
        assertTrue(store.acknowledge(a, submitted.revision))
        assertFalse(store.get(a).hasContent)
        assertFalse(store.get(a).composing)
        assertEquals(other, store.get(b))
    }

    @Test fun old_ack_cannot_clear_newer_same_topic_or_other_owner_draft() {
        val store = BallotDraftStore()
        val submitted = store.edit(a, question = "Старый A")
        val newer = store.edit(a, question = "Новый A")
        val otherOwner = BallotDraftKey("another-owner", a.communityId, a.topicId)
        store.edit(otherOwner, question = "Чужой A")
        assertFalse(store.acknowledge(a, submitted.revision))
        assertEquals(newer, store.get(a))
        assertEquals("Чужой A", store.get(otherOwner).question)
    }

    @Test fun changing_only_ballot_duration_still_requires_explicit_discard_confirmation() {
        assertFalse(BallotDraft().hasContent)
        assertTrue(BallotDraft(days = 7).hasContent)
        assertTrue(BallotDraft(question = "Вопрос").hasContent)
        assertTrue(BallotDraft(options = listOf("Да", "Нет")).hasContent)
    }

    @Test fun clearing_one_draft_scope_keeps_other_community_and_topic_drafts() {
        val store = BallotDraftStore()
        val otherCommunity = BallotDraftKey("owner", "other-community", "topic-a")
        store.edit(a, question = "A")
        store.edit(b, question = "B")
        store.edit(otherCommunity, question = "Other group")

        store.clear(a)

        assertFalse(store.get(a).hasContent)
        assertEquals("B", store.get(b).question)
        assertEquals("Other group", store.get(otherCommunity).question)
    }
}
