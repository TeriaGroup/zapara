package ru.bgtu_voenmeh.zapara.ui.communities

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.collect
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
    val now: () -> Instant = { Instant.now() },
    val ownerId: String = "test",
    val scopeChanges: Flow<*>? = null
) {
    companion object {
        fun from(container: AppContainer) = CommunitiesRuntime(
            guest = container.profile.isGuest,
            client = container.communities,
            accessToken = { container.accessToken() },
            groupId = { container.repo.settings().myGroupId },
            ownerId = container.profile.databaseName,
            scopeChanges = container.events.events
        )
    }
}

class CommunitiesViewModel internal constructor(private val runtime: CommunitiesRuntime) : ViewModel() {
    constructor(container: AppContainer) : this(CommunitiesRuntime.from(container))

    private val mutable = MutableStateFlow(CommunitiesComposer.compose(CommunitySnapshot(guest = runtime.guest)))
    val state: StateFlow<CommunitiesUiState> = mutable.asStateFlow()
    private var snapshot = CommunitySnapshot(guest = runtime.guest)
    private val pending = LinkedHashMap<String, JoinRequest>()
    private val busy = HashSet<String>()
    private var round = 0
    private var openTicket = 0
    private var groupScope: String? = null
    private var scopeReady = false
    private var scopeEpoch = 0L
    private data class ScopeStamp(val group: String?, val epoch: Long)
    private fun scopeStamp() = ScopeStamp(groupScope, scopeEpoch)
    private var requestedDetailId: String? = null
    private var loading = false

    init {
        viewModelScope.launch { load() }
        runtime.scopeChanges?.let { changes -> viewModelScope.launch {
            changes.collect {
                val group = try { runtime.groupId() } catch (e: CancellationException) { throw e }
                catch (_: Exception) { return@collect }
                if (scopeReady && groupScope != group) {
                    ++round; ++openTicket
                    ++scopeEpoch
                    pending.clear(); requestedDetailId = null
                    groupScope = group
                    snapshot = CommunitySnapshot(guest = runtime.guest)
                    publish(snapshot)
                    load()
                }
            }
        } }
    }

