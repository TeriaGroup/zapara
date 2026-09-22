package ru.bgtu_voenmeh.zapara.ui.groups

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.communities.ChatMessage
import ru.bgtu_voenmeh.zapara.data.communities.Classmate
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient
import ru.bgtu_voenmeh.zapara.data.communities.Conversation
import ru.bgtu_voenmeh.zapara.data.communities.GroupHome
import java.time.ZoneId
import java.time.format.DateTimeFormatter

internal class GroupRuntime(
    val guest: Boolean,
    val userId: String?,
    val client: CommunityHttpClient?,
    val accessToken: suspend () -> String?,
    val groupName: suspend () -> String?
) {
    companion object {
        fun from(container: AppContainer) = GroupRuntime(
            guest = container.profile.isGuest,
            userId = container.profile.userId,
            client = container.communities,
            accessToken = { container.accessToken() },
            groupName = { container.repo.settings().myGroupId }
        )
    }
}

data class GroupCommunityUi(val id: String, val name: String, val role: String)
data class GroupPersonUi(val id: String, val name: String, val handle: String, val role: String, val self: Boolean)
data class GroupChatUi(val id: String, val title: String, val preview: String, val unread: Int)
data class GroupMessageUi(val id: String, val author: String, val body: String, val time: String, val mine: Boolean)

data class GroupUiState(
    val guest: Boolean = false,
    val loading: Boolean = false,
    val empty: Boolean = false,
    val failed: Boolean = false,
    val title: String = "",
    val myRole: String = "",
    val communities: List<GroupCommunityUi> = emptyList(),
    val people: List<GroupPersonUi> = emptyList(),
    val directs: List<GroupChatUi> = emptyList(),
    val messages: List<GroupMessageUi> = emptyList(),
    val chatTitle: String = "",
    val draft: String = "",
    val hasHome: Boolean = false,
    val showPeople: Boolean = false,
    val direct: Boolean = false,
    val hasMore: Boolean = false,
    val groupUnread: Int = 0,
    val chatLoading: Boolean = false
)

sealed interface GroupEvent {
    data class Open(val communityId: String) : GroupEvent
    data object Back : GroupEvent
    data class Direct(val userId: String) : GroupEvent
    data class OpenChat(val conversationId: String, val title: String) : GroupEvent
    data object GroupChat : GroupEvent
    data object Older : GroupEvent
    data class Draft(val text: String) : GroupEvent
    data object Send : GroupEvent
    data object People : GroupEvent
    data object Chat : GroupEvent
}

class GroupViewModel internal constructor(private val runtime: GroupRuntime) : ViewModel() {
    constructor(container: AppContainer) : this(GroupRuntime.from(container))

    private val mutable = MutableStateFlow(GroupUiState(guest = runtime.guest || runtime.client == null))
    val state: StateFlow<GroupUiState> = mutable.asStateFlow()
    private var home: GroupHome? = null
    private var conversationId: String? = null
    private var poll: Job? = null
    private var generation = 0
    private var sending = false
    private val drafts = HashMap<String, String>()
    private val clock: DateTimeFormatter = DateTimeFormatter.ofPattern("dd.MM HH:mm").withZone(ZoneId.systemDefault())

    init { viewModelScope.launch { load() } }

    fun onEvent(event: GroupEvent) {
        when (event) {
            is GroupEvent.Open -> viewModelScope.launch { open(event.communityId) }
            GroupEvent.Back -> leave()
            is GroupEvent.Direct -> viewModelScope.launch { direct(event.userId) }
            is GroupEvent.OpenChat -> viewModelScope.launch { openChat(event.conversationId, event.title, true) }
            GroupEvent.GroupChat -> home?.let { viewModelScope.launch { openChat(it.groupChat.conversationId, "", false) } }
            GroupEvent.Older -> viewModelScope.launch { older() }
            is GroupEvent.Draft -> {
                val text = event.text.take(2000)
                conversationId?.let { drafts[it] = text }
                mutable.value = mutable.value.copy(draft = text)
            }
            GroupEvent.Send -> viewModelScope.launch { send() }
            GroupEvent.People -> mutable.value = mutable.value.copy(showPeople = true)
            GroupEvent.Chat -> mutable.value = mutable.value.copy(showPeople = false)
        }
    }

    override fun onCleared() {
        poll?.cancel()
    }

