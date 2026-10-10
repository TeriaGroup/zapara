package ru.bgtu_voenmeh.zapara.ui.communities

import ru.bgtu_voenmeh.zapara.data.communities.Community
import ru.bgtu_voenmeh.zapara.data.communities.CommunityAnnouncement
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHomework
import ru.bgtu_voenmeh.zapara.data.communities.CommunityMember
import ru.bgtu_voenmeh.zapara.data.communities.CommunityPoll
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkCompletion
import ru.bgtu_voenmeh.zapara.data.communities.JoinRequest
import ru.bgtu_voenmeh.zapara.data.communities.PollResults
import ru.bgtu_voenmeh.zapara.data.communities.PollVote
import java.time.Instant

enum class CommunityPane { Guest, Empty, Forbidden, Catalog, Detail }

data class CommunitySnapshot(
    val guest: Boolean,
    val failure: CommunityClientFailure? = null,
    val communities: List<Community> = emptyList(),
    val ownJoin: Map<String, JoinRequest> = emptyMap(),
    val selectedId: String? = null,
    val members: List<CommunityMember> = emptyList(),
    val staff: List<CommunityMember> = emptyList(),
    val joinRequests: List<JoinRequest> = emptyList(),
    /** #104: имена людей по userId — из списка одногруппников (`/home`), если сообщество групповое. */
    val people: Map<String, ru.bgtu_voenmeh.zapara.data.communities.Classmate> = emptyMap(),
    val homework: List<CommunityHomework> = emptyList(),
    val completions: Map<String, HomeworkCompletion> = emptyMap(),
    val announcements: List<CommunityAnnouncement> = emptyList(),
    val polls: List<CommunityPoll> = emptyList(),
    val votes: Map<String, PollVote> = emptyMap(),
    val results: Map<String, PollResults> = emptyMap(),
    val rejectedHomework: List<CommunityHomework> = emptyList(),
    val rejectedAnnouncements: List<CommunityAnnouncement> = emptyList(),
    val rejectedPolls: List<CommunityPoll> = emptyList(),
    val now: Instant = Instant.parse("2026-09-08T12:00:00Z")
)

data class CommunityListItemUi(
    val communityId: String,
    val name: String,
    val description: String,
    val role: String?,
    val joinStatus: String?,
    val canJoin: Boolean,
    val canOpen: Boolean
)

/** [name] — отображаемое имя или логин; null — имени нет, в UI нейтральное «Участник N». [login] — только если отличается от имени. */
data class CommunityPersonUi(val userId: String, val role: String, val name: String? = null, val login: String? = null)

/** #104 / AN-24: подпись человека в сообществе без userId. */
object CommunityPeople {
    data class Label(val name: String?, val login: String?)

    fun label(person: ru.bgtu_voenmeh.zapara.data.communities.Classmate?): Label {
        val display = person?.displayName?.trim().orEmpty()
        val login = person?.username?.trim().orEmpty()
        return when {
            display.isNotEmpty() -> Label(display, login.takeIf { it.isNotEmpty() && !it.equals(display, ignoreCase = true) })
            login.isNotEmpty() -> Label(login, null)
            else -> Label(null, null)
        }
    }

    /**
     * #115 follow-up: номер «Участник №N» без имени — постоянный для человека, а не позиция в списке. Один и тот же
     * человек получает тот же номер в «Участниках», «Модераторах» и «Заявках» и после обновления списка.
     * FNV-1a (32 бита) по userId → 1000…9999; совпадения номеров у разных людей возможны, но редки
     * (номер не идентификатор, решение — имя из сервера, см. PR).
     */
    fun stableNumber(userId: String): Int {
        var hash = 0x811C9DC5.toInt()
        for (b in userId.trim().lowercase().toByteArray(Charsets.UTF_8)) {
            hash = hash xor (b.toInt() and 0xFF)
            hash *= 0x01000193
        }
        return 1000 + ((hash.toLong() and 0xFFFFFFFFL) % 9000).toInt()
    }
}

data class CommunityJoinRequestUi(
    val requestId: String,
    val userId: String,
    val status: String,
    val canResolve: Boolean,
    val name: String? = null,
    val login: String? = null
)

data class CommunityHomeworkUi(
    val homeworkId: String,
    val title: String,
    val body: String,
    val revision: Long,
    val completed: Boolean,
    val completionRevision: Long,
    val rejectedDraft: Boolean,
    val canToggle: Boolean
)

data class CommunityAnnouncementUi(
    val announcementId: String,
    val title: String,
    val body: String,
    val revision: Long,
    val rejectedDraft: Boolean
)

data class CommunityPollOptionUi(
    val optionId: String,
    val label: String,
    val votes: Int? = null
)

data class CommunityPollResultsUi(
    val totalVotes: Int,
    val options: List<CommunityPollOptionUi>
)

data class CommunityPollUi(
    val pollId: String,
    val question: String,
    val deadlineAt: Instant,
    val revision: Long,
    val options: List<CommunityPollOptionUi>,
    val votedOptionId: String?,
    val closed: Boolean,
    val canVote: Boolean,
    val results: CommunityPollResultsUi?,
    val rejectedDraft: Boolean
)

