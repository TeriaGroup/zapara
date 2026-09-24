package ru.bgtu_voenmeh.zapara.ui.groups

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.communities.ChatMessage
import ru.bgtu_voenmeh.zapara.data.communities.ChatReaction
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
    val groupName: suspend () -> String?,
    val openMedia: suspend (GroupMessageUi, ByteArray) -> Boolean = { _, _ -> false },
    val initialCommunityId: String? = null,
    val initialConversationId: String? = null,
    val mediaCacheDir: File? = null
) {
    companion object {
        fun from(container: AppContainer, initialCommunityId: String? = null, initialConversationId: String? = null) = GroupRuntime(
            guest = container.profile.isGuest,
            userId = container.profile.userId,
            client = container.communities,
            accessToken = { withContext(Dispatchers.IO) { container.accessToken() } },
            groupName = { readGroupNameOffMain { container.repo.settings().myGroupId } },
            openMedia = { message, bytes -> GroupMediaViewer(container.app).open(message, bytes) },
            initialCommunityId = initialCommunityId,
            initialConversationId = initialConversationId,
            mediaCacheDir = container.app.cacheDir
        )
    }
}

internal suspend fun readGroupNameOffMain(read: () -> String?): String? = withContext(Dispatchers.IO) { read() }

data class GroupCommunityUi(val id: String, val name: String, val role: String)
data class GroupPersonUi(val id: String, val name: String, val handle: String, val role: String, val self: Boolean)
data class GroupChatUi(val id: String, val title: String, val preview: String, val unread: Int)
data class GroupMessageUi(val id: String, val author: String, val body: String, val time: String, val mine: Boolean,
    val kind: String = "text", val deleted: Boolean = false, val replyTo: String? = null,
    val reactions: List<ChatReaction> = emptyList())

internal interface GroupHoldApi {
    suspend fun edit(token: String, conversationId: String, messageId: String, body: String)
    suspend fun delete(token: String, conversationId: String, messageId: String)
    suspend fun react(token: String, conversationId: String, messageId: String, emoji: String)
}

internal data class GroupHoldState(val draft: String = "", val replyTo: String? = null, val editing: String? = null, val removed: Boolean = false)

internal object GroupMedia {
    const val maxBytes = 8 * 1024 * 1024
    fun extension(kind: String, bytes: ByteArray): String = when {
        kind == "image" -> ".img"
        kind == "voice" && bytes.size >= 4 && bytes.take(4) == listOf(0x1A, 0x45, 0xDF, 0xA3).map(Int::toByte) -> ".webm"
        kind == "voice" && bytes.size >= 4 && bytes.take(4) == "OggS".toByteArray().toList() -> ".ogg"
        kind == "voice" && bytes.size >= 8 && String(bytes, 4, 4, Charsets.US_ASCII) == "ftyp" -> ".m4a"
        kind == "voice" -> ".mp3"
        kind == "circle" && bytes.size >= 4 && bytes.take(4) == listOf(0x1A, 0x45, 0xDF, 0xA3).map(Int::toByte) -> ".webm"
        else -> ".mp4"
    }
    suspend fun place(api: ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient, token: String, conversationId: String, kind: String, name: String, bytes: ByteArray, replyTo: String?, durationMs: Int? = null): ru.bgtu_voenmeh.zapara.data.communities.ChatMessage {
        if (kind !in setOf("image", "video", "file", "voice", "circle")) throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.InvalidRequest)
        if (bytes.isEmpty() || bytes.size > (if (kind == "voice") 2 * 1024 * 1024 else maxBytes)) throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.PayloadTooLarge)
        if (kind in setOf("voice", "circle") && durationMs == null) throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.InvalidRequest)
        val clean = name.trim().substringAfterLast('/').substringAfterLast('\\').ifBlank {
            when (kind) { "image" -> "Фото"; "video" -> "Видео"; "voice" -> "voice.m4a"; "circle" -> "circle.mp4"; else -> "Документ" }
        }
        return api.sendMedia(token, conversationId, kind, clean, bytes, replyTo, durationMs)
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
    val activeConversationId: String? = null,
    val draft: String = "",
    val hasHome: Boolean = false,
    val showPeople: Boolean = false,
    val direct: Boolean = false,
    val hasMore: Boolean = false,
    val groupUnread: Int = 0,
    val chatLoading: Boolean = false,
    val sending: Boolean = false,
    val attachmentPending: Boolean = false,
    val mediaLoadingId: String? = null,
    val mediaError: Boolean = false,
    val mediaFiles: Map<String, File> = emptyMap(),
    val mediaLoadingIds: Set<String> = emptySet(),
    val mediaFailedIds: Set<String> = emptySet(),
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
    data class React(val messageId: String, val emoji: String) : GroupEvent
    data class Media(val kind: String, val name: String, val bytes: ByteArray, val conversationId: String? = null) : GroupEvent
    data class Recorded(val kind: String, val file: File, val durationMs: Int, val conversationId: String?) : GroupEvent
    data class LoadMedia(val messageId: String) : GroupEvent
    data object MediaError : GroupEvent
    data class OpenMedia(val messageId: String) : GroupEvent
    data object People : GroupEvent
    data object Chat : GroupEvent
}

