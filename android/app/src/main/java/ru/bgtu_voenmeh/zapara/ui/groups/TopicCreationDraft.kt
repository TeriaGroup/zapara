package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.*
import java.util.UUID

data class TopicCreationDraft(
    val communityId:String, val ownerId:String, val draftId:String=UUID.randomUUID().toString(),
    val title:String="",val icon:String="💬",val template:String="chat",val description:String="",val accent:String="default",val pinned:Boolean=false,
    val writePolicy:String="all",val categoryId:String?=null,val position:String="0",val subject:String?=null,
    val read:GroupAccessPresets.Selection=GroupAccessPresets.Selection(GroupAccessPresets.Mode.All),
    val post:GroupAccessPresets.Selection=GroupAccessPresets.Selection(GroupAccessPresets.Mode.All)
) {
    fun snapshot()=copy(read=read.copy(roles=read.roles.toSet()),post=post.copy(roles=post.roles.toSet()))
    fun initialRules():List<GroupAccessRule>? {
        if(read.mode==GroupAccessPresets.Mode.All && post.mode==GroupAccessPresets.Mode.All) return null
        val viewing=GroupAccessPresets.apply(emptyList(),"read",read)
        return GroupAccessPresets.apply(viewing,"post",post).takeIf { it.isNotEmpty() }
    }
    fun canCustomize(state:GroupUiState)=state.space!=null && (state.desk?.headman==true || "access" in state.desk?.mine.orEmpty())
    fun canSubmit(state:GroupUiState):Boolean {
        val knownTemplates=state.space?.capabilities?.templates?.toSet() ?: setOf("chat","polls")
        val roleIds=state.desk?.roles.orEmpty().map { it.roleId }.toSet()
        val selected=read.roles+post.roles
        return state.communityId==communityId && !state.loading && state.preview==null && state.canManageChannels && !state.channelBusy && !state.creationSubmitting && title.trim().length in 2..40 && position.toIntOrNull()!=null && template in knownTemplates &&
            (template!="subject" || !subject.isNullOrBlank()) && selected.all { it in roleIds } && (initialRules()==null || canCustomize(state))
    }
}

internal class TopicCreationDrafts {
    private val values=HashMap<String,TopicCreationDraft>()
    fun get(id:String)=values[id]
    fun begin(id:String,owner:String):TopicCreationDraft {
        val existing=values[id]?.takeIf { it.ownerId==owner }
        return existing ?: TopicCreationDraft(id,owner).also { values[id]=it }
    }
    fun change(draft:TopicCreationDraft) { values[draft.communityId]=draft.snapshot() }
    fun discard(id:String) { values.remove(id) }
    fun acknowledge(submitted:TopicCreationDraft):Boolean {
        if(values[submitted.communityId]!=submitted) return false
        values.remove(submitted.communityId)
        return true
    }
}