    private suspend fun load() {
        val api = runtime.client
        if (runtime.guest || api == null) {
            mutable.value = GroupUiState(guest = true)
            return
        }
        val token = runtime.accessToken()
        if (token.isNullOrEmpty()) {
            mutable.value = GroupUiState(guest = true)
            return
        }
        mutable.value = mutable.value.copy(loading = true, failed = false, guest = false)
        try {
            val rows = api.list(token).filter { it.role != null }
            val wanted = runtime.groupName()
            val preferred = rows.firstOrNull { it.name == wanted || it.name == "Группа $wanted" } ?: rows.singleOrNull()
            mutable.value = mutable.value.copy(
                loading = false,
                empty = rows.isEmpty(),
                communities = rows.map { GroupCommunityUi(it.communityId, it.name, it.role ?: "member") }
            )
            if (preferred != null) open(preferred.communityId)
        } catch (e: CancellationException) {
            throw e
        } catch (_: CommunityClientException) {
            mutable.value = mutable.value.copy(loading = false, failed = true)
        }
    }

    private suspend fun open(communityId: String) {
        val api = runtime.client ?: return
        val token = runtime.accessToken() ?: return
        mutable.value = mutable.value.copy(loading = true, failed = false)
        try {
            val loaded = api.groupHome(token, communityId)
            home = loaded
            publishHome(loaded)
            openChat(loaded.groupChat.conversationId, "", false)
        } catch (e: CancellationException) {
            throw e
        } catch (_: CommunityClientException) {
            mutable.value = mutable.value.copy(loading = false, failed = true)
        }
    }

    private fun publishHome(loaded: GroupHome) {
        val me = loaded.classmates.firstOrNull { it.self }
        mutable.value = mutable.value.copy(
            loading = false,
            hasHome = true,
            empty = false,
            title = loaded.groupName ?: loaded.name,
            myRole = me?.role ?: "member",
            people = loaded.classmates.map { person(it) },
            directs = loaded.directs.map { chat(it) },
            groupUnread = loaded.groupChat.unread,
            failed = false
        )
    }

    private suspend fun direct(userId: String) {
        val loaded = home ?: return
        val person = loaded.classmates.firstOrNull { it.userId == userId && !it.self } ?: return
        val api = runtime.client ?: return
        val token = runtime.accessToken() ?: return
        try {
            val conversation = api.openDirect(token, loaded.communityId, person.userId)
            val fresh = api.groupHome(token, loaded.communityId)
            home = fresh
            publishHome(fresh)
            openChat(conversation.conversationId, person.displayName ?: person.username, true)
            mutable.value = mutable.value.copy(showPeople = false)
        } catch (e: CancellationException) {
            throw e
        } catch (_: CommunityClientException) {
            mutable.value = mutable.value.copy(failed = true)
        }
    }

