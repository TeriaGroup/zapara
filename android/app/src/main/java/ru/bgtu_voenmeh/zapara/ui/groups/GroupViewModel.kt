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
import ru.bgtu_voenmeh.zapara.data.communities.BallotBoard
import ru.bgtu_voenmeh.zapara.data.communities.Classmate
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient
import ru.bgtu_voenmeh.zapara.data.communities.Conversation
import ru.bgtu_voenmeh.zapara.data.communities.GroupHome
import ru.bgtu_voenmeh.zapara.data.communities.GroupDesk
import ru.bgtu_voenmeh.zapara.data.communities.GroupRole
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopicList
import java.time.ZoneId
import java.time.Instant
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
    val mediaCacheDir: File? = null,
    val startInChannelList: Boolean = false
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
            mediaCacheDir = container.app.cacheDir,
            startInChannelList = true
        )
    }
}

internal suspend fun readGroupNameOffMain(read: () -> String?): String? = withContext(Dispatchers.IO) { read() }

internal fun trustedChannelRoles(desk: GroupDesk): List<GroupRole> = desk.roles.filter { role ->
    desk.powers.filter { it.roleId == role.roleId }.map { it.power }.toSet() == setOf("channels")
}

data class GroupCommunityUi(val id: String, val name: String, val role: String)
data class GroupPersonUi(val id: String, val name: String, val handle: String, val role: String, val self: Boolean)
data class GroupChatUi(val id: String, val title: String, val preview: String, val unread: Int)
data class GroupMessageUi(val id: String, val author: String, val body: String, val time: String, val mine: Boolean,
    val kind: String = "text", val deleted: Boolean = false, val replyTo: String? = null,
    val reactions: List<ChatReaction> = emptyList(), val day: String = "",
    val senderId: String = "", val createdAt: Instant? = null)

internal interface GroupHoldApi {
    suspend fun edit(token: String, conversationId: String, messageId: String, body: String)
    suspend fun delete(token: String, conversationId: String, messageId: String)
    suspend fun react(token: String, conversationId: String, messageId: String, emoji: String)
}

