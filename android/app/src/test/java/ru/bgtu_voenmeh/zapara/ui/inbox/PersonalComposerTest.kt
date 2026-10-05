package ru.bgtu_voenmeh.zapara.ui.inbox

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import java.time.Instant

class PersonalComposerTest {
    private fun message(id: String, body: String = "old text") = SocialMessage(id, "me", "Me", body,
        "text", Instant.EPOCH, null, null, false, false, false, emptyList(), null, null)

    @Test fun `reply preserves text and cancel only clears reply`() {
        val draft = PersonalComposer().type("unfinished\ntext").replyTo(message("reply"))
        assertEquals("unfinished\ntext", draft.text)
        assertEquals("unfinished\ntext", draft.cancel().text)
        assertNull(draft.cancel().reply)
    }

    @Test fun `cancel and successful edit restore ordinary draft with reply`() {
        val draft = PersonalComposer().type("ordinary").replyTo(message("reply"))
        val editing = draft.edit(message("edit")).type("edited")
        for (restored in listOf(editing.cancel(), editing.acknowledge(editing))) {
            assertEquals("ordinary", restored.text)
            assertEquals("reply", restored.reply?.id)
            assertNull(restored.editing)
        }
    }

    @Test fun `switching edit targets retains original draft`() {
        val edited = PersonalComposer().type("ordinary").edit(message("one")).type("one changed").edit(message("two"))
        assertEquals("ordinary", edited.cancel().text)
    }

    @Test fun `reply while editing restores ordinary text`() {
        val draft = PersonalComposer().type("ordinary").edit(message("edit")).type("edited").replyTo(message("reply"))
        assertEquals("ordinary", draft.text)
        assertEquals("reply", draft.reply?.id)
        assertNull(draft.editing)
    }

    @Test fun `late acknowledgement cannot clear changed then restored text or compose mode`() {
        val sent = PersonalComposer().type("message")
        val next = sent.type("new").type("message")
        assertEquals(next, next.acknowledge(sent))
        val reply = sent.replyTo(message("reply")).cancel()
        assertEquals(reply, reply.acknowledge(sent))
        val spaces = sent.type("message ")
        assertEquals(spaces, spaces.acknowledge(sent))
    }

    @Test fun `late successful edit leaves newer edit draft intact`() {
        val sent = PersonalComposer().type("ordinary").edit(message("edit")).type("saved")
        val newer = sent.type("still typing")
        assertEquals(newer, newer.acknowledge(sent))
    }

    @Test fun `accepted unchanged send clears compose context`() {
        val sent = PersonalComposer().type(" message ").replyTo(message("reply"))
        assertEquals("", sent.acknowledge(sent).text)
        assertNull(sent.acknowledge(sent).reply)
    }

    @Test fun `pasted text remains intact and server limit uses normalized text`() {
        val pasted = "  " + "x".repeat(1998) + "\r\ny  "
        val draft = PersonalComposer().type(pasted)
        assertEquals(pasted, draft.text)
        assertEquals(2000, draft.messageLength)
        assertTrue(draft.canSend)
        val tooLong = draft.type("x".repeat(4001))
        assertEquals(4001, tooLong.text.length)
        assertFalse(tooLong.canSend)
        assertFalse(draft.type(" \r\n ").canSend)
        assertFalse(draft.type("bad\u0000text").canSend)
    }

    @Test fun `conversation sessions retain modes without crossing owners`() {
        val sessions = PersonalComposerSessions()
        val draft = PersonalComposer().type("private").replyTo(message("reply")).edit(message("edit"))
        sessions.save("a", draft)
        sessions.save("b", PersonalComposer().type("other"))
        assertEquals(draft, sessions.restore("a"))
        assertEquals("other", sessions.restore("b").text)
        assertEquals("", PersonalComposerSessions().restore("a").text)
        sessions.clear()
        assertEquals("", sessions.restore("a").text)
    }

    @Test fun `counter and limit count Unicode characters like the server`() {
        val draft = PersonalComposer().type("😀".repeat(2000))
        assertEquals(2000, draft.messageLength)
        assertTrue(draft.canSend)
        assertFalse(draft.type(draft.text + "😀").canSend)
    }

    @Test fun `history read begun before mutation cannot replace acknowledged edit`() {
        val versions = PersonalHistoryVersions()
        val beforeEdit = versions.current("chat")
        versions.acknowledge("chat")
        assertFalse(versions.isCurrent("chat", beforeEdit))
        val afterEdit = versions.current("chat")
        assertTrue(versions.isCurrent("chat", afterEdit))
        versions.acknowledge("chat")
        assertFalse(versions.isCurrent("chat", afterEdit))
    }

    @Test fun `acknowledgement leaves other conversation history reads valid`() {
        val versions = PersonalHistoryVersions()
        val otherRead = versions.current("other")
        versions.acknowledge("chat")
        assertTrue(versions.isCurrent("other", otherRead))
    }
}
