package ru.bgtu_voenmeh.zapara.ui.communities

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.communities.Community
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkCompletion
import ru.bgtu_voenmeh.zapara.data.communities.JoinRequest
import java.time.Instant

internal class CommunitiesRuntime(
    val guest: Boolean,
    val client: CommunityHttpClient?,
    val accessToken: suspend () -> String?,
    val groupId: suspend () -> String?,
    val now: () -> Instant = { Instant.now() }
) {
    companion object {
        fun from(container: AppContainer) = CommunitiesRuntime(
            guest = container.profile.isGuest,
            client = container.communities,
            accessToken = { container.accessToken() },
            groupId = { container.repo.settings().myGroupId }
        )
    }
}

class CommunitiesViewModel internal constructor(private val runtime: CommunitiesRuntime) : ViewModel() {
    constructor(container: AppContainer) : this(CommunitiesRuntime.from(container))

    private val mutable = MutableStateFlow(CommunitiesComposer.compose(CommunitySnapshot(guest = runtime.guest)))
    val state: StateFlow<CommunitiesUiState> = mutable.asStateFlow()
    private var snapshot = CommunitySnapshot(guest = runtime.guest)
    private val pending = LinkedHashMap<String, JoinRequest>()
    private var round = 0

    init {
        viewModelScope.launch { load() }
    }

    fun onEvent(event: CommunitiesEvent) {
        when (event) {
            is CommunitiesEvent.Open -> viewModelScope.launch { open(event.communityId) }
            CommunitiesEvent.Back -> publish(
                snapshot.copy(
                    selectedId = null,
                    members = emptyList(),
                    staff = emptyList(),
                    joinRequests = emptyList(),
                    homework = emptyList(),
                    completions = emptyMap(),
                    announcements = emptyList(),
                    polls = emptyList(),
                    votes = emptyMap(),
                    results = emptyMap()
                )
            )
            is CommunitiesEvent.Join -> viewModelScope.launch { join(event.communityId) }
            is CommunitiesEvent.AcceptJoin -> viewModelScope.launch {
                mutate { api, token ->
                    api.acceptJoin(token, event.communityId, event.requestId)
                    publish(snapshot.copy(joinRequests = snapshot.joinRequests.filterNot { it.requestId == event.requestId }))
                }
            }
            is CommunitiesEvent.RejectJoin -> viewModelScope.launch {
                mutate { api, token ->
                    api.rejectJoin(token, event.communityId, event.requestId)
                    publish(snapshot.copy(joinRequests = snapshot.joinRequests.filterNot { it.requestId == event.requestId }))
                }
            }
            is CommunitiesEvent.ToggleCompletion -> viewModelScope.launch {
                mutate { api, token ->
                    val done = api.upsertCompletion(
                        token, event.communityId, event.homeworkId, event.completed, event.expectedRevision
                    )
                    publish(snapshot.copy(completions = snapshot.completions + (event.homeworkId to done)))
                }
            }
            is CommunitiesEvent.Vote -> viewModelScope.launch { vote(event.communityId, event.pollId, event.optionId) }
            is CommunitiesEvent.ShowResults -> viewModelScope.launch { showResults(event.communityId, event.pollId) }
        }
    }

    private suspend fun load() {
        val ticket = ++round
        if (runtime.guest || runtime.client == null) {
            publish(CommunitySnapshot(guest = true))
            return
        }
        val token = runtime.accessToken()
        if (token.isNullOrEmpty()) {
            publish(CommunitySnapshot(guest = true))
            return
        }
        try {
            val memberships = runtime.client.list(token)
            if (ticket != round) return
            val group = runtime.groupId()
            val catalog = if (group.isNullOrBlank()) emptyList() else runtime.client.list(token, group)
            if (ticket != round) return
            publish(
                CommunitySnapshot(
                    guest = false,
                    communities = merge(memberships, catalog),
                    ownJoin = pending.toMap()
                )
            )
        } catch (e: CancellationException) {
            throw e
        } catch (e: CommunityClientException) {
            if (ticket == round) applyFailure(e)
        } catch (e: Exception) {
            android.util.Log.w("ZaparaCommunities", "load", e)
        }
    }