internal data class GroupHoldState(val draft: String = "", val replyTo: String? = null, val editing: String? = null, val removed: Boolean = false)
private data class GroupDraftContext(val replyTo: String?, val editing: String?)

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
    suspend fun place(api: ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient, token: String, conversationId: String, kind: String, name: String, bytes: ByteArray, replyTo: String?, durationMs: Int? = null, topicId: String? = null): ru.bgtu_voenmeh.zapara.data.communities.ChatMessage {
        if (kind !in setOf("image", "video", "file", "voice", "circle")) throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.InvalidRequest)
        if (bytes.isEmpty() || bytes.size > (if (kind == "voice") 2 * 1024 * 1024 else maxBytes)) throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.PayloadTooLarge)
        if (kind in setOf("voice", "circle") && durationMs == null) throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.InvalidRequest)
        val clean = name.trim().substringAfterLast('/').substringAfterLast('\\').ifBlank {
            when (kind) { "image" -> "Фото"; "video" -> "Видео"; "voice" -> "voice.m4a"; "circle" -> "circle.mp4"; else -> "Документ" }
        }
        return api.sendMedia(token, conversationId, kind, clean, bytes, replyTo, durationMs, topicId)
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
    val communityId: String? = null,
    val title: String = "",
    val myRole: String = "",
    val channels: List<GroupTopic> = emptyList(),
    val canManageChannels: Boolean = false,
    val showChannels: Boolean = false,
    val activeTopicId: String? = null,
    val activeChannelKind: String = "chat",
    val canPost: Boolean = true,
    val board: BallotBoard? = null,
    val ballotRefreshFailed: Boolean = false,
    val ballotCreateVersion: Int = 0,
    val ballotCreateFailed: Boolean = false,
    val desk: GroupDesk? = null,
    val showTrusted: Boolean = false,
    val channelBusy: Boolean = false,
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
    val olderLoading: Boolean = false,
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
    data object Refresh : GroupEvent
    data class Open(val communityId: String) : GroupEvent
    data object Back : GroupEvent
    data class Direct(val userId: String) : GroupEvent
    data class OpenChat(val conversationId: String, val title: String) : GroupEvent
    data class OpenChannel(val topicId: String?) : GroupEvent
    data class GlobalBallots(val title: String) : GroupEvent
    data object Channels : GroupEvent
    data class CreateChannel(val title: String, val icon: String, val kind: String,
        val description: String = "", val accent: String = "default", val pinned: Boolean = false,
        val writePolicy: String = "all") : GroupEvent
    data class RenameChannel(val topicId: String, val title: String, val icon: String, val kind: String,
        val description: String, val accent: String, val pinned: Boolean, val writePolicy: String) : GroupEvent
    data class DeleteChannel(val topicId: String) : GroupEvent
    data object Trusted : GroupEvent
    data object CloseTrusted : GroupEvent
    data class CreateTrustedRole(val name: String) : GroupEvent
    data class EnableTrustedRole(val roleId: String) : GroupEvent
    data class GrantTrusted(val roleId: String, val userId: String) : GroupEvent
    data class RevokeTrusted(val roleId: String, val userId: String) : GroupEvent
    data class CreateBallot(val question: String, val options: List<String>, val days: Int, val headman: Boolean) : GroupEvent
    data class SupportBallot(val ballotId: String) : GroupEvent
    data class VoteBallot(val ballotId: String, val optionId: String) : GroupEvent
    data class CloseBallot(val ballotId: String) : GroupEvent
    data object GroupChat : GroupEvent
    data object Older : GroupEvent
    data class Draft(val text: String) : GroupEvent
    data object CancelContext : GroupEvent
    data object Send : GroupEvent
    data class Hold(val messageId: String, val action: String) : GroupEvent
    data class React(val messageId: String, val emoji: String) : GroupEvent
    data class Media(val kind: String, val name: String, val bytes: ByteArray, val conversationId: String? = null, val topicId: String? = null) : GroupEvent
    data class Recorded(val kind: String, val file: File, val durationMs: Int, val conversationId: String?, val topicId: String? = null) : GroupEvent
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
    private var topics: GroupTopicList? = null
    private var conversationId: String? = null
    private var draftKey: String? = null
    private var poll: Job? = null
    private var generation = 0
    private var trustedRequest = 0
    private val sendingKeys = HashSet<String>()
    private var pendingKind: String? = null
    private var pendingName: String = ""
    private var pendingBytes: ByteArray? = null
    private var pendingDurationMs: Int? = null
    private val cachedMedia = HashMap<String, File>()
    private val mediaCacheRoot = runtime.mediaCacheDir?.let { File(it, "group-chat-${java.util.UUID.randomUUID()}") }
    private val mediaJobs = HashMap<String, Job>()
    private val drafts = HashMap<String, String>()
    private val plainDrafts = HashMap<String, String>()
    private val draftContexts = HashMap<String, GroupDraftContext>()
    private val draftVersions = HashMap<String, Long>()
    private val clock: DateTimeFormatter = DateTimeFormatter.ofPattern("dd.MM HH:mm").withZone(ZoneId.systemDefault())
    private val dayClock: DateTimeFormatter = DateTimeFormatter.ofPattern("dd.MM.yyyy").withZone(ZoneId.systemDefault())

    init { viewModelScope.launch { load() } }

    fun onEvent(event: GroupEvent) {
        when (event) {
            GroupEvent.Refresh -> viewModelScope.launch {
                val current = mutable.value
                val loaded = home
                when {
                    loaded == null -> load()
                    current.activeChannelKind == "ballots" -> openBallots(current.activeTopicId, current.chatTitle)
                    current.activeConversationId != null -> openChat(current.activeConversationId, current.chatTitle,
                        current.direct, current.activeTopicId)
                    else -> {
                        open(loaded.communityId)
                        if (current.showPeople && home?.communityId == loaded.communityId) showHomePane(true)
                    }
                }
            }
            is GroupEvent.Open -> viewModelScope.launch { open(event.communityId) }
            GroupEvent.Back -> leave()
            is GroupEvent.Direct -> viewModelScope.launch { direct(event.userId) }
            is GroupEvent.OpenChat -> viewModelScope.launch { openChat(event.conversationId, event.title, true) }
            GroupEvent.GroupChat -> home?.let { loaded ->
                viewModelScope.launch { openChat(loaded.groupChat.conversationId,
                    topics?.topics?.firstOrNull { it.topicId == null }?.title ?: loaded.groupChat.title, false) }
            }
            is GroupEvent.OpenChannel -> viewModelScope.launch { openChannel(event.topicId) }
            is GroupEvent.GlobalBallots -> viewModelScope.launch { openBallots(null, event.title) }
            GroupEvent.Channels -> showHomePane(false)
            is GroupEvent.CreateChannel -> manageChannel { api, token, id -> api.createTopic(token, id, event.title, event.icon, event.kind,
                event.description, event.accent, event.pinned, event.writePolicy) }
            is GroupEvent.RenameChannel -> manageChannel { api, token, id -> api.renameTopic(token, id, event.topicId, event.title, event.icon, event.kind,
                event.description, event.accent, event.pinned, event.writePolicy) }
            is GroupEvent.DeleteChannel -> manageChannel { api, token, id -> api.deleteTopic(token, id, event.topicId) }
            GroupEvent.Trusted -> viewModelScope.launch { openTrusted() }
            GroupEvent.CloseTrusted -> {
                trustedRequest++
                mutable.value = mutable.value.copy(showTrusted = false, desk = null,
                    channelBusy = if (mutable.value.desk == null) false else mutable.value.channelBusy)
            }
            is GroupEvent.CreateTrustedRole -> createTrustedRole(event.name)
            is GroupEvent.EnableTrustedRole -> enableTrustedRole(event.roleId)
            is GroupEvent.GrantTrusted -> manageTrusted(event.roleId, event.userId, false)
            is GroupEvent.RevokeTrusted -> manageTrusted(event.roleId, event.userId, true)
            is GroupEvent.CreateBallot -> if (mutable.value.canPost && (!event.headman || mutable.value.board?.canOpen == true)) ballotAction(created = true) { api, token, id, topic ->
                if (event.headman) api.openHeadmanBallot(token, id, event.question, event.options, event.days, topic)
                else api.proposeBallot(token, id, event.question, event.options, event.days, topic)
            }
            is GroupEvent.SupportBallot -> ballotAction { api, token, id, _ -> api.supportBallot(token, id, event.ballotId) }
            is GroupEvent.VoteBallot -> ballotAction { api, token, id, _ -> api.voteBallot(token, id, event.ballotId, event.optionId) }
            is GroupEvent.CloseBallot -> if (mutable.value.board?.canClose == true) ballotAction { api, token, id, _ -> api.closeBallot(token, id, event.ballotId) }
            GroupEvent.Older -> viewModelScope.launch { older() }
            is GroupEvent.Draft -> if (mutable.value.canPost) {
                val text = event.text.take(2000)
                draftKey?.let { key ->
                    drafts[key] = text
                    if (mutable.value.replyTo == null && mutable.value.editing == null) plainDrafts[key] = text
                    bumpDraftVersion(key)
                }
                mutable.value = mutable.value.copy(draft = text)
            }
            GroupEvent.CancelContext -> cancelContext()
            GroupEvent.Send -> viewModelScope.launch { send() }
            is GroupEvent.Media -> if ((event.conversationId == null || event.conversationId == conversationId) && event.topicId == mutable.value.activeTopicId && mutable.value.activeChannelKind != "ballots" && mutable.value.canPost)
                attach(event.kind, event.name, event.bytes)
            is GroupEvent.Recorded -> viewModelScope.launch { attachRecorded(event) }
            is GroupEvent.LoadMedia -> loadMedia(event.messageId)
            GroupEvent.MediaError -> mutable.value = mutable.value.copy(mediaError = true)
            is GroupEvent.OpenMedia -> openMedia(event.messageId)
            is GroupEvent.Hold -> hold(event.messageId, event.action)
            is GroupEvent.React -> react(event.messageId, event.emoji)
            GroupEvent.People -> showHomePane(true)
            GroupEvent.Chat -> showHomePane(false)
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
            topics = try { api.topics(token, communityId) } catch (error: CommunityClientException) {
                if (error.failure !in setOf(CommunityClientFailure.NotFound, CommunityClientFailure.InvalidRequest)) throw error
                null
            }
            home = loaded
            publishHome(loaded)
            val requested = if (communityId == runtime.initialCommunityId)
                loaded.directs.firstOrNull { it.conversationId == runtime.initialConversationId } else null
            if (requested == null && runtime.startInChannelList) {
                mutable.value = mutable.value.copy(showChannels = true, showPeople = false)
                armChannels()
            } else if (requested == null) openChat(loaded.groupChat.conversationId,
                topics?.topics?.firstOrNull { it.topicId == null }?.title ?: loaded.groupChat.title, false)
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
        val available = orderedChannels(topics?.topics ?: listOf(GroupTopic(null, loaded.groupChat.title, "💬", "chat", loaded.groupChat.lastBody,
            null, loaded.groupChat.lastAt, loaded.groupChat.unread, false, 0)))
        mutable.value = mutable.value.copy(
            loading = false,
            hasHome = true,
            empty = false,
            communityId = loaded.communityId,
            title = loaded.groupName ?: loaded.name,
            myRole = me?.role ?: "member",
            channels = available,
            canManageChannels = topics?.canManageChannels == true,
            people = loaded.classmates.map { person(it) },
            directs = loaded.directs.map { chat(it) },
            groupUnread = if (topics == null) loaded.groupChat.unread else available.sumOf { it.unread },
            failed = false
        )
    }

    private fun orderedChannels(rows: List<GroupTopic>): List<GroupTopic> = browseChannels(rows)

    private suspend fun openChannel(topicId: String?) {
        val loaded = home ?: return
        val topic = mutable.value.channels.firstOrNull { it.topicId == topicId } ?: return
        if (topic.kind == "ballots") openBallots(topic.topicId, topic.title)
        else openChat(loaded.groupChat.conversationId, "${topic.icon} ${topic.title}", false, topic.topicId)
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

    private suspend fun openChat(id: String, title: String, direct: Boolean, topicId: String? = null) {
        rememberDraft()
        val ticket = ++generation
        poll?.cancel()
        mediaJobs.values.forEach { it.cancel() }
        mediaJobs.clear()
        conversationId = id
        draftKey = if (direct || topics == null) id else "$id:${topicId ?: "general"}"
        val context = draftContexts[draftKey]
        pendingBytes = null
        pendingKind = null
        pendingName = ""
        pendingDurationMs = null
        mutable.value = mutable.value.copy(
            chatTitle = title, activeConversationId = id, activeTopicId = topicId, activeChannelKind = if (direct) "direct" else "chat",
            canPost = direct || topics == null || topics?.topics?.firstOrNull { it.topicId == topicId }?.canPost == true,
            board = null, ballotRefreshFailed = false, showTrusted = false, channelBusy = false, direct = direct, showPeople = false, showChannels = false, messages = emptyList(), hasMore = false, olderLoading = false,
            draft = drafts[draftKey] ?: plainDrafts[draftKey].orEmpty(),
            replyTo = context?.replyTo, editing = context?.editing,
            sending = draftKey in sendingKeys,
            chatLoading = true, failed = false,
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
            val page = api.messages(token, id, topic = topicQuery(direct, topicId))
            if (!current(ticket, id)) return
            mutable.value = mutable.value.copy(
                messages = page.messages.map { row(it) }, hasMore = page.hasMore, failed = false, chatLoading = false
            )
            if (direct || topics == null) acknowledge(token, id, ticket) else refreshTopics(token, ticket)
            if (current(ticket, id)) arm(id)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            android.util.Log.w("ZaparaGroup", "openChat", e)
            if (current(ticket, id)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
        }
    }

    private fun topicQuery(direct: Boolean, topicId: String?): String? =
        if (direct || topics == null) null else topicId ?: "general"

    private suspend fun refreshTopics(token: String, ticket: Int) {
        val loaded = home ?: return
        val api = runtime.client ?: return
        try {
            val result = api.topics(token, loaded.communityId)
            if (ticket != generation) return
            topics = result
            mutable.value = mutable.value.copy(channels = orderedChannels(result.topics), canManageChannels = result.canManageChannels,
                canPost = mutable.value.direct || result.topics.firstOrNull { it.topicId == mutable.value.activeTopicId }?.canPost == true,
                groupUnread = result.topics.sumOf { it.unread })
        } catch (e: CancellationException) {
            throw e
        } catch (_: CommunityClientException) { }
    }

    private suspend fun openBallots(topicId: String?, title: String) {
        val loaded = home ?: return
        rememberDraft()
        val ticket = ++generation
        poll?.cancel()
        mediaJobs.values.forEach { it.cancel() }
        mediaJobs.clear()
        conversationId = null
        draftKey = null
        pendingBytes = null
        pendingKind = null
        mutable.value = mutable.value.copy(activeConversationId = null, activeTopicId = topicId, activeChannelKind = "ballots",
            canPost = topicId == null || topics?.topics?.firstOrNull { it.topicId == topicId }?.canPost == true,
            chatTitle = title, direct = false, showPeople = false, showChannels = false, showTrusted = false,
            messages = emptyList(), hasMore = false, olderLoading = false, board = null,
            ballotRefreshFailed = false, ballotCreateFailed = false,
            draft = "", replyTo = null, editing = null, sending = false,
            chatLoading = true, channelBusy = false, failed = false, attachmentPending = false)
        val api = runtime.client
        if (api == null) {
            if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
            return
        }
        try {
            val token = runtime.accessToken()
            if (token.isNullOrEmpty()) {
                if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
                return
            }
            val board = api.ballots(token, loaded.communityId, topicId)
            if (!currentBallots(ticket, topicId)) return
            mutable.value = mutable.value.copy(board = board, chatLoading = false, ballotRefreshFailed = false)
            armBallots(topicId)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
        }
    }

    private fun currentBallots(ticket: Int, topicId: String?) = ticket == generation &&
        mutable.value.activeChannelKind == "ballots" && mutable.value.activeTopicId == topicId

    private fun armBallots(topicId: String?) {
        val ticket = generation
        poll?.cancel()
        poll = viewModelScope.launch {
            while (isActive && currentBallots(ticket, topicId)) {
                delay(8000)
                if (!currentBallots(ticket, topicId)) return@launch
                try {
                    val loaded = home ?: return@launch
                    val api = runtime.client ?: return@launch
                    val token = runtime.accessToken() ?: return@launch
                    val board = api.ballots(token, loaded.communityId, topicId)
                    if (currentBallots(ticket, topicId)) {
                        mutable.value = mutable.value.copy(board = board, failed = false, ballotRefreshFailed = false)
                        refreshTopics(token, ticket)
                    }
                } catch (e: CancellationException) { throw e }
                catch (_: Exception) { if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(ballotRefreshFailed = true) }
            }
        }
    }

    private fun armChannels() {
        if (topics == null) return
        val ticket = generation
        poll?.cancel()
        poll = viewModelScope.launch {
            while (isActive && ticket == generation && mutable.value.showChannels) {
                delay(8000)
                if (ticket != generation || !mutable.value.showChannels) return@launch
                try {
                    val token = runtime.accessToken() ?: return@launch
                    refreshTopics(token, ticket)
                } catch (e: CancellationException) { throw e }
                catch (_: Exception) { if (ticket == generation) mutable.value = mutable.value.copy(failed = true) }
            }
        }
    }

    private fun manageChannel(action: suspend (CommunityHttpClient, String, String) -> GroupTopicList) {
        if (!mutable.value.canManageChannels || mutable.value.channelBusy) return
        val loaded = home ?: return
        val api = runtime.client ?: return
        val ticket = generation
        mutable.value = mutable.value.copy(channelBusy = true)
        viewModelScope.launch {
            try {
                val token = runtime.accessToken() ?: return@launch
                val result = action(api, token, loaded.communityId)
                if (home?.communityId != loaded.communityId) return@launch
                topics = result
                mutable.value = mutable.value.copy(channels = orderedChannels(result.topics), canManageChannels = result.canManageChannels, failed = false)
                val active = mutable.value.activeTopicId
                if (active != null && result.topics.none { it.topicId == active }) showHomePane(false)
                else if (active != null) mutable.value = mutable.value.copy(chatTitle = result.topics.first { it.topicId == active }.let { "${it.icon} ${it.title}" })
            } catch (e: CancellationException) {
                throw e
            } catch (e: CommunityClientException) {
                reconcileForbidden(e, ticket)
                if (home?.communityId == loaded.communityId) mutable.value = mutable.value.copy(failed = true)
            } catch (_: Exception) {
                if (home?.communityId == loaded.communityId) mutable.value = mutable.value.copy(failed = true)
            } finally {
                if (home?.communityId == loaded.communityId) mutable.value = mutable.value.copy(channelBusy = false)
            }
        }
    }

    private suspend fun reconcileForbidden(error: CommunityClientException, ticket: Int) {
        if (error.failure != CommunityClientFailure.Forbidden || ticket != generation || topics == null || mutable.value.direct) return
        mutable.value = mutable.value.copy(canManageChannels = false, canPost = false)
        try {
            val token = runtime.accessToken() ?: return
            refreshTopics(token, ticket)
        } catch (e: CancellationException) { throw e }
        catch (_: Exception) { }
    }

    private suspend fun openTrusted() {
        if (mutable.value.myRole != "headman") return
        val loaded = home ?: return
        val api = runtime.client ?: return
        showHomePane(false)
        val ticket = generation
        val request = ++trustedRequest
        mutable.value = mutable.value.copy(showTrusted = true, channelBusy = true, failed = false)
        try {
            val token = runtime.accessToken()
            if (token.isNullOrEmpty()) {
                if (ticket == generation && request == trustedRequest)
                    mutable.value = mutable.value.copy(showTrusted = false, desk = null, failed = true)
                return
            }
            val desk = api.desk(token, loaded.communityId)
            if (ticket != generation || request != trustedRequest || !mutable.value.showTrusted ||
                home?.communityId != loaded.communityId) return
            mutable.value = mutable.value.copy(desk = if (desk.headman) desk else null,
                showTrusted = desk.headman, failed = !desk.headman)
        } catch (e: CancellationException) {
            throw e
        } catch (_: Exception) {
            if (ticket == generation && request == trustedRequest && mutable.value.showTrusted)
                mutable.value = mutable.value.copy(failed = true)
        } finally {
            if (ticket == generation && request == trustedRequest) mutable.value = mutable.value.copy(channelBusy = false)
        }
    }

    private fun createTrustedRole(name: String) {
        val desk = mutable.value.desk ?: return
        if (!mutable.value.showTrusted || !desk.headman || mutable.value.channelBusy ||
            trustedChannelRoles(desk).isNotEmpty()) return
        val loaded = home ?: return
        val api = runtime.client ?: return
        val ticket = generation
        mutable.value = mutable.value.copy(channelBusy = true)
        viewModelScope.launch {
            try {
                val token = runtime.accessToken() ?: return@launch
                val created = api.createRole(token, loaded.communityId, name)
                if (ticket == generation && mutable.value.showTrusted) mutable.value = mutable.value.copy(desk = created)
                val old = desk.roles.map { it.roleId }.toSet()
                val role = created.roles.firstOrNull { it.roleId !in old }
                    ?: throw IllegalStateException("new_role_missing")
                val enabled = api.setRolePower(token, loaded.communityId, role.roleId, true)
                if (ticket == generation && mutable.value.showTrusted) mutable.value = mutable.value.copy(desk = enabled, failed = false)
            } catch (e: CancellationException) {
                throw e
            } catch (_: Exception) {
                if (ticket == generation) mutable.value = mutable.value.copy(failed = true)
            } finally {
                if (ticket == generation) mutable.value = mutable.value.copy(channelBusy = false)
            }
        }
    }

    private fun enableTrustedRole(roleId: String) {
        val desk = mutable.value.desk ?: return
        if (!mutable.value.showTrusted || !desk.headman || mutable.value.channelBusy ||
            desk.roles.none { it.roleId == roleId } || desk.grants.any { it.roleId == roleId } ||
            desk.powers.any { it.roleId == roleId }) return
        val loaded = home ?: return
        val api = runtime.client ?: return
        val ticket = generation
        mutable.value = mutable.value.copy(channelBusy = true)
        viewModelScope.launch {
            try {
                val token = runtime.accessToken() ?: return@launch
                val result = api.setRolePower(token, loaded.communityId, roleId, true)
                if (ticket == generation && mutable.value.showTrusted) mutable.value = mutable.value.copy(desk = result, failed = false)
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) { if (ticket == generation) mutable.value = mutable.value.copy(failed = true) }
            finally { if (ticket == generation) mutable.value = mutable.value.copy(channelBusy = false) }
        }
    }

    private fun manageTrusted(roleId: String, userId: String, revoke: Boolean) {
        val desk = mutable.value.desk ?: return
        if (!mutable.value.showTrusted || !desk.headman || mutable.value.channelBusy ||
            trustedChannelRoles(desk).none { it.roleId == roleId } ||
            mutable.value.people.none { it.id == userId && !it.self }) return
        val loaded = home ?: return
        val api = runtime.client ?: return
        val ticket = generation
        mutable.value = mutable.value.copy(channelBusy = true)
        viewModelScope.launch {
            try {
                val token = runtime.accessToken() ?: return@launch
                val result = if (revoke) api.revokeRole(token, loaded.communityId, roleId, userId)
                    else api.grantRole(token, loaded.communityId, roleId, userId)
                if (ticket == generation && mutable.value.showTrusted) mutable.value = mutable.value.copy(desk = result, failed = false)
            } catch (e: CancellationException) {
                throw e
            } catch (_: Exception) {
                if (ticket == generation) mutable.value = mutable.value.copy(failed = true)
            } finally {
                if (ticket == generation) mutable.value = mutable.value.copy(channelBusy = false)
            }
        }
    }

    private fun ballotAction(created: Boolean = false,
        action: suspend (CommunityHttpClient, String, String, String?) -> BallotBoard) {
        if (mutable.value.activeChannelKind != "ballots" || mutable.value.channelBusy) return
        val loaded = home ?: return
        val api = runtime.client ?: return
        val topicId = mutable.value.activeTopicId
        val ticket = generation
        mutable.value = mutable.value.copy(channelBusy = true,
            ballotCreateFailed = if (created) false else mutable.value.ballotCreateFailed)
        viewModelScope.launch {
            try {
                val token = runtime.accessToken()
                if (token.isNullOrEmpty()) {
                    if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(failed = true,
                        ballotCreateFailed = created)
                    return@launch
                }
                val posted = action(api, token, loaded.communityId, topicId)
                if (!currentBallots(ticket, topicId)) return@launch
                val scoped = if (topicId == null) posted else posted.copy(ballots = posted.ballots.filter { it.topicId == topicId })
                mutable.value = mutable.value.copy(board = scoped, failed = false, ballotRefreshFailed = false,
                    ballotCreateFailed = false,
                    ballotCreateVersion = mutable.value.ballotCreateVersion + if (created) 1 else 0)
                try {
                    val fresh = api.ballots(token, loaded.communityId, topicId)
                    if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(board = fresh, ballotRefreshFailed = false)
                } catch (e: CancellationException) { throw e }
                catch (_: Exception) { if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(ballotRefreshFailed = true) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: CommunityClientException) {
                reconcileForbidden(e, ticket)
                if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(failed = true,
                    ballotCreateFailed = created)
            } catch (_: Exception) {
                if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(failed = true,
                    ballotCreateFailed = created)
            } finally {
                if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(channelBusy = false)
            }
        }
    }

    private suspend fun older() {
        val id = conversationId ?: return
        val ticket = generation
        if (mutable.value.olderLoading || !mutable.value.hasMore) return
        val first = mutable.value.messages.firstOrNull() ?: return
        val api = runtime.client ?: return
        if (!current(ticket, id)) return
        mutable.value = mutable.value.copy(olderLoading = true)
        try {
            val token = runtime.accessToken() ?: return
            if (!current(ticket, id)) return
            val page = api.messages(token, id, before = first.id, topic = topicQuery(mutable.value.direct, mutable.value.activeTopicId))
            if (!current(ticket, id)) return
            val have = mutable.value.messages.map { it.id }.toSet()
            val older = page.messages.map { row(it) }.filter { it.id !in have }
            mutable.value = mutable.value.copy(messages = older + mutable.value.messages, hasMore = page.hasMore, failed = false)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            android.util.Log.w("ZaparaGroup", "older", e)
            if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
        } finally {
            if (current(ticket, id)) mutable.value = mutable.value.copy(olderLoading = false)
        }
    }

    private suspend fun pull(id: String) {
        val ticket = generation
        if (!current(ticket, id)) return
        val api = runtime.client ?: return
        val token = runtime.accessToken() ?: return
        if (!current(ticket, id)) return
        val last = mutable.value.messages.lastOrNull()
        val topic = topicQuery(mutable.value.direct, mutable.value.activeTopicId)
        val page = if (last == null) api.messages(token, id, topic = topic) else api.messages(token, id, after = last.id, topic = topic)
        if (!current(ticket, id)) return
        val recent = if (last == null) page.messages else try {
            api.messages(token, id, topic = topic).messages
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
        if (mutable.value.direct || topics == null) {
            if (page.messages.isNotEmpty()) acknowledge(token, id, ticket)
        } else refreshTopics(token, ticket)
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
        if (conversationId == null || mutable.value.editing != null || !mutable.value.canPost) return
        if (draftKey in sendingKeys || pendingBytes != null) {
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
            if (!current(ticket, id) || event.topicId != mutable.value.activeTopicId || mutable.value.activeChannelKind == "ballots" || !mutable.value.canPost) return
            val bytes = withContext(Dispatchers.IO) {
                val limit = if (event.kind == "voice") 2L * 1024 * 1024 else 8L * 1024 * 1024
                if (!event.file.isFile || event.file.length() !in 1L..limit) error("recording_unavailable")
                event.file.readBytes()
            }
            if (!current(ticket, id) || event.topicId != mutable.value.activeTopicId) return
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
        val id = conversationId ?: return
        val key = draftKey ?: id
        if (key in sendingKeys) return
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
            sendingKeys.add(key)
            mutable.value = mutable.value.copy(sending = true)
            try {
                val api = runtime.client
                val token = if (api == null) null else runtime.accessToken()
                if (api == null || token.isNullOrEmpty() || !current(ticket, id)) {
                    if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
                    return
                }
                val saved = GroupMedia.place(api, token, id, fileKind, name, file, mutable.value.replyTo, durationMs,
                    if (topics == null || mutable.value.direct) null else mutable.value.activeTopicId)
                delivered = true
                draftContexts.remove(key)
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
            } catch (e: CommunityClientException) {
                reconcileForbidden(e, ticket)
                if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
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
                sendingKeys.remove(key)
                if (draftKey == key) mutable.value = mutable.value.copy(sending = key in sendingKeys)
            }
            return
        }
        val body = mutable.value.draft.trim()
        if (body.isEmpty()) return
        if (mutable.value.activeChannelKind == "ballots" || !mutable.value.canPost) return
        val selectedTopicId = mutable.value.activeTopicId
        val selectedDirect = mutable.value.direct
        val editing = mutable.value.editing
        val replyTo = mutable.value.replyTo
        val contextual = editing != null || replyTo != null
        val draftVersion = draftVersions[key] ?: 0L
        sendingKeys.add(key)
        mutable.value = mutable.value.copy(sending = true)
        if (!contextual) {
            drafts[key] = ""
            plainDrafts[key] = ""
            if (current(ticket, id)) mutable.value = mutable.value.copy(draft = "")
        }
        try {
            val api = runtime.client
            val token = if (api == null) null else runtime.accessToken()
            if (api == null || token.isNullOrEmpty() || !current(ticket, id) || !mutable.value.canPost) {
                restoreDraft(id, key, draftVersion, body, replyTo, editing)
                return
            }
            val message = when {
                editing != null -> api.editMessage(token, id, editing, body)
                !selectedDirect && topics != null -> api.sendTopicMessage(token, id, body, selectedTopicId, replyTo)
                else -> api.sendMessage(token, id, body, replyTo)
            }
            val row = row(message)
            val unchanged = (draftVersions[key] ?: 0L) == draftVersion
            val restoredPlain = if (contextual) plainDrafts[key].orEmpty() else ""
            if (unchanged) {
                if (restoredPlain.isEmpty()) drafts.remove(key) else drafts[key] = restoredPlain
                if (!contextual) plainDrafts.remove(key)
                draftContexts.remove(key)
            }
            if (showingChannelKey(key, id)) {
                val messages = mutable.value.messages.toMutableList()
                val previous = messages.indexOfFirst { it.id == row.id }
                if (previous < 0) messages += row else messages[previous] = row
                mutable.value = mutable.value.copy(
                    draft = if (unchanged) restoredPlain else mutable.value.draft,
                    failed = false,
                    replyTo = if (unchanged) null else mutable.value.replyTo,
                    editing = if (unchanged) null else mutable.value.editing,
                    messages = messages
                )
            }
        } catch (e: CancellationException) {
            restoreDraft(id, key, draftVersion, body, replyTo, editing)
            throw e
        } catch (e: CommunityClientException) {
            restoreDraft(id, key, draftVersion, body, replyTo, editing)
            reconcileForbidden(e, ticket)
        } catch (e: Exception) {
            restoreDraft(id, key, draftVersion, body, replyTo, editing)
            runCatching { android.util.Log.w("ZaparaGroup", "send", e) }
        } finally {
            sendingKeys.remove(key)
            if (draftKey == key) mutable.value = mutable.value.copy(sending = key in sendingKeys)
        }
    }

    private fun restoreDraft(id: String, key: String, draftVersion: Long, body: String, replyTo: String?, editing: String?) {
        if ((draftVersions[key] ?: 0L) != draftVersion) {
            if (showingChannelKey(key, id)) mutable.value = mutable.value.copy(failed = true)
            return
        }
        drafts[key] = body
        if (replyTo == null && editing == null) plainDrafts[key] = body
        setDraftContext(key, replyTo, editing)
        if (!showingChannelKey(key, id)) return
        mutable.value = mutable.value.copy(
            failed = true,
            draft = body, replyTo = replyTo, editing = editing
        )
    }

    private fun showingChannelKey(key: String, id: String) = draftKey == key && conversationId == id &&
        mutable.value.activeChannelKind != "ballots"

    private fun rememberDraft() {
        val key = draftKey ?: return
        drafts[key] = mutable.value.draft
        if (mutable.value.replyTo == null && mutable.value.editing == null) plainDrafts[key] = mutable.value.draft
        setDraftContext(key, mutable.value.replyTo, mutable.value.editing)
    }

    private fun bumpDraftVersion(key: String) {
        draftVersions[key] = (draftVersions[key] ?: 0L) + 1
    }

    private fun cancelContext() {
        if (mutable.value.replyTo == null && mutable.value.editing == null) return
        val key = draftKey ?: return
        val plain = plainDrafts[key].orEmpty()
        drafts[key] = plain
        draftContexts.remove(key)
        bumpDraftVersion(key)
        mutable.value = mutable.value.copy(draft = plain, replyTo = null, editing = null)
    }

    private fun setDraftContext(key: String, replyTo: String?, editing: String?) {
        if (replyTo == null && editing == null) draftContexts.remove(key)
        else draftContexts[key] = GroupDraftContext(replyTo, editing)
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
        trustedRequest++
        rememberDraft()
        conversationId = null
        draftKey = null
        pendingBytes = null
        pendingKind = null
        pendingName = ""
        pendingDurationMs = null
        cachedMedia.clear()
        runCatching { mediaCacheRoot?.deleteRecursively() }
        home = null
        topics = null
        mutable.value = mutable.value.copy(
            hasHome = false, communityId = null, messages = emptyList(), hasMore = false, olderLoading = false,
            activeConversationId = null, activeTopicId = null, activeChannelKind = "chat", canPost = true,
            channels = emptyList(), canManageChannels = false, board = null, ballotRefreshFailed = false, desk = null,
            channelBusy = false, direct = false, failed = false, attachmentPending = false,
            showPeople = false, showChannels = false, showTrusted = false, chatLoading = false, sending = false,
            draft = "", replyTo = null, editing = null,
            mediaLoadingId = null, mediaError = false,
            mediaFiles = emptyMap(), mediaLoadingIds = emptySet(), mediaFailedIds = emptySet()
        )
    }

    private fun showHomePane(people: Boolean) {
        poll?.cancel()
        mediaJobs.values.forEach { it.cancel() }
        mediaJobs.clear()
        generation++
        trustedRequest++
        rememberDraft()
        conversationId = null
        draftKey = null
        pendingBytes = null
        pendingKind = null
        pendingName = ""
        pendingDurationMs = null
        mutable.value = mutable.value.copy(showPeople = people, showChannels = !people, showTrusted = false, desk = null,
            activeConversationId = null, activeTopicId = null, activeChannelKind = "chat", canPost = true, direct = false,
            messages = emptyList(), hasMore = false, olderLoading = false,
            board = null, ballotRefreshFailed = false, draft = "", replyTo = null, editing = null, sending = false,
            chatLoading = false, channelBusy = false, attachmentPending = false,
            mediaLoadingId = null, mediaError = false, mediaFiles = emptyMap(),
            mediaLoadingIds = emptySet(), mediaFailedIds = emptySet())
        if (!people && topics != null) {
            val ticket = generation
            viewModelScope.launch {
                try {
                    val token = runtime.accessToken() ?: return@launch
                    refreshTopics(token, ticket)
                } catch (e: CancellationException) { throw e }
                catch (_: Exception) { if (ticket == generation) mutable.value = mutable.value.copy(failed = true) }
            }
            armChannels()
        }
    }

    private fun person(item: Classmate) = GroupPersonUi(item.userId, item.displayName ?: item.username, item.username, item.role, item.self)
    private fun chat(item: Conversation) = GroupChatUi(item.conversationId, item.title, item.lastBody ?: "", item.unread)
    private fun hold(messageId: String, action: String) {
        if (!mutable.value.canPost && action in setOf("reply", "edit")) return
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
                if (mutable.value.replyTo == null && mutable.value.editing == null &&
                    (next.replyTo != null || next.editing != null)) draftKey?.let { plainDrafts[it] = mutable.value.draft }
                val messages = if (next.removed) mutable.value.messages.map { if (it.id == messageId) it.copy(deleted = true) else it } else mutable.value.messages
                mutable.value = mutable.value.copy(draft = next.draft, replyTo = next.replyTo, editing = next.editing, messages = messages)
                rememberDraft()
                if (next.replyTo != null || next.editing != null) draftKey?.let(::bumpDraftVersion)
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
        item.kind, item.deleted, item.replyTo, item.reactions, dayClock.format(item.createdAt),
        item.senderId, item.createdAt
    )

    companion object {
        fun factory(container: AppContainer, initialCommunityId: String? = null, initialConversationId: String? = null) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                GroupViewModel(container, initialCommunityId, initialConversationId) as T
        }
    }
}
