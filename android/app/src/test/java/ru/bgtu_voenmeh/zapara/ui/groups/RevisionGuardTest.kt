package ru.bgtu_voenmeh.zapara.ui.groups
import org.junit.Test
import org.junit.Assert.*
import ru.bgtu_voenmeh.zapara.ui.components.RevisionGuard
class RevisionGuardTest {
    @Test fun foreign_polling_does_not_advance_expected_write_revision() {
        val draft = RevisionGuard(1).observed(2)
        assertEquals(1, draft.base); assertTrue(draft.conflict)
        assertEquals(2, draft.reload().base)
    }
    @Test fun confirmed_own_save_rebases_next_save_without_an_extra_conflict() {
        val saved = RevisionGuard(1).observed(2).confirmed(2)
        assertFalse(saved.conflict); assertEquals(2, saved.base)
        val nextForeign = saved.observed(3)
        assertEquals(2, nextForeign.base); assertTrue(nextForeign.conflict)
    }
    @Test fun delegated_access_pin_and_moderate_do_not_require_channel_management() {
        val topic = ru.bgtu_voenmeh.zapara.data.communities.GroupTopic("topic", "Topic", "", "chat", null, null, null, 0, false, 0, permissions = listOf("read","access","pin","moderate"))
        val desk = ru.bgtu_voenmeh.zapara.data.communities.GroupDesk(false, emptyList(), emptyList(), emptyList(), listOf("access","pin","moderate"))
        val state = GroupUiState(canManageChannels=false, activeTopicId="topic", channels=listOf(topic), desk=desk, space=ru.bgtu_voenmeh.zapara.data.communities.GroupSpace(listOf(topic),emptyList(),ru.bgtu_voenmeh.zapara.data.communities.GroupCapabilities(),desk))
        assertTrue(GroupActions.canAccess(state,topic)); assertTrue(GroupActions.canPin(state,topic)); assertTrue(GroupActions.canModerate(state))
        assertTrue("delete" in ru.bgtu_voenmeh.zapara.ui.chat.HoldDecision.actions("text",false,false,true,GroupActions.canModerate(state)))
        assertFalse(GroupActions.canPin(state.copy(preview=listOf(topic)),topic))
    }

    @Test fun pinned_topics_are_kept_inside_their_category() {
        fun row(id:String,cat:String,pinned:Boolean)=ru.bgtu_voenmeh.zapara.data.communities.GroupTopic(id,id,"","chat",null,null,null,0,false,0,pinned=pinned,categoryId=cat)
        val rows=listOf(row("a-normal","A",false),row("b-pin","B",true),row("b-normal","B",false),row("a-pin","A",true))
        assertEquals(listOf("a-pin","a-normal","b-pin","b-normal"),browseChannels(rows,categoryOrder=mapOf("A" to 0,"B" to 1)).map { it.topicId })
    }

    @Test fun search_expands_matching_collapsed_categories_without_changing_preference() {
        val collapsed=listOf("A")
        assertFalse(categoryExpanded("A",collapsed,false)); assertTrue(categoryExpanded("A",collapsed,true))
        assertTrue(categoryExpanded(null,collapsed,false)); assertEquals(listOf("A"),collapsed)
    }

    @Test fun preview_topic_uses_target_permissions_instead_of_operator_permissions() {
        val operator=ru.bgtu_voenmeh.zapara.data.communities.GroupTopic("t","Topic","","chat",null,null,null,0,false,0,permissions=listOf("read","post","access"))
        val target=operator.copy(permissions=listOf("read"))
        assertEquals(listOf("read"),GroupActions.topic(GroupUiState(activeTopicId="t",channels=listOf(operator),preview=listOf(target)),"t")!!.permissions)
    }

    @Test fun subject_next_lesson_skips_the_groups_closer_different_subject() {
        val date=java.time.LocalDate.of(2026,9,28)
        val physics=ru.bgtu_voenmeh.zapara.data.Lesson(dayOfWeek=1,timeStart="08:00",timeEnd="09:35",subjectRaw="Physics")
        val math=physics.copy(timeStart="12:00",timeEnd="13:35",subjectRaw="Math")
        assertEquals("Math",GroupSubjectLogic.nextLesson("Group","Math",date.atTime(7,0)) { listOf(physics,math) }!!.subject)
    }
    @Test fun own_ack_does_not_hide_a_later_foreign_revision() {
        val value=RevisionGuard(1).observed(3).acknowledgedOwn(2)
        assertEquals(2,value.base); assertEquals(3,value.latest); assertTrue(value.conflict)
    }

}
