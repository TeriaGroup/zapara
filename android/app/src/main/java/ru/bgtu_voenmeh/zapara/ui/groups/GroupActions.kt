package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic

object GroupActions {
    fun topic(state: GroupUiState, id: String?): GroupTopic? = state.activeArchivedTopic?.takeIf { it.topicId == id }
        ?: (state.preview ?: state.channels).firstOrNull { it.topicId == id }
    fun canManageTopic(state: GroupUiState, topic: GroupTopic) = state.preview==null &&
        (if(state.space==null) state.canManageChannels else "channels" in topic.permissions)
    fun canAccess(state: GroupUiState, topic: GroupTopic) = state.preview == null && state.space != null && "access" in topic.permissions
    fun canPin(state: GroupUiState, topic: GroupTopic) = state.preview == null && state.space != null && "pin" in topic.permissions
    fun canModerate(state: GroupUiState) = !state.direct && state.preview == null && state.space != null &&
        state.channels.firstOrNull { it.topicId == state.activeTopicId }?.permissions?.contains("moderate") == true
}