    fun onEvent(event: CommunitiesEvent) {
        when (event) {
            is CommunitiesEvent.Open -> viewModelScope.launch { open(event.communityId) }
            CommunitiesEvent.Back -> {
                openTicket++
                requestedDetailId = null
                loading = false
                succeed(
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
            }
            CommunitiesEvent.Retry -> viewModelScope.launch {
                val detailId = requestedDetailId ?: snapshot.selectedId
                if (detailId != null) open(detailId) else load()
            }
            is CommunitiesEvent.Join -> launchOnce("join:${event.communityId}") { join(event.communityId) }
            is CommunitiesEvent.AcceptJoin -> launchOnce("resolve:${event.requestId}") {
                mutate { api, token, scope ->
                    api.acceptJoin(token, event.communityId, event.requestId)
                    if (!isCurrentScope(scope) || snapshot.selectedId != event.communityId) return@mutate
                    succeed(snapshot.copy(joinRequests = snapshot.joinRequests.filterNot { it.requestId == event.requestId }))
                }
            }
            is CommunitiesEvent.RejectJoin -> launchOnce("resolve:${event.requestId}") {
                mutate { api, token, scope ->
                    api.rejectJoin(token, event.communityId, event.requestId)
                    if (!isCurrentScope(scope) || snapshot.selectedId != event.communityId) return@mutate
                    succeed(snapshot.copy(joinRequests = snapshot.joinRequests.filterNot { it.requestId == event.requestId }))
                }
            }
            is CommunitiesEvent.ToggleCompletion -> launchOnce("completion:${event.homeworkId}") {
                mutate { api, token, scope ->
                    val done = api.upsertCompletion(
                        token, event.communityId, event.homeworkId, event.completed, event.expectedRevision
                    )
                    if (!isCurrentScope(scope) || snapshot.selectedId != event.communityId) return@mutate
                    succeed(snapshot.copy(completions = snapshot.completions + (event.homeworkId to done)))
                }
            }
            is CommunitiesEvent.Vote -> launchOnce("vote:${event.pollId}") { vote(event.communityId, event.pollId, event.optionId) }
            is CommunitiesEvent.ShowResults -> viewModelScope.launch { showResults(event.communityId, event.pollId) }
        }
    }

    private suspend fun load() {
        val ticket = ++round
        val group = try { runtime.groupId() } catch (e: CancellationException) { throw e }
        catch (e: Exception) { applyFailure(CommunityClientException(CommunityClientFailure.Transport)); return }
        if (scopeReady && groupScope != group) {
            ++openTicket
            ++scopeEpoch
            pending.clear(); requestedDetailId = null
            snapshot = CommunitySnapshot(guest = runtime.guest)
        }
        groupScope = group
        scopeReady = true
        val scope = scopeStamp()
        loading = true
        publish(snapshot)
        if (runtime.guest || runtime.client == null) {
            loading = false
            publish(CommunitySnapshot(guest = true))
            return
        }
        val token = runtime.accessToken()
        if (token.isNullOrEmpty()) {
            loading = false
            publish(CommunitySnapshot(guest = true))
            return
        }
        try {
            val memberships = runtime.client.list(token)
            if (ticket != round) return
            val catalog = if (group.isNullOrBlank()) emptyList() else runtime.client.list(token, group)
            if (ticket != round) return
            if (!isCurrentScope(scope)) return
            loading = false
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
            if (ticket == round && isCurrentScope(scope)) { loading = false; applyFailure(e) }
        } catch (e: Exception) {
            android.util.Log.w("ZaparaCommunities", "load", e)
            if (ticket == round && isCurrentScope(scope)) {
                loading = false; publish(snapshot.copy(failure = CommunityClientFailure.Transport))
            }
        }
    }

    private suspend fun open(communityId: String) {
        val community = snapshot.communities.firstOrNull { it.communityId == communityId } ?: return
        if (!isMember(community.role)) return
        val ticket = ++openTicket
        requestedDetailId = communityId
        val scope = scopeStamp()
        loading = true
        publish(snapshot)
        val api = runtime.client ?: run { loading = false; publish(snapshot); return }
        val token = runtime.accessToken()
        if (token.isNullOrEmpty()) {
            loading = false
            publish(CommunitySnapshot(guest = true))
            return
        }
        try {
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
            val members = if (staff) api.listMembers(token, communityId) else emptyList()
            val staffRows = if (staff) api.listStaff(token, communityId) else emptyList()
            val requests = if (staff) api.listJoinRequests(token, communityId) else emptyList()
            if (ticket != openTicket) return
            if (!isCurrentScope(scope)) return
            loading = false
            succeed(
                snapshot.copy(
                    selectedId = communityId,
                    homework = homework,
                    completions = completions,
                    announcements = announcements,
                    polls = polls,
                    members = members,
                    staff = staffRows,
                    joinRequests = requests
                )
            )
        } catch (e: CancellationException) {
            throw e
        } catch (e: CommunityClientException) {
            if (ticket == openTicket && isCurrentScope(scope)) { loading = false; applyFailure(e) }
        } catch (e: Exception) {
            android.util.Log.w("ZaparaCommunities", "open", e)
            if (ticket == openTicket && isCurrentScope(scope)) {
                loading = false; publish(snapshot.copy(failure = CommunityClientFailure.Transport))
            }
        }
    }

    private suspend fun join(communityId: String) {
        val scope = scopeStamp()
        val api = runtime.client ?: return
        val token = runtime.accessToken()
        if (token.isNullOrEmpty()) {
            publish(CommunitySnapshot(guest = true))
            return
        }
        try {
            val result = api.requestJoin(token, communityId)
            if (!isCurrentScope(scope)) return
            if (result.status == "pending" || result.status == "accepted") markPending(result)
            if (result.status == "accepted") load()
        } catch (e: CommunityClientException) {
            if (!isCurrentScope(scope)) return
            when (e.failure) {
                CommunityClientFailure.AlreadyRequested -> markPending(
                    JoinRequest(communityId, communityId, communityId, "pending", runtime.now())
                )
                CommunityClientFailure.AlreadyMember -> {
                    markPending(JoinRequest(communityId, communityId, communityId, "accepted", runtime.now()))
                    load()
                }
                else -> applyFailure(e)
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            if (!isCurrentScope(scope)) return
            android.util.Log.w("ZaparaCommunities", "join", e)
            publish(snapshot.copy(failure = CommunityClientFailure.Transport))
        }
    }

    private suspend fun vote(communityId: String, pollId: String, optionId: String) {
        val scope = scopeStamp()
        val api = runtime.client ?: return
        val token = runtime.accessToken()
        if (token.isNullOrEmpty()) {
            publish(CommunitySnapshot(guest = true))
            return
        }
        try {
            val vote = api.vote(token, communityId, pollId, optionId)
            if (!isCurrentScope(scope) || snapshot.selectedId != communityId) return
            succeed(snapshot.copy(votes = snapshot.votes + (pollId to vote)))
        } catch (e: CommunityClientException) {
            if (!isCurrentScope(scope)) return
            if (e.failure == CommunityClientFailure.AlreadyVoted || e.failure == CommunityClientFailure.PollClosed) {
                showResults(communityId, pollId)
            } else {
                applyFailure(e)
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            if (!isCurrentScope(scope)) return
            android.util.Log.w("ZaparaCommunities", "vote", e)
            publish(snapshot.copy(failure = CommunityClientFailure.Transport))
        }
    }

    private suspend fun showResults(communityId: String, pollId: String) {
        mutate { api, token, scope ->
            val results = api.results(token, communityId, pollId)
            if (!isCurrentScope(scope) || snapshot.selectedId != communityId) return@mutate
            succeed(snapshot.copy(results = snapshot.results + (pollId to results)))
        }
    }

    private suspend fun mutate(action: suspend (CommunityHttpClient, String, ScopeStamp) -> Unit) {
        val scope = scopeStamp()
        if (scopeReady && !isCurrentScope(scope)) return
        val api = runtime.client ?: return
        val token = runtime.accessToken()
        if (token.isNullOrEmpty()) {
            publish(CommunitySnapshot(guest = true))
            return
        }
        try {
            action(api, token, scope)
        } catch (e: CancellationException) {
            throw e
        } catch (e: CommunityClientException) {
            if (isCurrentScope(scope)) applyFailure(e)
        } catch (e: Exception) {
            if (!isCurrentScope(scope)) return
            android.util.Log.w("ZaparaCommunities", "mutate", e)
            publish(snapshot.copy(failure = CommunityClientFailure.Transport))
        }
    }

    private fun markPending(request: JoinRequest) {
        pending[request.communityId] = request
        publish(snapshot.copy(guest = false, failure = null, ownJoin = pending.toMap()))
    }

    private fun launchOnce(key: String, block: suspend () -> Unit) {
        if (!busy.add(key)) return
        publish(snapshot)
        viewModelScope.launch {
            try {
                block()
            } finally {
                busy.remove(key)
                publish(snapshot)
            }
        }
    }

    private suspend fun isCurrentScope(expected: ScopeStamp): Boolean {
        if (scopeEpoch != expected.epoch || groupScope != expected.group) return false
        if (runtime.groupId() == expected.group) return true
        load()
        return false
    }

    private fun succeed(next: CommunitySnapshot) = publish(next.copy(failure = null))

    private fun applyFailure(error: CommunityClientException) {
        when (error.failure) {
            CommunityClientFailure.Forbidden -> publish(CommunitySnapshot(guest = false, failure = error.failure))
            CommunityClientFailure.InvalidSession -> publish(CommunitySnapshot(guest = true))
            else -> publish(snapshot.copy(failure = error.failure))
        }
    }

    private fun publish(next: CommunitySnapshot) {
        snapshot = next.copy(now = runtime.now())
        mutable.value = CommunitiesComposer.compose(snapshot).copy(
            loading = loading,
            joining = busy.filter { it.startsWith("join:") }.map { it.removePrefix("join:") }.toSet(),
            resolving = busy.filter { it.startsWith("resolve:") }.map { it.removePrefix("resolve:") }.toSet()
        )
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
