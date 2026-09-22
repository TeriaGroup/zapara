package ru.bgtu_voenmeh.zapara.data

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class GroupHomeworkTest {
    private val subject = "лек ВЫСШ. МАТЕМАТ"
    private val text = "§5, задачи 1–12"

    private data class Saved(val outcome: HomeworkShareOutcome, val local: List<Pair<String, String>>, val sent: List<Pair<String, String>>)

    private suspend fun save(share: Boolean, signedIn: Boolean, communityId: String, failSend: Boolean = false): Saved {
        val local = mutableListOf<Pair<String, String>>()
        val sent = mutableListOf<Pair<String, String>>()
        val outcome = GroupHomework.saveEditor(HomeworkEditorShare(subject, text, share, isNew = true), signedIn, communityId, { savedSubject, savedText ->
            local += savedSubject to savedText
        }) { savedSubject, savedText ->
            sent += savedSubject to savedText
            if (failSend) throw IllegalStateException("down")
        }
        return Saved(outcome, local, sent)
    }

    @Test fun four_share_starts_keep_the_saved_task_and_send_only_when_allowed() = runBlocking {
        val off = save(share = false, signedIn = true, communityId = "community")
        assertEquals(listOf(subject to text), off.local)
        assertTrue(off.sent.isEmpty())
        assertTrue(off.outcome.stored)
        assertFalse(off.outcome.sent)
        assertEquals("", off.outcome.note)

        val signedOut = save(true, false, "community")
        assertEquals(listOf(subject to text), signedOut.local)
        assertTrue(signedOut.sent.isEmpty())
        assertEquals(GroupHomework.SIGN_IN, signedOut.outcome.note)

        val noCommunity = save(true, true, "  ")
        assertEquals(listOf(subject to text), noCommunity.local)
        assertTrue(noCommunity.sent.isEmpty())
        assertEquals(GroupHomework.LOCAL_ONLY, noCommunity.outcome.note)

        val failed = save(true, true, "community", failSend = true)
        assertEquals(listOf(subject to text), failed.local)
        assertEquals(listOf(subject to text), failed.sent)
        assertTrue(failed.outcome.stored)
        assertFalse(failed.outcome.sent)
        assertEquals(GroupHomework.FAILED, failed.outcome.note)

        val shared = save(true, true, "community")
        assertEquals(listOf(subject to text), shared.local)
        assertEquals(listOf(subject to text), shared.sent)
        assertTrue(shared.outcome.sent)
        assertEquals(GroupHomework.SHARED, shared.outcome.note)

        val edited = GroupHomework.saveEditor(
            HomeworkEditorShare(subject, text, share = true, isNew = false),
            signedIn = true,
            communityId = "community",
            { _, _ -> },
        ) { _, _ -> throw IllegalStateException("edit must not send") }
        assertFalse(edited.sent)
        assertEquals("", edited.note)
    }

    @Test fun one_members_done_does_not_flip_the_other_copy_or_the_local_task() {
        val local = listOf(LocalHomeworkMark("mine", done = false))
        val copies = listOf(
            GroupCopyMark("hw", "anna", completed = false),
            GroupCopyMark("hw", "boris", completed = false),
            GroupCopyMark("other", "anna", completed = true),
        )
        val next = GroupHomework.complete(local, copies, "anna", "hw", true)
        assertFalse(local.single().done)
        assertFalse(copies[0].completed)
        assertFalse(copies[1].completed)
        assertFalse(next.local.single().done)
        assertTrue(next.copies[0].completed)
        assertFalse(next.copies[1].completed)
        assertTrue(next.copies[2].completed)
        assertEquals("mine", next.local.single().id)
    }
}