    private suspend fun openChat(id: String, title: String, direct: Boolean) {
        conversationId?.let { drafts[it] = mutable.value.draft }
        val ticket = ++generation
        poll?.cancel()
        conversationId = id
        mutable.value = mutable.value.copy(
            chatTitle = title, direct = direct, showPeople = false, messages = emptyList(), hasMore = false,
            draft = drafts[id].orEmpty(), chatLoading = true, failed = false
        )
        val api = runtime.client
        if (api == null) {
            if (current(ticket, id)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
            return
        }
        try {
            val token = runtime.accessToken()
            if (token.isNullOrEmpty()) {
                if (current(ticket, id)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
                return
            }
            if (!current(ticket, id)) return
            val page = api.messages(token, id)
            if (!current(ticket, id)) return
            mutable.value = mutable.value.copy(
                messages = page.messages.map { row(it) }, hasMore = page.hasMore, failed = false, chatLoading = false
            )
            try { api.markRead(token, id) } catch (_: CommunityClientException) { }
            if (current(ticket, id)) arm(id)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            android.util.Log.w("ZaparaGroup", "openChat", e)
            if (current(ticket, id)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
        }
    }

    private suspend fun older() {
        val id = conversationId ?: return
        val ticket = generation
        val first = mutable.value.messages.firstOrNull() ?: return
        val api = runtime.client ?: return
        val token = runtime.accessToken() ?: return
        if (!current(ticket, id)) return
        try {
            val page = api.messages(token, id, before = first.id)
            if (!current(ticket, id)) return
            val have = mutable.value.messages.map { it.id }.toSet()
            val older = page.messages.map { row(it) }.filter { it.id !in have }
            mutable.value = mutable.value.copy(messages = older + mutable.value.messages, hasMore = page.hasMore, failed = false)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            android.util.Log.w("ZaparaGroup", "older", e)
            if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
        }
    }

    private suspend fun pull(id: String) {
        val ticket = generation
        if (!current(ticket, id)) return
        val api = runtime.client ?: return
        val token = runtime.accessToken() ?: return
        if (!current(ticket, id)) return
        val last = mutable.value.messages.lastOrNull()
        val page = if (last == null) api.messages(token, id) else api.messages(token, id, after = last.id)
        if (!current(ticket, id)) return
        val have = mutable.value.messages.map { it.id }.toSet()
        val added = page.messages.map { row(it) }.filter { it.id !in have }
        val pageIds = page.messages.map { it.messageId }.toSet()
        val kept = if (last == null) mutable.value.messages.filter { it.id !in pageIds } else mutable.value.messages
        mutable.value = mutable.value.copy(
            messages = if (last == null) page.messages.map { row(it) } + kept else kept + added,
            hasMore = if (last == null) page.hasMore else mutable.value.hasMore,
            failed = false,
            chatLoading = false
        )
    }

    private suspend fun send() {
        if (sending) return
        val id = conversationId ?: return
        val ticket = generation
        val body = mutable.value.draft.trim()
        if (body.isEmpty()) return
        sending = true
        drafts[id] = ""
        if (current(ticket, id)) mutable.value = mutable.value.copy(draft = "")
        try {
            val api = runtime.client
            val token = if (api == null) null else runtime.accessToken()
            if (api == null || token.isNullOrEmpty()) {
                restoreDraft(id, ticket, body)
                return
            }
            val message = api.sendMessage(token, id, body)
            if (!current(ticket, id)) {
                if (drafts[id].isNullOrEmpty()) drafts.remove(id)
                return
            }
            val row = row(message)
            if (drafts[id].isNullOrEmpty()) drafts.remove(id)
            if (!current(ticket, id)) return
            mutable.value = mutable.value.copy(
                draft = drafts[id].orEmpty(),
                failed = false,
                messages = if (mutable.value.messages.any { it.id == row.id }) mutable.value.messages else mutable.value.messages + row
            )
        } catch (e: CancellationException) {
            restoreDraft(id, ticket, body)
            throw e
        } catch (e: Exception) {
            android.util.Log.w("ZaparaGroup", "send", e)
            restoreDraft(id, ticket, body)
        } finally {
            sending = false
        }
    }

    private fun restoreDraft(id: String, ticket: Int, body: String) {
        if (drafts[id].isNullOrEmpty()) drafts[id] = body
        if (!current(ticket, id)) return
        mutable.value = mutable.value.copy(
            failed = true,
            draft = mutable.value.draft.ifEmpty { drafts[id].orEmpty() }
        )
    }

    private fun current(ticket: Int, id: String) = ticket == generation && conversationId == id

    private fun arm(id: String) {
        val ticket = generation
        poll?.cancel()
        poll = viewModelScope.launch {
            while (isActive && current(ticket, id)) {
                delay(4000)
                if (!current(ticket, id)) return@launch
                try {
                    pull(id)
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    android.util.Log.w("ZaparaGroup", "pull", e)
                    if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
                }
            }
        }
    }

    private fun leave() {
        poll?.cancel()
        generation++
        conversationId?.let { drafts[it] = mutable.value.draft }
        conversationId = null
        home = null
        mutable.value = mutable.value.copy(
            hasHome = false, messages = emptyList(), direct = false, failed = false,
            showPeople = false, chatLoading = false, draft = ""
        )
    }

    private fun person(item: Classmate) = GroupPersonUi(item.userId, item.displayName ?: item.username, item.username, item.role, item.self)
    private fun chat(item: Conversation) = GroupChatUi(item.conversationId, item.title, item.lastBody ?: "", item.unread)
    private fun row(item: ChatMessage) = GroupMessageUi(
        item.messageId, item.senderName, item.body, clock.format(item.createdAt), item.senderId == runtime.userId
    )

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = GroupViewModel(container) as T
        }
    }
}
