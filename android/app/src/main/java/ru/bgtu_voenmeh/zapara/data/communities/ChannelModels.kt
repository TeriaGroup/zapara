package ru.bgtu_voenmeh.zapara.data.communities

import java.time.Instant

data class GroupTopic(
    val topicId: String?,
    val title: String,
    val icon: String,
    val kind: String,
    val lastBody: String?,
    val lastAuthor: String?,
    val lastAt: Instant?,
    val unread: Int,
    val canDelete: Boolean,
    val activeBallots: Int,
    val description: String = "",
    val accent: String = "default",
    val pinned: Boolean = false,
    val writePolicy: String = "all",
    val canPost: Boolean = true
)

data class GroupTopicList(val topics: List<GroupTopic>, val canManageChannels: Boolean)

data class BallotOption(val optionId: String, val label: String, val votes: Int, val chosen: Boolean)

data class Ballot(
    val ballotId: String,
    val question: String,
    val origin: String,
    val status: String,
    val deadlineAt: Instant,
    val supporters: Int,
    val supportersNeeded: Int,
    val supported: Boolean,
    val options: List<BallotOption>,
    val effect: String,
    val outcome: String,
    val topicId: String?
)

data class BallotBoard(
    val headman: Boolean,
    val canOpen: Boolean,
    val canClose: Boolean,
    val members: Int,
    val supportersNeeded: Int,
    val ballots: List<Ballot>
)
