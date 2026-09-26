package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Test
import org.junit.Assert.*
import ru.bgtu_voenmeh.zapara.data.communities.*

class TopicCreationDraftTest {
    private fun state(modern:Boolean,access:Boolean)=GroupUiState(communityId="A",canManageChannels=true,desk=GroupDesk(false,emptyList(),emptyList(),emptyList(),if(access) listOf("access","channels") else listOf("channels")),
        space=if(modern) GroupSpace(emptyList(),emptyList(),GroupCapabilities(templates=listOf("chat")),GroupDesk(false,emptyList(),emptyList(),emptyList(),emptyList())) else null)
    @Test fun default_public_is_omitted_and_custom_is_not_allowed_for_legacy_or_channels_only() {
        val ordinary=TopicCreationDraft("A","owner",title="Topic")
        assertNull(ordinary.initialRules()); assertTrue(ordinary.canSubmit(state(false,false))); assertTrue(ordinary.canSubmit(state(true,false)))
        val private=ordinary.copy(post=GroupAccessPresets.Selection(GroupAccessPresets.Mode.Headman))
        assertFalse(private.canSubmit(state(false,true))); assertFalse(private.canSubmit(state(true,false))); assertTrue(private.canSubmit(state(true,true)))
        assertEquals("deny",private.initialRules()!!.single { it.power=="post" }.state)
    }
    @Test fun successful_ack_clears_only_the_unchanged_submitted_account_community_draft() {
        val drafts=TopicCreationDrafts(); val old=drafts.begin("A","owner").copy(title="Old"); drafts.change(old)
        val other=drafts.begin("B","owner").copy(title="Other"); drafts.change(other)
        drafts.change(old.copy(title="Newer"))
        assertFalse(drafts.acknowledge(old)); assertEquals("Newer",drafts.get("A")!!.title); assertEquals("Other",drafts.get("B")!!.title)
        assertTrue(drafts.acknowledge(drafts.get("A")!!)); assertNull(drafts.get("A")); assertEquals("Other",drafts.get("B")!!.title)
    }
    @Test fun account_change_cannot_reuse_or_acknowledge_previous_owner_draft() {
        val drafts=TopicCreationDrafts();val old=drafts.begin("A","first").copy(title="Private");drafts.change(old)
        val next=drafts.begin("A","second");assertEquals("",next.title);assertEquals("second",next.ownerId)
        assertFalse(drafts.acknowledge(old));assertEquals(next,drafts.get("A"))
    }
}