    private suspend fun open(communityId: String) {
        val community = snapshot.communities.firstOrNull { it.communityId == communityId } ?: return
        if (!isMember(community.role)) return
        publish(snapshot.copy(selectedId = communityId))
        mutate { api, token ->
            val homework = api.listHomework(token, communityId)
            val completions = HashMap<String, HomeworkCompletion>()
            for (item in homework) {
                try {
                    completions[item.homeworkId] = api.getCompletion(token, communityId, item.homeworkId)
                } catch (_: CommunityClientException) {
                }
            }
            val announcements = api.listAnnouncements(token, communityId)
            val polls = api.listPolls(token, communityId)
            val staff = isStaff(community.role)
            publish(
                snapshot.copy(
                    selectedId = communityId,
                    homework = homework,
                    completions = completions,
                    announcements = announcements,
                    polls = polls,
                    members = if (staff) api.listMembers(token, communityId) else emptyList(),
                    staff = if (staff) api.listStaff(token, communityId) else emptyList(),
                    joinRequests = if (staff) api.listJoinRequests(token, communityId) else emptyList()
                )
            )
        }
    }

    private suspend fun join(communityId: String) {
        val api = runtime.client ?: return
        val token = runtime.accessToken()
        if (token.isNullOrEmpty()) {
            publish(CommunitySnapshot(guest = true))
            return
        }
        try {
            val result = api.requestJoin(token, communityId)
            if (result.status == "pending") markPending(result)
        } catch (e: CommunityClientException) {
            when (e.failure) {
                CommunityClientFailure.AlreadyRequested -> markPending(
                    JoinRequest(communityId, communityId, communityId, "pending", runtime.now())
                )
                CommunityClientFailure.AlreadyMember -> {
                    pending.remove(communityId)
                    load()
                }
                else -> applyFailure(e)
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            android.util.Log.w("ZaparaCommunities", "join", e)
        }
    }

    private suspend fun vote(communityId: String, pollId: String, optionId: String) {
        val api = runtime.client ?: return
        val token = runtime.accessToken()
        if (token.isNullOrEmpty()) {
            publish(CommunitySnapshot(guest = true))
            return
        }
        try {
            val vote = api.vote(token, communityId, pollId, optionId)
            publish(snapshot.copy(votes = snapshot.votes + (pollId to vote)))
        } catch (e: CommunityClientException) {
            if (e.failure == CommunityClientFailure.AlreadyVoted || e.failure == CommunityClientFailure.PollClosed) {
                showResults(communityId, pollId)
            } else {
                applyFailure(e)
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            android.util.Log.w("ZaparaCommunities", "vote", e)
        }
    }

    private suspend fun showResults(communityId: String, pollId: String) {
        mutate { api, token ->
            val results = api.results(token, communityId, pollId)
            publish(snapshot.copy(results = snapshot.results + (pollId to results)))
        }
    }

    private suspend fun mutate(action: suspend (CommunityHttpClient, String) -> Unit) {
        val api = runtime.client ?: return
        val token = runtime.accessToken()
        if (token.isNullOrEmpty()) {
            publish(CommunitySnapshot(guest = true))
            return
        }
        try {
            action(api, token)
        } catch (e: CancellationException) {
            throw e
        } catch (e: CommunityClientException) {
            applyFailure(e)
        } catch (e: Exception) {
            android.util.Log.w("ZaparaCommunities", "mutate", e)
        }
    }

    private fun markPending(request: JoinRequest) {
        pending[request.communityId] = request
        publish(snapshot.copy(guest = false, failure = null, ownJoin = pending.toMap()))
    }

    private fun applyFailure(error: CommunityClientException) {
        when (error.failure) {
            CommunityClientFailure.Forbidden -> publish(CommunitySnapshot(guest = false, failure = error.failure))
            CommunityClientFailure.InvalidSession -> publish(CommunitySnapshot(guest = true))
            else -> android.util.Log.w("ZaparaCommunities", error.failure.name)
        }
    }

    private fun publish(next: CommunitySnapshot) {
        snapshot = next.copy(now = runtime.now())
        mutable.value = CommunitiesComposer.compose(snapshot)
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = CommunitiesViewModel(container) as T
        }

        private fun isMember(role: String?) = role == "member" || isStaff(role)
        private fun isStaff(role: String?) = role == "headman" || role == "curator"

        private fun merge(memberships: List<Community>, catalog: List<Community>): List<Community> {
            val rows = memberships.toMutableList()
            val seen = rows.map { it.communityId }.toHashSet()
            for (row in catalog) if (seen.add(row.communityId)) rows.add(row)
            return rows
        }
    }
}