data class CommunityDetailUi(
    val communityId: String,
    val name: String,
    val description: String,
    val role: String,
    val canModerate: Boolean,
    val canPublish: Boolean,
    val members: List<CommunityPersonUi>,
    val staff: List<CommunityPersonUi>,
    val joinRequests: List<CommunityJoinRequestUi>,
    val homework: List<CommunityHomeworkUi>,
    val announcements: List<CommunityAnnouncementUi>,
    val polls: List<CommunityPollUi>
)

object CommunitiesComposer {
    private fun person(userId: String, role: String, snapshot: CommunitySnapshot): CommunityPersonUi =
        CommunityPeople.label(snapshot.people[userId]).let { CommunityPersonUi(userId, role, it.name, it.login) }

    fun compose(snapshot: CommunitySnapshot): CommunitiesUiState {
        if (snapshot.guest) return CommunitiesUiState(CommunityPane.Guest)
        if (snapshot.failure == CommunityClientFailure.Forbidden) {
            return CommunitiesUiState(CommunityPane.Forbidden)
        }
        val failed = snapshot.failure != null
        if (snapshot.communities.isEmpty()) return CommunitiesUiState(CommunityPane.Empty, failed = failed)
        val rows = snapshot.communities.map { community ->
            val join = snapshot.ownJoin[community.communityId]
            val member = isMember(community.role)
            CommunityListItemUi(
                communityId = community.communityId,
                name = community.name,
                description = community.description,
                role = community.role,
                joinStatus = join?.status,
                canJoin = !member && join?.status != "pending" && join?.status != "accepted",
                canOpen = member
            )
        }
        val selected = snapshot.communities.firstOrNull { it.communityId == snapshot.selectedId }
        val role = selected?.role
        if (selected == null || !isMember(role)) {
            return CommunitiesUiState(CommunityPane.Catalog, rows, failed = failed)
        }
        val staff = isStaff(role)
        return CommunitiesUiState(
            pane = CommunityPane.Detail,
            communities = rows,
            failed = failed,
            selected = CommunityDetailUi(
                communityId = selected.communityId,
                name = selected.name,
                description = selected.description,
                role = role!!,
                canModerate = staff,
                canPublish = staff,
                members = snapshot.members.map { person(it.userId, it.role, snapshot) },
                staff = snapshot.staff.map { person(it.userId, it.role, snapshot) },
                joinRequests = if (staff) snapshot.joinRequests.map { request ->
                    val label = CommunityPeople.label(snapshot.people[request.userId])
                    CommunityJoinRequestUi(request.requestId, request.userId, request.status, request.status == "pending",
                        label.name, label.login)
                } else emptyList(),
                homework = mergeHomework(snapshot),
                announcements = mergeAnnouncements(snapshot),
                polls = mergePolls(snapshot, member = true)
            )
        )
    }

    private fun isMember(role: String?) = role == "member" || isStaff(role)

    private fun isStaff(role: String?) = role == "headman" || role == "curator"

    private fun mergeHomework(snapshot: CommunitySnapshot): List<CommunityHomeworkUi> {
        val seen = HashSet<String>()
        return (snapshot.homework.map { it to false } + snapshot.rejectedHomework.map { it to true })
            .mapNotNull { (item, rejected) ->
                if (!seen.add(item.homeworkId)) null
                else {
                    val completion = snapshot.completions[item.homeworkId]
                    CommunityHomeworkUi(
                        homeworkId = item.homeworkId,
                        title = item.title,
                        body = item.body,
                        revision = item.revision,
                        completed = completion?.completed == true,
                        completionRevision = completion?.revision ?: 0,
                        rejectedDraft = rejected,
                        canToggle = !rejected
                    )
                }
            }
    }

    private fun mergeAnnouncements(snapshot: CommunitySnapshot): List<CommunityAnnouncementUi> {
        val seen = HashSet<String>()
        return (snapshot.announcements.map { it to false } + snapshot.rejectedAnnouncements.map { it to true })
            .mapNotNull { (item, rejected) ->
                if (!seen.add(item.announcementId)) null
                else CommunityAnnouncementUi(item.announcementId, item.title, item.body, item.revision, rejected)
            }
    }

    private fun mergePolls(snapshot: CommunitySnapshot, member: Boolean): List<CommunityPollUi> {
        val seen = HashSet<String>()
        return (snapshot.polls.map { it to false } + snapshot.rejectedPolls.map { it to true })
            .mapNotNull { (item, rejected) ->
                if (!seen.add(item.pollId)) null
                else {
                    val closed = !snapshot.now.isBefore(item.deadlineAt)
                    val vote = snapshot.votes[item.pollId]
                    val results = snapshot.results[item.pollId]?.let { tallies ->
                        CommunityPollResultsUi(
                            totalVotes = tallies.totalVotes,
                            options = tallies.options.map { CommunityPollOptionUi(it.optionId, it.label, it.votes) }
                        )
                    }
                    CommunityPollUi(
                        pollId = item.pollId,
                        question = item.question,
                        deadlineAt = item.deadlineAt,
                        revision = item.revision,
                        options = item.options.map { CommunityPollOptionUi(it.optionId, it.label) },
                        votedOptionId = vote?.optionId,
                        closed = closed,
                        canVote = member && !rejected && !closed && vote == null,
                        results = results,
                        rejectedDraft = rejected
                    )
                }
            }
    }
}
