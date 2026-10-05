package ru.bgtu_voenmeh.zapara.ui.groups

import androidx.compose.runtime.saveable.SaverScope
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkAudience

class GroupHomeworkDraftRulesTest {
    private val role = "11111111-1111-4111-8111-111111111111"
    private val person = "22222222-2222-4222-8222-222222222222"

    @Test fun selected_audience_survives_restore_and_partial_restore_fails_closed() {
        val selected = HomeworkAudience("selected", listOf(role), listOf(person))
        val scope = object : SaverScope { override fun canBeSaved(value: Any): Boolean = true }
        val saved = with(GroupHomeworkDraftRules.AudienceSaver) { with(scope) { save(selected) } }!!
        assertEquals(selected, GroupHomeworkDraftRules.AudienceSaver.restore(saved))
        val partial = GroupHomeworkDraftRules.saveAudience(selected).dropLast(1)
        val restored = GroupHomeworkDraftRules.restoreAudience(partial)
        assertEquals("selected", restored.kind)
        assertFalse(restored.valid())
        assertFalse(GroupHomeworkDraftRules.restoreAudience(listOf("all", "1", role, "0")).valid())
        assertEquals(HomeworkAudience(), GroupHomeworkDraftRules.restoreAudience(GroupHomeworkDraftRules.saveAudience(HomeworkAudience())))
    }

    @Test fun editor_scope_changes_with_community_topic_and_existing_task() {
        val a = GroupHomeworkDraftRules.scope("group-a", "topic-a", null)
        assertFalse(a == GroupHomeworkDraftRules.scope("group-b", "topic-a", null))
        assertFalse(a == GroupHomeworkDraftRules.scope("group-a", "topic-b", null))
        assertFalse(a == GroupHomeworkDraftRules.scope("group-a", "topic-a", "task-a"))
    }

    @Test fun retry_keeps_operation_but_confirmed_next_publication_rotates_it() {
        val first = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
        val next = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
        assertEquals(first, GroupHomeworkDraftRules.nextOperation(first, null) { next })
        assertEquals(first, GroupHomeworkDraftRules.nextOperation(first, "some-other-request") { next })
        assertEquals(next, GroupHomeworkDraftRules.nextOperation(first, first) { next })
    }

    @Test fun conflict_reload_adopts_server_audience_while_explicit_keep_preserves_draft() {
        val draft = HomeworkAudience()
        val server = HomeworkAudience("selected", listOf(role), emptyList())
        val conflict = GroupHomeworkDraftRules.conflict(draft, server)
        assertEquals(server, conflict.reloadAudience())
        assertEquals(draft, conflict.keepDraftAudience())
        assertFalse(conflict.sameAudience)
    }
}
