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
data class GroupMessageUi(val id: String, val author: String, val body: String, val time: String, val mine: Boolean, val kind: String = "text", val deleted: Boolean = false)

internal interface GroupHoldApi {
    suspend fun edit(token: String, conversationId: String, messageId: String, body: String)
    suspend fun delete(token: String, conversationId: String, messageId: String)
    suspend fun react(token: String, conversationId: String, messageId: String, emoji: String)
}

internal data class GroupHoldState(val draft: String = "", val replyTo: String? = null, val editing: String? = null, val removed: Boolean = false)

internal object GroupMedia {
    const val maxBytes = 8 * 1024 * 1024
    suspend fun place(api: ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient, token: String, conversationId: String, kind: String, name: String, bytes: ByteArray, replyTo: String?): ru.bgtu_voenmeh.zapara.data.communities.ChatMessage {
        if (kind != "image" && kind != "video" && kind != "file") throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.InvalidRequest)
        if (bytes.isEmpty() || bytes.size > maxBytes) throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.PayloadTooLarge)
        val clean = name.trim().substringAfterLast('/').substringAfterLast('\\').ifBlank {
            if (kind == "image") "Фото" else if (kind == "video") "Видео" else "Документ"
        }
        return api.sendMedia(token, conversationId, kind, clean, bytes, replyTo)
    }
}

internal object GroupHold {
    suspend fun perform(message: GroupMessageUi, action: String, conversationId: String, token: String, api: GroupHoldApi, state: GroupHoldState): GroupHoldState {
        if (!ru.bgtu_voenmeh.zapara.ui.chat.HoldDecision.actions(message.kind, message.mine, message.deleted, true).contains(action)) return state
        return when (action) {
            "reply" -> state.copy(replyTo = message.id, editing = null, draft = "")
            "edit" -> state.copy(editing = message.id, replyTo = null, draft = message.body)
            "reaction" -> {
                api.react(token, conversationId, message.id, "like")
                state
            }
            "delete" -> {
                api.delete(token, conversationId, message.id)
                state.copy(removed = true)
            }
            else -> state
        }
    }
}

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
    val chatLoading: Boolean = false,
    val replyTo: String? = null,
    val editing: String? = null
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
    data class Hold(val messageId: String, val action: String) : GroupEvent
    data class Media(val kind: String, val name: String, val bytes: ByteArray) : GroupEvent
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
    private var pendingKind: String? = null
    private var pendingName: String = ""
    private var pendingBytes: ByteArray? = null
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
            is GroupEvent.Media -> attach(event.kind, event.name, event.bytes)
            is GroupEvent.Hold -> hold(event.messageId, event.action)
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

    private fun attach(kind: String, name: String, bytes: ByteArray) {
        if (conversationId == null || mutable.value.editing != null) return
        if (bytes.isEmpty() || bytes.size > GroupMedia.maxBytes || (kind != "image" && kind != "video" && kind != "file")) {
            mutable.value = mutable.value.copy(failed = true)
            return
        }
        pendingKind = kind
        pendingName = name
        pendingBytes = bytes
        viewModelScope.launch { send() }
    }

    private suspend fun send() {
        if (sending) return
        val id = conversationId ?: return
        val ticket = generation
        val file = pendingBytes
        val fileKind = pendingKind
        if (file != null && fileKind != null) {
            pendingBytes = null
            pendingKind = null
            val name = pendingName
            pendingName = ""
            sending = true
            try {
                val api = runtime.client
                val token = if (api == null) null else runtime.accessToken()
                if (api == null || token.isNullOrEmpty() || !current(ticket, id)) {
                    if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
                    return
                }
                val saved = GroupMedia.place(api, token, id, fileKind, name, file, mutable.value.replyTo)
                if (!current(ticket, id)) return
                val next = row(saved)
                mutable.value = mutable.value.copy(
                    failed = false,
                    replyTo = null,
                    editing = null,
                    messages = mutable.value.messages.filter { it.id != next.id } + next
                )
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaGroup", "media", e)
                if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
            } finally {
                sending = false
            }
            return
        }
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
            val message = if (mutable.value.editing != null) api.editMessage(token, id, mutable.value.editing!!, body) else api.sendMessage(token, id, body, mutable.value.replyTo)
            if (!current(ticket, id)) {
                if (drafts[id].isNullOrEmpty()) drafts.remove(id)
                return
            }
            val row = row(message)
            if (drafts[id].isNullOrEmpty()) drafts.remove(id)
            if (!current(ticket, id)) return
            val kept = mutable.value.messages.filter { it.id != row.id }
            mutable.value = mutable.value.copy(
                draft = drafts[id].orEmpty(),
                failed = false,
                replyTo = null,
                editing = null,
                messages = kept + row
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
    private fun hold(messageId: String, action: String) {
        val message = mutable.value.messages.find { it.id == messageId } ?: return
        val id = conversationId ?: return
        val api = runtime.client ?: return
        viewModelScope.launch {
            val token = runtime.accessToken()
            if (token.isNullOrEmpty() || conversationId != id) return@launch
            val port = object : GroupHoldApi {
                override suspend fun edit(token: String, conversationId: String, messageId: String, body: String) {
                    api.editMessage(token, conversationId, messageId, body)
                }
                override suspend fun delete(token: String, conversationId: String, messageId: String) {
                    api.deleteMessage(token, conversationId, messageId)
                }
                override suspend fun react(token: String, conversationId: String, messageId: String, emoji: String) {
                    api.reactMessage(token, conversationId, messageId, emoji)
                }
            }
            val next = GroupHold.perform(message, action, id, token, port, GroupHoldState(mutable.value.draft, mutable.value.replyTo, mutable.value.editing))
            if (conversationId != id) return@launch
            val messages = if (next.removed) mutable.value.messages.map { if (it.id == messageId) it.copy(deleted = true) else it } else mutable.value.messages
            mutable.value = mutable.value.copy(draft = next.draft, replyTo = next.replyTo, editing = next.editing, messages = messages)
            conversationId?.let { drafts[it] = next.draft }
        }
    }

    private fun row(item: ChatMessage) = GroupMessageUi(
        item.messageId, item.senderName, item.body, clock.format(item.createdAt), item.senderId == runtime.userId, item.kind, item.deleted
    )

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = GroupViewModel(container) as T
        }
    }
}