class GroupViewModel internal constructor(private val runtime: GroupRuntime) : ViewModel() {
    constructor(container: AppContainer, initialCommunityId: String? = null, initialConversationId: String? = null) :
        this(GroupRuntime.from(container, initialCommunityId, initialConversationId))

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
    private var pendingDurationMs: Int? = null
    private val cachedMedia = HashMap<String, File>()
    private val mediaCacheRoot = runtime.mediaCacheDir?.let { File(it, "group-chat-${java.util.UUID.randomUUID()}") }
    private val mediaJobs = HashMap<String, Job>()
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
                if (mutable.value.editing == null && mutable.value.replyTo == null) {
                    conversationId?.let { drafts[it] = text }
                }
                mutable.value = mutable.value.copy(draft = text)
            }
            GroupEvent.Send -> viewModelScope.launch { send() }
            is GroupEvent.Media -> if (event.conversationId == null || event.conversationId == conversationId)
                attach(event.kind, event.name, event.bytes)
            is GroupEvent.Recorded -> viewModelScope.launch { attachRecorded(event) }
            is GroupEvent.LoadMedia -> loadMedia(event.messageId)
            GroupEvent.MediaError -> mutable.value = mutable.value.copy(mediaError = true)
            is GroupEvent.OpenMedia -> openMedia(event.messageId)
            is GroupEvent.Hold -> hold(event.messageId, event.action)
            is GroupEvent.React -> react(event.messageId, event.emoji)
            GroupEvent.People -> mutable.value = mutable.value.copy(showPeople = true)
            GroupEvent.Chat -> mutable.value = mutable.value.copy(showPeople = false)
        }
    }

    override fun onCleared() {
        poll?.cancel()
        mediaJobs.values.forEach { it.cancel() }
        runCatching { mediaCacheRoot?.deleteRecursively() }
    }

    private suspend fun load() {
        val api = runtime.client
        if (runtime.guest || api == null) {
            mutable.value = GroupUiState(guest = true)
            return
        }
        mutable.value = mutable.value.copy(loading = true, failed = false, guest = false)
        try {
            val token = runtime.accessToken()
            if (token.isNullOrEmpty()) {
                mutable.value = GroupUiState(guest = true)
                return
            }
            val rows = api.list(token).filter { it.role != null }
            val wanted = runtime.groupName()
            val preferred = rows.firstOrNull { it.communityId == runtime.initialCommunityId }
                ?: rows.firstOrNull { it.name == wanted || it.name == "Группа $wanted" } ?: rows.singleOrNull()
            mutable.value = mutable.value.copy(
                loading = false,
                empty = rows.isEmpty(),
                communities = rows.map { GroupCommunityUi(it.communityId, it.name, it.role ?: "member") }
            )
            if (preferred != null) open(preferred.communityId)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            mutable.value = mutable.value.copy(loading = false, failed = true)
            runCatching { android.util.Log.w("ZaparaGroup", "load", e) }
        }
    }

    private suspend fun open(communityId: String) {
        val api = runtime.client ?: return
        mutable.value = mutable.value.copy(loading = true, failed = false)
        try {
            val token = runtime.accessToken()
            if (token.isNullOrEmpty()) {
                mutable.value = mutable.value.copy(loading = false, failed = true)
                return
            }
            val loaded = api.groupHome(token, communityId)
            home = loaded
            publishHome(loaded)
            val requested = if (communityId == runtime.initialCommunityId)
                loaded.directs.firstOrNull { it.conversationId == runtime.initialConversationId } else null
            if (requested == null) openChat(loaded.groupChat.conversationId, "", false)
            else openChat(requested.conversationId, requested.title, true)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            mutable.value = mutable.value.copy(loading = false, failed = true)
            runCatching { android.util.Log.w("ZaparaGroup", "open", e) }
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
        if (mutable.value.editing == null && mutable.value.replyTo == null) {
            conversationId?.let { drafts[it] = mutable.value.draft }
        }
        val ticket = ++generation
        poll?.cancel()
        mediaJobs.values.forEach { it.cancel() }
        mediaJobs.clear()
        conversationId = id
        pendingBytes = null
        pendingKind = null
        pendingName = ""
        pendingDurationMs = null
        mutable.value = mutable.value.copy(
            chatTitle = title, activeConversationId = id, direct = direct, showPeople = false, messages = emptyList(), hasMore = false,
            draft = drafts[id].orEmpty(), replyTo = null, editing = null, chatLoading = true, failed = false,
            attachmentPending = false,
            mediaLoadingId = null, mediaError = false,
            mediaFiles = emptyMap(), mediaLoadingIds = emptySet(), mediaFailedIds = emptySet()
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
            acknowledge(token, id, ticket)
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
        val recent = if (last == null) page.messages else try {
            api.messages(token, id).messages
        } catch (e: CancellationException) {
            throw e
        } catch (_: CommunityClientException) {
            emptyList()
        }
        if (!current(ticket, id)) return
        val have = mutable.value.messages.map { it.id }.toSet()
        val added = page.messages.map { row(it) }.filter { it.id !in have }
        val pageIds = page.messages.map { it.messageId }.toSet()
        val kept = if (last == null) mutable.value.messages.filter { it.id !in pageIds } else mutable.value.messages
        val recentById = recent.associateBy { it.messageId }
        val merged = (if (last == null) page.messages.map { row(it) } + kept else kept + added).map { existing ->
            recentById[existing.id]?.let { row(it) } ?: existing
        }
        mutable.value = mutable.value.copy(
            messages = merged,
            hasMore = if (last == null) page.hasMore else mutable.value.hasMore,
            failed = false,
            chatLoading = false
        )
        if (page.messages.isNotEmpty()) acknowledge(token, id, ticket)
    }

    private suspend fun acknowledge(token: String, id: String, ticket: Int) {
        val api = runtime.client ?: return
        try {
            val read = api.markRead(token, id)
            if (!current(ticket, id)) return
            mutable.value = if (read.kind == "group") {
                mutable.value.copy(groupUnread = read.unread)
            } else {
                mutable.value.copy(directs = mutable.value.directs.map { if (it.id == id) it.copy(unread = read.unread) else it })
            }
        } catch (e: CancellationException) {
            throw e
        } catch (_: CommunityClientException) { }
    }

    private fun attach(kind: String, name: String, bytes: ByteArray, durationMs: Int? = null) {
        if (conversationId == null || mutable.value.editing != null) return
        if (sending || pendingBytes != null) {
            mutable.value = mutable.value.copy(failed = true)
            return
        }
        val maxBytes = if (kind == "voice") 2 * 1024 * 1024 else GroupMedia.maxBytes
        val maxDuration = if (kind == "voice") 180_000 else 60_000
        if (bytes.isEmpty() || bytes.size > maxBytes || kind !in setOf("image", "video", "file", "voice", "circle") ||
            (durationMs != null && (kind !in setOf("voice", "circle") || durationMs !in 1..maxDuration))) {
            mutable.value = mutable.value.copy(failed = true)
            return
        }
        pendingKind = kind
        pendingName = name
        pendingBytes = bytes
        pendingDurationMs = durationMs
        mutable.value = mutable.value.copy(attachmentPending = true)
        viewModelScope.launch { send() }
    }

    private suspend fun attachRecorded(event: GroupEvent.Recorded) {
        try {
            val id = event.conversationId ?: return
            val ticket = generation
            if (!current(ticket, id)) return
            val bytes = withContext(Dispatchers.IO) {
                val limit = if (event.kind == "voice") 2L * 1024 * 1024 else 8L * 1024 * 1024
                if (!event.file.isFile || event.file.length() !in 1L..limit) error("recording_unavailable")
                event.file.readBytes()
            }
            if (!current(ticket, id)) return
            attach(event.kind, event.file.name, bytes, event.durationMs)
        } catch (e: CancellationException) {
            throw e
        } catch (_: Exception) {
            mutable.value = mutable.value.copy(mediaError = true)
        } finally {
            runCatching { event.file.delete() }
        }
    }

    private fun loadMedia(messageId: String) {
        val id = conversationId ?: return
        val ticket = generation
        val message = mutable.value.messages.firstOrNull { it.id == messageId } ?: return
        if (message.deleted || message.kind !in setOf("image", "voice", "circle")) return
        val existing = cachedMedia[messageId]
        if (existing?.isFile == true) {
            if (mutable.value.mediaFiles[messageId] != existing)
                mutable.value = mutable.value.copy(mediaFiles = mutable.value.mediaFiles + (messageId to existing))
            return
        }
        if (mediaJobs[messageId]?.isActive == true) return
        val api = runtime.client ?: return
        val root = mediaCacheRoot ?: return
        mediaJobs[messageId] = viewModelScope.launch {
            mutable.value = mutable.value.copy(mediaLoadingIds = mutable.value.mediaLoadingIds + messageId,
                mediaFailedIds = mutable.value.mediaFailedIds - messageId)
            try {
                val token = runtime.accessToken() ?: error("Сессия недоступна")
                val bytes = withContext(Dispatchers.IO) { api.downloadMedia(token, id, messageId) }
                if (!current(ticket, id)) return@launch
                val file = withContext(Dispatchers.IO) {
                    root.mkdirs()
                    val extension = GroupMedia.extension(message.kind, bytes)
                    File(root, messageId + extension).also { it.writeBytes(bytes) }
                }
                if (!current(ticket, id)) { file.delete(); return@launch }
                cachedMedia[messageId] = file
                mutable.value = mutable.value.copy(mediaFiles = mutable.value.mediaFiles + (messageId to file))
            } catch (e: CancellationException) {
                throw e
            } catch (_: Exception) {
                if (current(ticket, id)) mutable.value = mutable.value.copy(mediaFailedIds = mutable.value.mediaFailedIds + messageId)
            } finally {
                if (current(ticket, id)) mediaJobs.remove(messageId)
                if (current(ticket, id)) mutable.value = mutable.value.copy(mediaLoadingIds = mutable.value.mediaLoadingIds - messageId)
            }
        }
    }

    private fun openMedia(messageId: String) {
        val id = conversationId ?: return
        val message = mutable.value.messages.firstOrNull { it.id == messageId } ?: return
        if (message.deleted || message.kind !in setOf("image", "video", "file") || mutable.value.mediaLoadingId != null) return
        val api = runtime.client ?: return
        val ticket = generation
        mutable.value = mutable.value.copy(mediaLoadingId = messageId, mediaError = false)
        viewModelScope.launch {
            try {
                val token = runtime.accessToken()
                if (token.isNullOrEmpty()) {
                    if (current(ticket, id)) mutable.value = mutable.value.copy(mediaError = true)
                    return@launch
                }
                if (!current(ticket, id)) return@launch
                val bytes = api.downloadMedia(token, id, messageId)
                if (!current(ticket, id)) return@launch
                val opened = runtime.openMedia(message, bytes)
                if (current(ticket, id)) mutable.value = mutable.value.copy(mediaError = !opened)
            } catch (e: CancellationException) {
                throw e
            } catch (_: Exception) {
                if (current(ticket, id)) mutable.value = mutable.value.copy(mediaError = true)
            } finally {
                if (current(ticket, id)) mutable.value = mutable.value.copy(mediaLoadingId = null)
            }
        }
    }

    private suspend fun send() {
        if (sending) return
        val id = conversationId ?: return
        val ticket = generation
        val file = pendingBytes
        val fileKind = pendingKind
        if (file != null && fileKind != null) {
            var delivered = false
            pendingBytes = null
            pendingKind = null
            val name = pendingName
            pendingName = ""
            val durationMs = pendingDurationMs
            pendingDurationMs = null
            sending = true
            mutable.value = mutable.value.copy(sending = true)
            try {
                val api = runtime.client
                val token = if (api == null) null else runtime.accessToken()
                if (api == null || token.isNullOrEmpty() || !current(ticket, id)) {
                    if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
                    return
                }
                val saved = GroupMedia.place(api, token, id, fileKind, name, file, mutable.value.replyTo, durationMs)
                delivered = true
                if (!current(ticket, id)) return
                val next = row(saved)
                mutable.value = mutable.value.copy(
                    failed = false,
                    attachmentPending = false,
                    replyTo = null,
                    editing = null,
                    messages = mutable.value.messages.filter { it.id != next.id } + next
                )
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
                runCatching { android.util.Log.w("ZaparaGroup", "media", e) }
            } finally {
                if (!delivered && current(ticket, id) && pendingBytes == null) {
                    pendingBytes = file
                    pendingKind = fileKind
                    pendingName = name
                    pendingDurationMs = durationMs
                    mutable.value = mutable.value.copy(attachmentPending = true)
                }
                sending = false
                mutable.value = mutable.value.copy(sending = false)
            }
            return
        }
        val body = mutable.value.draft.trim()
        if (body.isEmpty()) return
        val editing = mutable.value.editing
        val replyTo = mutable.value.replyTo
        val contextual = editing != null || replyTo != null
        sending = true
        mutable.value = mutable.value.copy(sending = true)
        if (!contextual) drafts[id] = ""
        if (current(ticket, id)) mutable.value = mutable.value.copy(draft = "")
        try {
            val api = runtime.client
            val token = if (api == null) null else runtime.accessToken()
            if (api == null || token.isNullOrEmpty()) {
                restoreDraft(id, ticket, body, contextual)
                return
            }
            val message = if (editing != null) api.editMessage(token, id, editing, body) else api.sendMessage(token, id, body, replyTo)
            if (!current(ticket, id)) {
                if (!contextual && drafts[id].isNullOrEmpty()) drafts.remove(id)
                return
            }
            val row = row(message)
            if (!contextual && drafts[id].isNullOrEmpty()) drafts.remove(id)
            if (!current(ticket, id)) return
            val messages = mutable.value.messages.toMutableList()
            val previous = messages.indexOfFirst { it.id == row.id }
            if (previous < 0) messages += row else messages[previous] = row
            mutable.value = mutable.value.copy(
                draft = drafts[id].orEmpty(),
                failed = false,
                replyTo = null,
                editing = null,
                messages = messages
            )
        } catch (e: CancellationException) {
            restoreDraft(id, ticket, body, contextual)
            throw e
        } catch (e: Exception) {
            restoreDraft(id, ticket, body, contextual)
            runCatching { android.util.Log.w("ZaparaGroup", "send", e) }
        } finally {
            sending = false
            mutable.value = mutable.value.copy(sending = false)
        }
    }

    private fun restoreDraft(id: String, ticket: Int, body: String, contextual: Boolean) {
        if (!contextual && drafts[id].isNullOrEmpty()) drafts[id] = body
        if (!current(ticket, id)) return
        mutable.value = mutable.value.copy(
            failed = true,
            draft = mutable.value.draft.ifEmpty { if (contextual) body else drafts[id].orEmpty() }
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
        mediaJobs.values.forEach { it.cancel() }
        mediaJobs.clear()
        generation++
        if (mutable.value.editing == null && mutable.value.replyTo == null) {
            conversationId?.let { drafts[it] = mutable.value.draft }
        }
        conversationId = null
        pendingBytes = null
        pendingKind = null
        pendingName = ""
        pendingDurationMs = null
        cachedMedia.clear()
        runCatching { mediaCacheRoot?.deleteRecursively() }
        home = null
        mutable.value = mutable.value.copy(
            hasHome = false, messages = emptyList(), activeConversationId = null, direct = false, failed = false, attachmentPending = false,
            showPeople = false, chatLoading = false, draft = "", replyTo = null, editing = null,
            mediaLoadingId = null, mediaError = false,
            mediaFiles = emptyMap(), mediaLoadingIds = emptySet(), mediaFailedIds = emptySet()
        )
    }

    private fun person(item: Classmate) = GroupPersonUi(item.userId, item.displayName ?: item.username, item.username, item.role, item.self)
    private fun chat(item: Conversation) = GroupChatUi(item.conversationId, item.title, item.lastBody ?: "", item.unread)
    private fun hold(messageId: String, action: String) {
        val message = mutable.value.messages.find { it.id == messageId } ?: return
        val id = conversationId ?: return
        val ticket = generation
        val api = runtime.client ?: return
        viewModelScope.launch {
            try {
                val token = runtime.accessToken()
                if (token.isNullOrEmpty() || !current(ticket, id)) return@launch
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
                if (!current(ticket, id)) return@launch
                val messages = if (next.removed) mutable.value.messages.map { if (it.id == messageId) it.copy(deleted = true) else it } else mutable.value.messages
                mutable.value = mutable.value.copy(draft = next.draft, replyTo = next.replyTo, editing = next.editing, messages = messages)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
            }
        }
    }

    private fun react(messageId: String, emoji: String) {
        if (emoji !in setOf("like", "heart", "laugh", "wow", "sad")) return
        val id = conversationId ?: return
        val ticket = generation
        val api = runtime.client ?: return
        viewModelScope.launch {
            try {
                val token = runtime.accessToken()
                if (token.isNullOrEmpty() || !current(ticket, id)) return@launch
                val updated = row(api.reactMessage(token, id, messageId, emoji))
                if (!current(ticket, id)) return@launch
                mutable.value = mutable.value.copy(messages = mutable.value.messages.map { if (it.id == messageId) updated else it }, failed = false)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
                runCatching { android.util.Log.w("ZaparaGroup", "reaction", e) }
            }
        }
    }

    private fun row(item: ChatMessage) = GroupMessageUi(
        item.messageId, item.senderName, item.body, clock.format(item.createdAt), item.senderId == runtime.userId,
        item.kind, item.deleted, item.replyTo, item.reactions
    )

    companion object {
        fun factory(container: AppContainer, initialCommunityId: String? = null, initialConversationId: String? = null) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                GroupViewModel(container, initialCommunityId, initialConversationId) as T
        }
    }
}
