package ru.bgtu_voenmeh.zapara.ui.communities

data class CommunitiesUiState(
    val pane: CommunityPane = CommunityPane.Guest,
    val communities: List<CommunityListItemUi> = emptyList(),
    val selected: CommunityDetailUi? = null
)

sealed interface CommunitiesEvent {
    data class Open(val communityId: String) : CommunitiesEvent
    data object Back : CommunitiesEvent
    data class Join(val communityId: String) : CommunitiesEvent
    data class AcceptJoin(val communityId: String, val requestId: String) : CommunitiesEvent
    data class RejectJoin(val communityId: String, val requestId: String) : CommunitiesEvent
    data class ToggleCompletion(
        val communityId: String,
        val homeworkId: String,
        val completed: Boolean,
        val expectedRevision: Long
    ) : CommunitiesEvent
    data class Vote(val communityId: String, val pollId: String, val optionId: String) : CommunitiesEvent
    data class ShowResults(val communityId: String, val pollId: String) : CommunitiesEvent
}
