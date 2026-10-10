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
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.Schedule
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
import ru.bgtu_voenmeh.zapara.data.communities.*
import java.time.ZoneId
import java.time.Instant
import java.time.LocalDateTime
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
    val initialContext: String? = null,
    val mediaCacheDir: File? = null,
    val startInChannelList: Boolean = false,
    val lessonContext: suspend (String?) -> GroupLessonHint? = { null },
    val context: android.content.Context? = null,
    val scheduleRows: suspend (String?, java.time.LocalDate) -> List<String> = { _, _ -> emptyList() },
    val subjects: suspend (String?) -> List<String> = { emptyList() },
    val subjectContext: suspend (String?, String, java.time.LocalDate) -> GroupSubjectContext = { _,_,_ -> GroupSubjectContext(null,emptyList()) },
    val saveMedia: suspend (String, ByteArray) -> Boolean = { _, _ -> false }
) {
    companion object {
        fun from(container: AppContainer, initialCommunityId: String? = null, initialConversationId: String? = null, initialContext: String? = null) = GroupRuntime(
            guest = container.profile.isGuest,
            userId = container.profile.userId,
            client = container.communities,
            accessToken = { withContext(Dispatchers.IO) { container.accessToken() } },
            groupName = { readGroupNameOffMain { container.repo.settings().myGroupId } },
            openMedia = { message, bytes -> GroupMediaViewer(container.app).open(message, bytes) },
            initialCommunityId = initialCommunityId,
            initialConversationId = initialConversationId,
            initialContext = initialContext,
            mediaCacheDir = container.app.cacheDir,
            startInChannelList = true,
            context = container.app,
            subjects = { groupName -> withContext(Dispatchers.IO) {
                if (!groupName.isNullOrBlank()) container.api.ensureGroup(groupName)
                val group = container.repo.groups().firstOrNull { it.name.equals(groupName, true) }
                group?.let { container.repo.allForGroup(it.id).map { row -> row.subjectRaw }.distinct().sorted() } ?: emptyList()
            } },
            scheduleRows = { groupName, date -> withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val group = container.repo.groups().firstOrNull { it.name.equals(groupName, true) }
                if (!groupName.isNullOrBlank()) container.api.ensureGroup(groupName)
                val resolved = container.repo.groups().firstOrNull { it.name.equals(groupName, true) } ?: group
                if (resolved == null) emptyList()
                else {
                    val metadata = ru.bgtu_voenmeh.zapara.data.api.TimetableApiCache(container.repo.store).read(resolved.id)
                    val choices = if (resolved.id == prefs.myGroupId) container.subgroupChoices(resolved.id) else emptyMap()
                    val lessons = ru.bgtu_voenmeh.zapara.data.Subgroups.visible(container.repo.allForGroup(resolved.id), choices)
                    Schedule.lessonsForDate(lessons, resolved.id, date, metadata?.period?.start ?: prefs.periodStart, metadata?.period?.weekCount ?: prefs.weekCount, prefs.parityInvert)
                        .map { "${it.timeStart}–${it.timeEnd} · ${it.subjectRaw} · ${it.classroomRaw}" }
                }
            } },
            subjectContext = { groupName, subject, date -> withContext(Dispatchers.IO) {
                if (!groupName.isNullOrBlank()) container.api.ensureGroup(groupName)
                val settings = container.repo.settings()
                val group = container.repo.groups().firstOrNull { it.name.equals(groupName,true) }
                if (group == null) GroupSubjectContext(null, emptyList()) else {
                    val cache = ru.bgtu_voenmeh.zapara.data.api.TimetableApiCache(container.repo.store).read(group.id)
                    val lessons = ru.bgtu_voenmeh.zapara.data.Subgroups.visible(container.repo.allForGroup(group.id), container.subgroupChoices(group.id))
                    val now = container.clock()
                    val reference = if (date == now.toLocalDate()) now else date.atStartOfDay()
                    val lesson = GroupSubjectLogic.nextLesson(group.name, subject, reference) { day -> Schedule.lessonsForDate(lessons, group.id, day, cache?.period?.start ?: settings.periodStart, cache?.period?.weekCount ?: settings.weekCount, settings.parityInvert) }
                    val personal = if (group.id != settings.myGroupId) emptyList() else container.homework.all().filter { GroupSubjectLogic.matches(subject,it.norm) }.map { GroupSubjectHomeworkUi(it.id,null,subject,it.text,it.due,it.done) }
                    GroupSubjectContext(lesson,personal)
                }
            } },
            lessonContext = { communityName -> withContext(Dispatchers.IO) {
                val settings = container.repo.settings()
                val selectedId = settings.myGroupId.orEmpty()
                val selectedName = container.repo.groups().firstOrNull { it.id == selectedId }?.name
                if (selectedId.isEmpty() || !sameAcademicGroup(communityName, selectedName)) null
                else {
                    val lessons = container.ownLessons()
                    nextGroupLesson(communityName, selectedName, LocalDateTime.now()) { date ->
                        Schedule.lessonsForDate(lessons, selectedId, date,
                            settings.periodStart, settings.weekCount, settings.parityInvert)
                    }
                }
            } },
            saveMedia = { uri, bytes -> GroupMediaViewer(container.app).save(uri, bytes) }
        )
    }
}

internal suspend fun readGroupNameOffMain(read: () -> String?): String? = withContext(Dispatchers.IO) { read() }

internal fun trustedChannelRoles(desk: GroupDesk): List<GroupRole> = desk.roles.filter { role ->
    desk.powers.filter { it.roleId == role.roleId }.map { it.power }.toSet() == setOf("channels")
}

data class GroupCommunityUi(val id: String, val name: String, val role: String)
data class GroupPersonUi(val id: String, val name: String, val handle: String, val role: String, val self: Boolean)
data class GroupChatUi(val id: String, val title: String, val preview: String, val unread: Int, val peerUserId: String? = null)
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
    suspend fun place(api: ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient, token: String, conversationId: String, kind: String, name: String, bytes: ByteArray, replyTo: String?, durationMs: Int? = null, topicId: String? = null, blankLabel: (String) -> String = { "file" }): ru.bgtu_voenmeh.zapara.data.communities.ChatMessage {
        if (kind !in setOf("image", "video", "file", "voice", "circle")) throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.InvalidRequest)
        val maximum = when (kind) { "voice" -> 4 * 1024 * 1024; "circle" -> 24 * 1024 * 1024; else -> maxBytes }
        if (bytes.isEmpty() || bytes.size > maximum) throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.PayloadTooLarge)
        if (kind in setOf("voice", "circle") && durationMs == null) throw ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.InvalidRequest)
        val clean = name.trim().substringAfterLast('/').substringAfterLast('\\').ifBlank {
            when (kind) { "voice" -> "voice.m4a"; "circle" -> "circle.mp4"; else -> blankLabel(kind) }
        }
        return api.sendMedia(token, conversationId, kind, clean, bytes, replyTo, durationMs, topicId)
    }
}

internal object GroupHold {
    suspend fun perform(message: GroupMessageUi, action: String, conversationId: String, token: String, api: GroupHoldApi, state: GroupHoldState, canModerate: Boolean = false): GroupHoldState {
        if (!ru.bgtu_voenmeh.zapara.ui.chat.HoldDecision.actions(message.kind, message.mine, message.deleted, true, canModerate).contains(action)) return state
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
    val ownerId: String? = null,
    val obligations: GroupObligations? = null,
    val obligationsOpen: Boolean = false,
    val obligationsLoading: Boolean = false,
    val obligationFocusId: String? = null,
    val loading: Boolean = false,
    val empty: Boolean = false,
    val failed: Boolean = false,
    val communityId: String? = null,
    val title: String = "",
    val myRole: String = "",
    val channels: List<GroupTopic> = emptyList(),
    val contextLesson: GroupLessonHint? = null,
    val subjectLesson: GroupLessonHint? = null,
    val subjectHomework: List<GroupSubjectHomeworkUi> = emptyList(),
    val subjectDetail: GroupSubjectHomeworkUi? = null,
    val showSubjectTasks: Boolean = false,
    val canManageChannels: Boolean = false,
    val showChannels: Boolean = false,
    val activeTopicId: String? = null,
    val activeArchivedTopic: GroupTopic? = null,
    val activeChannelKind: String = "chat",
    val canPost: Boolean = true,
    val board: BallotBoard? = null,
    val ballotRefreshFailed: Boolean = false,
    val ballotCreateVersion: Int = 0,
    val ballotAckRevision: Long? = null,
    val ballotCreateFailed: Boolean = false,
    val ballotPendingIds: Set<String> = emptySet(),
    val ballotDraftKey: BallotDraftKey? = null,
    val ballotDraft: BallotDraft = BallotDraft(),
    val desk: GroupDesk? = null,
    val showTrusted: Boolean = false,
    val channelBusy: Boolean = false,
    val channelSaveVersion: Int = 0,
    val creationDraft: TopicCreationDraft? = null,
    val creationNotice: String? = null,
    val creationSubmitting: Boolean = false,
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
    val mediaSavingIds: Set<String> = emptySet(),
    val mediaSavedIds: Set<String> = emptySet(),
    val mediaSaveFailedIds: Set<String> = emptySet(),
    val replyTo: String? = null,
    val editing: String? = null,
    val space: GroupSpace? = null,
    val spacePanel: String? = null,
    val archived: List<GroupTopic> = emptyList(),
    val access: GroupTopicAccess? = null,
    val accessApproval: GroupAccessApproval? = null,
    val audit: List<GroupAuditEvent> = emptyList(),
    val preview: List<GroupTopic>? = null,
    val previewSubject: String? = null,
    val forms: List<GroupForm> = emptyList(),
    val formRefreshFailed: Boolean = false,
    val responses: Map<String, List<GroupFormResponse>> = emptyMap(),
    val responseCursors: Map<String, String?> = emptyMap(),
    val responseCounts: Map<String, Int> = emptyMap(),
    val homework: List<CommunityHomework> = emptyList(),
    val completions: Map<String, HomeworkCompletion> = emptyMap(),
    val scheduleDate: java.time.LocalDate = java.time.LocalDate.now(),
    val scheduleRows: List<String> = emptyList(),
    val subjects: List<String> = emptyList(),
    val spaceError: String? = null,
    val accessRevoked: Boolean = false,
    val composeContext: String? = null,
    val roleImpact: GroupRoleImpact? = null,
    val formCreateVersion: Int = 0,
    val lastSavedHomework: CommunityHomework? = null,
    val homeworkCreateVersion: Int = 0,
    val lastCreatedHomeworkOperationId: String? = null
)

sealed interface GroupEvent {
    data object Obligations : GroupEvent
    data object CloseObligations : GroupEvent
    data class OpenObligation(val row: GroupObligation) : GroupEvent
    data object ClearObligationFocus : GroupEvent
    data object BeginCreation : GroupEvent
    data class CreationChanged(val draft:TopicCreationDraft) : GroupEvent
    data object CancelCreation : GroupEvent
    data object SubmitCreation : GroupEvent

    data class SpaceAction(val action: GroupSpaceAction) : GroupEvent
    data class Context(val text: String?) : GroupEvent
    data class Discuss(val text: String) : GroupEvent
    data object SubjectTasks : GroupEvent
    data class SubjectDetail(val task: GroupSubjectHomeworkUi) : GroupEvent
    data object CloseSubjectTasks : GroupEvent
    data object Refresh : GroupEvent
    data class Open(val communityId: String) : GroupEvent
    data object Back : GroupEvent
    data class Direct(val userId: String) : GroupEvent
    data class OpenChat(val conversationId: String, val title: String) : GroupEvent
    data class OpenChannel(val topicId: String?, val focusObjectId: String? = null) : GroupEvent
    data class OpenArchived(val topicId: String) : GroupEvent
    data class GlobalBallots(val title: String, val focusObjectId: String? = null) : GroupEvent
    data object Channels : GroupEvent
    data class CreateChannel(val title: String, val icon: String, val kind: String,
        val description: String = "", val accent: String = "default", val pinned: Boolean = false,
        val writePolicy: String = "all", val template: String? = null, val categoryId: String? = null, val position: Int = 0, val subject: String? = null) : GroupEvent
    data class RenameChannel(val topicId: String, val title: String, val icon: String, val kind: String,
        val description: String, val accent: String, val pinned: Boolean, val writePolicy: String, val template: String? = null, val categoryId: String? = null, val position: Int = 0, val subject: String? = null, val revision: Long? = null) : GroupEvent
    data class PinChannel(val topic: GroupTopic, val pinned: Boolean) : GroupEvent
    data class DeleteChannel(val topicId: String) : GroupEvent
    data object Trusted : GroupEvent
    data object CloseTrusted : GroupEvent
    data class CreateTrustedRole(val name: String) : GroupEvent
    data class EnableTrustedRole(val roleId: String) : GroupEvent
    data class GrantTrusted(val roleId: String, val userId: String) : GroupEvent
    data class RevokeTrusted(val roleId: String, val userId: String) : GroupEvent
    data class BallotDraftEdit(val key: BallotDraftKey, val question: String? = null,
        val options: List<String>? = null, val days: Int? = null, val composing: Boolean? = null,
        val clear: Boolean = false) : GroupEvent
    data class CreateBallot(val question: String, val options: List<String>, val days: Int, val headman: Boolean,
        val draftRevision: Long = 0, val draftKey: BallotDraftKey? = null) : GroupEvent
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
    data class SaveMedia(val target: GroupMediaSaveTarget, val destination: String) : GroupEvent
    data object People : GroupEvent
    data object Chat : GroupEvent
}

class GroupViewModel internal constructor(private val runtime: GroupRuntime) : ViewModel() {
    constructor(container: AppContainer, initialCommunityId: String? = null, initialConversationId: String? = null, initialContext: String? = null) :
        this(GroupRuntime.from(container, initialCommunityId, initialConversationId, initialContext))

    private val mutable = MutableStateFlow(GroupUiState(guest = runtime.guest || runtime.client == null, ownerId = runtime.userId))
    val state: StateFlow<GroupUiState> = mutable.asStateFlow()
    private var home: GroupHome? = null
    private var contextLesson: GroupLessonHint? = null
    private var topics: GroupTopicList? = null
    private var conversationId: String? = null
    // Sending an own message must not jump over an inbound catch-up gap.
    private var pulledCursor: String? = null
    private var draftKey: String? = null
    private var poll: Job? = null
    private var generation = 0
    private var trustedRequest = 0
    private var accessRequestSerial = 0L
    private var archiveRequestSerial = 0L
    private var archiveAuthorityLoaded = false
    private val sendingKeys = HashSet<String>()
    private var pendingKind: String? = null
    private var pendingName: String = ""
    private var pendingBytes: ByteArray? = null
    private var pendingDurationMs: Int? = null
    private val cachedMedia = HashMap<String, File>()
    private val mediaCacheRoot = runtime.mediaCacheDir?.let { File(it, "group-chat-${java.util.UUID.randomUUID()}") }
    private val mediaJobs = HashMap<String, Job>()
    private val creationDrafts=TopicCreationDrafts()
    private var creationRequest = 0L
    private val pendingCreations = HashSet<String>()
    private val drafts = HashMap<String, String>()
    private val composeContexts = HashMap<String, String?>()
    private var initialContextUsed = false
    private val plainDrafts = HashMap<String, String>()
    private val draftContexts = HashMap<String, GroupDraftContext>()
    private val draftVersions = HashMap<String, Long>()
    private var ballotBoardEpoch = 0L
    private val ballotDrafts = BallotDraftStore()
    private val clock: DateTimeFormatter = DateTimeFormatter.ofPattern("dd.MM HH:mm").withZone(ZoneId.systemDefault())
    private val dayClock: DateTimeFormatter = DateTimeFormatter.ofPattern("dd.MM.yyyy").withZone(ZoneId.systemDefault())
    private var obligationsSerial = 0L

    init { viewModelScope.launch { load() } }

    fun onEvent(event: GroupEvent) {
        if (mutable.value.activeArchivedTopic != null && event in listOf(GroupEvent.Send)) return
        if (mutable.value.activeArchivedTopic != null && (event is GroupEvent.Hold || event is GroupEvent.React || event is GroupEvent.Media || event is GroupEvent.Recorded || event is GroupEvent.CreateBallot || event is GroupEvent.VoteBallot || event is GroupEvent.CloseBallot || event is GroupEvent.SupportBallot)) return
        if (mutable.value.preview != null && event !is GroupEvent.SpaceAction && event !is GroupEvent.OpenChannel && event != GroupEvent.Channels && event != GroupEvent.Back && event != GroupEvent.SubjectTasks && event != GroupEvent.CloseSubjectTasks && event !is GroupEvent.SubjectDetail) return
        when (event) {
            GroupEvent.Obligations -> loadObligations()
            GroupEvent.CloseObligations -> { obligationsSerial++; mutable.value = mutable.value.copy(obligationsOpen = false, obligationsLoading = false, obligations = null) }
            GroupEvent.ClearObligationFocus -> mutable.value = mutable.value.copy(obligationFocusId = null)
            is GroupEvent.OpenObligation -> {
                val row = event.row
                if (mutable.value.preview != null || mutable.value.obligations?.rows?.none { it == row } != false ||
                    (row.topicId != null && mutable.value.channels.none { it.topicId == row.topicId && it.supported && !it.archived && "read" in it.permissions })) return
                obligationsSerial++
                mutable.value = mutable.value.copy(obligationsOpen = false, obligationsLoading = false, obligations = null)
                if (row.topicId == null && row.kind == "ballots") onEvent(GroupEvent.GlobalBallots(uiText(R.string.ux300_group_global_ballots), row.id))
                else onEvent(GroupEvent.OpenChannel(row.topicId, row.id))
            }
            GroupEvent.SubmitCreation -> submitCreation()
            GroupEvent.BeginCreation -> home?.communityId?.takeIf { mutable.value.canManageChannels && !mutable.value.loading }?.let { id ->
                val draft=creationDrafts.begin(id,runtime.userId.orEmpty())
                mutable.value=mutable.value.copy(creationDraft=draft, creationNotice=null)
            }
            is GroupEvent.CreationChanged -> if(event.draft.communityId==home?.communityId && event.draft.ownerId==runtime.userId.orEmpty()) {
                val draft=event.draft.snapshot(); creationDrafts.change(draft)
                mutable.value=mutable.value.copy(creationDraft=draft)
            }
            GroupEvent.CancelCreation -> { home?.communityId?.let(creationDrafts::discard); mutable.value=mutable.value.copy(creationDraft=null) }
            is GroupEvent.SpaceAction -> spaceAction(event.action)
            GroupEvent.SubjectTasks -> mutable.value = mutable.value.copy(showSubjectTasks = true, subjectDetail = null)
            is GroupEvent.SubjectDetail -> mutable.value = mutable.value.copy(showSubjectTasks = true, subjectDetail = event.task)
            GroupEvent.CloseSubjectTasks -> mutable.value = mutable.value.copy(showSubjectTasks = false, subjectDetail = null)
            is GroupEvent.Discuss -> viewModelScope.launch {
                val loaded = home ?: return@launch
                openChat(loaded.groupChat.conversationId, topics?.topics?.firstOrNull { it.topicId == null }?.title ?: loaded.groupChat.title, false)
                mutable.value = mutable.value.copy(composeContext = event.text)
                draftKey?.let { composeContexts[it] = event.text }
            }
            is GroupEvent.Context -> { mutable.value = mutable.value.copy(composeContext = event.text); draftKey?.let { composeContexts[it] = event.text } }
            GroupEvent.Refresh -> viewModelScope.launch {
                val current = mutable.value
                val loaded = home
                if (loaded != null) {
                    val refreshedLesson = try { runtime.lessonContext(loaded.groupName) }
                    catch (error: CancellationException) { throw error }
                    catch (_: Exception) { null }
                    if (home?.communityId == loaded.communityId) {
                        contextLesson = refreshedLesson
                        mutable.value = mutable.value.copy(contextLesson = refreshedLesson)
                    }
                }
                when {
                    loaded == null -> load()
                    current.activeChannelKind == "ballots" -> refreshBallots(current.activeTopicId, current.chatTitle)
                    current.activeChannelKind == "materials" && current.activeConversationId != null -> refreshMaterials(current.activeConversationId)
                    current.activeConversationId != null -> openChat(current.activeConversationId, current.chatTitle,
                        current.direct, current.activeTopicId)
                    else -> {
                        open(loaded.communityId)
                        if (current.showPeople && home?.communityId == loaded.communityId) showHomePane(true)
                    }
                }
            }
            is GroupEvent.Open -> { obligationsSerial++; mutable.value = mutable.value.copy(obligations = null,
                obligationsOpen = false, obligationsLoading = false, obligationFocusId = null)
                viewModelScope.launch { open(event.communityId) } }
            GroupEvent.Back -> leave()
            is GroupEvent.Direct -> viewModelScope.launch { direct(event.userId) }
            is GroupEvent.OpenChat -> viewModelScope.launch { openChat(event.conversationId, event.title, true) }
            GroupEvent.GroupChat -> home?.let { loaded ->
                viewModelScope.launch { openChat(loaded.groupChat.conversationId,
                    topics?.topics?.firstOrNull { it.topicId == null }?.title ?: loaded.groupChat.title, false) }
            }
            is GroupEvent.OpenChannel -> viewModelScope.launch { mutable.value=mutable.value.copy(activeArchivedTopic=null,
                obligationFocusId = event.focusObjectId); openChannel(event.topicId) }
            is GroupEvent.OpenArchived -> viewModelScope.launch { openArchived(event.topicId) }
            is GroupEvent.GlobalBallots -> viewModelScope.launch {
                mutable.value = mutable.value.copy(obligationFocusId = event.focusObjectId); openBallots(null, event.title)
            }
            GroupEvent.Channels -> showHomePane(false)
            is GroupEvent.CreateChannel -> manageChannel { api, token, id -> api.createTopic(token, id, event.title, event.icon, event.kind,
                event.description, event.accent, event.pinned, event.writePolicy, event.template, event.categoryId, event.position, event.subject) }
            is GroupEvent.RenameChannel -> if (mutable.value.channels.firstOrNull { it.topicId==event.topicId }?.let { GroupActions.canManageTopic(mutable.value,it) }==true) manageChannel(requireChannels=false) { api, token, id -> api.renameTopic(token, id, event.topicId, event.title, event.icon, event.kind,
                event.description, event.accent, event.pinned, event.writePolicy, event.template, event.categoryId, event.position, event.subject, event.revision) }
            is GroupEvent.PinChannel -> if (event.topic.topicId != null && GroupActions.canPin(mutable.value, event.topic)) manageChannel(requireChannels = false) { api, token, id ->
                val row = event.topic
                api.renameTopic(token, id, row.topicId!!,
                    row.title, row.icon, row.kind, row.description, row.accent, event.pinned, row.writePolicy, row.template, row.categoryId, row.position, row.subject, row.revision)
            }
            is GroupEvent.DeleteChannel -> if (mutable.value.channels.firstOrNull { it.topicId==event.topicId }?.let { GroupActions.canManageTopic(mutable.value,it) }==true) manageChannel(requireChannels=false) { api, token, id -> api.deleteTopic(token, id, event.topicId) }
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
            is GroupEvent.BallotDraftEdit -> {
                if (event.key == mutable.value.ballotDraftKey && event.key.ownerId == runtime.userId.orEmpty() &&
                    event.key.communityId == home?.communityId && mutable.value.activeChannelKind == "ballots") {
                    val draft = if (event.clear) ballotDrafts.clear(event.key) else ballotDrafts.edit(event.key,
                        event.question, event.options, event.days, event.composing)
                    mutable.value = mutable.value.copy(ballotDraft = draft)
                }
            }
            is GroupEvent.CreateBallot -> if (mutable.value.canPost &&
                (event.draftKey == null || event.draftKey == mutable.value.ballotDraftKey) &&
                (event.draftKey == null || mutable.value.ballotDraft.let { it.revision == event.draftRevision &&
                    it.question.trim() == event.question && it.options.map(String::trim) == event.options && it.days == event.days }) &&
                (!event.headman || mutable.value.board?.canOpen == true)) ballotAction(created = true, draftRevision = event.draftRevision) { api, token, id, topic ->
                if (event.headman) api.openHeadmanBallot(token, id, event.question, event.options, event.days, topic)
                else api.proposeBallot(token, id, event.question, event.options, event.days, topic)
            }
            is GroupEvent.SupportBallot -> if (voteAllowed(event.ballotId)) ballotAction(ballotId = event.ballotId) { api, token, id, _ -> api.supportBallot(token, id, event.ballotId) }
            is GroupEvent.VoteBallot -> if (voteAllowed(event.ballotId)) ballotAction(ballotId = event.ballotId) { api, token, id, _ -> api.voteBallot(token, id, event.ballotId, event.optionId) }
            is GroupEvent.CloseBallot -> if (mutable.value.board?.canClose == true && mutable.value.board?.ballots?.any { it.ballotId == event.ballotId } == true) ballotAction(ballotId = event.ballotId) { api, token, id, _ -> api.closeBallot(token, id, event.ballotId) }
            GroupEvent.Older -> viewModelScope.launch { older() }
            is GroupEvent.Draft -> if (mutable.value.canPost) {
                val text = event.text
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
            is GroupEvent.SaveMedia -> saveMedia(event)
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
                ?: rows.firstOrNull { it.name == wanted || it.name == wanted?.let { name -> runtime.context?.getString(R.string.face_named_group, name) } } ?: rows.singleOrNull()
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
        resetArchiveAuthority()
        val openTicket=++generation
        accessRequestSerial++
        poll?.cancel(); mediaJobs.values.forEach { it.cancel() }; mediaJobs.clear()
        rememberDraft(); conversationId=null; draftKey=null
        contextLesson = null
        mutable.value = mutable.value.copy(loading = true, failed = false, creationDraft=null, creationNotice=null, creationSubmitting=false, channelBusy=false, contextLesson = null, spacePanel=null,access=null,accessApproval=null,preview=null,previewSubject=null,activeArchivedTopic=null,
            activeConversationId=null,activeTopicId=null,messages=emptyList(),board=null,forms=emptyList(),homework=emptyList(),responses=emptyMap(),subjectHomework=emptyList(),subjectDetail=null,showSubjectTasks=false)
        try {
            val token = runtime.accessToken()
            if (token.isNullOrEmpty()) {
                mutable.value = mutable.value.copy(loading = false, failed = true)
                return
            }
            val loaded = api.groupHome(token, communityId)
            if(openTicket!=generation) return
            val space = try { api.space(token, communityId) } catch (e: CommunityClientException) {
                if (e.failure != CommunityClientFailure.NotFound) throw e
                null
            }
            if(openTicket!=generation) return
            mutable.value = mutable.value.copy(space = space, desk = space?.desk, subjects = runtime.subjects(loaded.groupName), spaceError = null)
            topics = if (space != null) GroupTopicList(space.topics, space.desk.headman || "channels" in space.desk.mine) else try { api.topics(token, communityId) } catch (error: CommunityClientException) {
                if (error.failure !in setOf(CommunityClientFailure.NotFound, CommunityClientFailure.InvalidRequest)) throw error
                null
            }
            if(openTicket!=generation) return
            contextLesson = try { runtime.lessonContext(loaded.groupName) }
            catch (error: CancellationException) { throw error }
            catch (_: Exception) { null }
            if(openTicket!=generation) return
            home = loaded
            publishHome(loaded)
            val requestedId = runtime.initialConversationId.takeIf { communityId == runtime.initialCommunityId }
            val requestedDirect = requestedId?.let { id -> loaded.directs.firstOrNull { it.conversationId == id } }
            val requestedGroupChat = loaded.groupChat.takeIf { it.conversationId == requestedId }
            if (requestedDirect != null) {
                openChat(requestedDirect.conversationId, requestedDirect.title, true)
            } else if (requestedGroupChat != null) {
                openChat(requestedGroupChat.conversationId, requestedGroupChat.title, false)
            } else if (runtime.startInChannelList && runtime.initialContext == null) {
                mutable.value = mutable.value.copy(showChannels = true, showPeople = false)
                armChannels()
            } else openChat(loaded.groupChat.conversationId,
                topics?.topics?.firstOrNull { it.topicId == null }?.title ?: loaded.groupChat.title, false)
        } catch (e: CancellationException) {
            throw e
        } catch (e: CommunityClientException) {
            if(openTicket==generation) { reconcileForbidden(e,openTicket); mutable.value=mutable.value.copy(loading=false,failed=true) }
        } catch (e: Exception) {
            if(openTicket!=generation) return
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
            communityId = loaded.communityId, creationDraft=creationDrafts.get(loaded.communityId)?.takeIf { it.ownerId==runtime.userId.orEmpty() }, creationNotice=null, creationSubmitting=loaded.communityId in pendingCreations,
            title = loaded.groupName ?: loaded.name,
            myRole = me?.role ?: "member",
            channels = available,
            contextLesson = contextLesson,
            canManageChannels = topics?.canManageChannels == true,
            people = loaded.classmates.map { person(it) },
            directs = loaded.directs.map { chat(it) },
            groupUnread = if (topics == null) loaded.groupChat.unread else available.sumOf { it.unread },
            failed = false, accessRevoked = false
        )
    }

    private fun voteAllowed(ballotId: String): Boolean {
        val topicId = mutable.value.board?.ballots?.firstOrNull { it.ballotId == ballotId }?.topicId
        val topic = GroupActions.topic(mutable.value,topicId)
        return mutable.value.activeArchivedTopic == null && mutable.value.preview == null && (topic == null || topic.permissions.isEmpty() || "vote" in topic.permissions)
    }

    private fun orderedChannels(rows: List<GroupTopic>): List<GroupTopic> = browseChannels(rows,
        categoryOrder = mutable.value.space?.categories.orEmpty().associate { it.categoryId to it.position })

    private suspend fun openChannel(topicId: String?) {
        val loaded = home ?: return
        val topic = (mutable.value.preview ?: mutable.value.channels).firstOrNull { it.topicId == topicId } ?: return
        openTopic(topic)
    }

    private suspend fun openArchived(topicId: String) {
        val loaded=home ?: return
        if(mutable.value.space==null || (archiveAuthorityLoaded && mutable.value.archived.none { it.topicId==topicId })) return
        val ticket=generation
        try {
            val token=runtime.accessToken() ?: return
            if(!refreshArchiveAuthority(token,ticket) || home?.communityId!=loaded.communityId) return
            val topic=mutable.value.archived.firstOrNull { it.topicId==topicId } ?: return
            mutable.value=mutable.value.copy(activeArchivedTopic=topic,spacePanel=null)
            openTopic(topic)
        } catch(e:CancellationException) { throw e } catch(e:CommunityClientException) { reconcileForbidden(e,ticket) }
    }

    private suspend fun openTopic(topic:GroupTopic) {
        val loaded=home ?: return
        if (!topic.supported || topic.kind in setOf("forms", "homework", "schedule")) {
            openSpecialized(topic)
            return
        }
        if (topic.kind == "ballots") openBallots(topic.topicId, topic.title)
        else openChat(loaded.groupChat.conversationId, ChannelIcons.title(topic.icon, topic.kind, topic.title), false, topic.topicId)
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
        accessRequestSerial++
        rememberDraft()
        val ticket = ++generation
        poll?.cancel()
        mediaJobs.values.forEach { it.cancel() }
        mediaJobs.clear()
        conversationId = id
        pulledCursor = null
        draftKey = if (direct || topics == null) id else "$id:${topicId ?: "general"}"
        val context = draftContexts[draftKey]
        pendingBytes = null
        pendingKind = null
        pendingName = ""
        pendingDurationMs = null
        mutable.value = mutable.value.copy(
            chatTitle = title, activeConversationId = id, activeTopicId = topicId, activeChannelKind = if (direct) "direct" else GroupActions.topic(mutable.value,topicId)?.kind ?: "chat",
            activeArchivedTopic=mutable.value.activeArchivedTopic?.takeIf { !direct && it.topicId==topicId },
            subjectLesson = null, subjectHomework = emptyList(), subjectDetail = null, showSubjectTasks = false, access = null, accessApproval = null, spacePanel = null,
            canPost = mutable.value.activeArchivedTopic==null && mutable.value.preview == null && (direct || topics == null || topics?.topics?.firstOrNull { it.topicId == topicId }?.canPost == true),
            board = null, ballotRefreshFailed = false, showTrusted = false, channelBusy = false, direct = direct, showPeople = false, showChannels = false, messages = emptyList(), hasMore = false, olderLoading = false,
            composeContext = if (!initialContextUsed && runtime.initialContext != null) runtime.initialContext else composeContexts[draftKey],
            draft = drafts[draftKey] ?: plainDrafts[draftKey].orEmpty(),
            replyTo = context?.replyTo, editing = context?.editing,
            sending = draftKey in sendingKeys,
            chatLoading = true, failed = false,
            attachmentPending = false,
            mediaLoadingId = null, mediaError = false,
            mediaFiles = emptyMap(), mediaLoadingIds = emptySet(), mediaFailedIds = emptySet(),
            mediaSavingIds = emptySet(), mediaSavedIds = emptySet(), mediaSaveFailedIds = emptySet()
        )
        if (runtime.initialContext != null && !initialContextUsed) { initialContextUsed = true; composeContexts[draftKey!!] = runtime.initialContext }
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
            pulledCursor = page.messages.lastOrNull()?.messageId
            mutable.value = mutable.value.copy(
                messages = mergeGroupHistory(mutable.value.messages, emptyList(), page.messages.map { row(it) }, emptyList()),
                hasMore = page.hasMore, failed = false, chatLoading = false
            )
            acknowledge(token, id, ticket)
            if (!direct && topics != null) refreshTopics(token, ticket)
            if (current(ticket,id)) loadSubjectContext(token,ticket,topicId)
            if (current(ticket, id)) arm(id)
        } catch (e: CancellationException) {
            throw e
        } catch (e: CommunityClientException) {
            reconcileForbidden(e, ticket)
            if (current(ticket, id)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
        } catch (e: Exception) {
            android.util.Log.w("ZaparaGroup", "openChat", e)
            if (current(ticket, id)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
        }
    }

    private suspend fun loadSubjectContext(token: String, ticket: Int, topicId: String?) {
        val loaded = home ?: return
        val api = runtime.client ?: return
        val topic = GroupActions.topic(mutable.value,topicId) ?: return
        val subject = topic.subject?.takeIf { topic.template == "subject" } ?: return
        val context = runtime.subjectContext(loaded.groupName,subject,mutable.value.scheduleDate)
        val shared = api.listHomework(token,loaded.communityId).filter { hw -> GroupSubjectLogic.matches(subject,hw.title) &&
            (mutable.value.preview == null || hw.topicId == null || mutable.value.preview.orEmpty().any { it.topicId == hw.topicId && "read" in it.permissions }) }
        val done = if (mutable.value.preview == null) api.homeworkCompletions(token,loaded.communityId,shared) else emptyMap()
        if (ticket != generation || mutable.value.activeTopicId != topicId) return
        val tasks = (if (mutable.value.preview == null) context.personal else emptyList()) + shared.map { GroupSubjectHomeworkUi(null,it.homeworkId,it.title,it.body,it.deadlineAt?.atZone(ZoneId.systemDefault())?.toLocalDate(),done[it.homeworkId]?.completed == true) }
        mutable.value = mutable.value.copy(subjectLesson = context.lesson, subjectHomework = tasks, completions = mutable.value.completions + done,
            subjectDetail = mutable.value.subjectDetail?.let { old -> tasks.firstOrNull { it.localId == old.localId && it.sharedId == old.sharedId } })
    }

    private fun topicQuery(direct: Boolean, topicId: String?): String? =
        if (direct || topics == null) null else topicId ?: "general"

    private fun forgetArchivedDrafts(ids: Set<String>) {
        val keys=(drafts.keys+plainDrafts.keys+draftContexts.keys+composeContexts.keys+draftVersions.keys).filter { key -> ids.any { key.endsWith(":$it") } }
        keys.forEach { key -> drafts.remove(key); plainDrafts.remove(key); draftContexts.remove(key); composeContexts.remove(key); draftVersions.remove(key) }
    }

    private fun resetArchiveAuthority() {
        archiveRequestSerial++
        archiveAuthorityLoaded=false
        forgetArchivedDrafts(mutable.value.archived.mapNotNull { it.topicId }.toSet())
        mutable.value=mutable.value.copy(archived=emptyList())
    }

    private suspend fun refreshArchiveAuthority(token: String, ticket: Int, freshActiveTopics: List<GroupTopic> = emptyList()): Boolean {
        if(mutable.value.space==null || ticket!=generation) return false
        val loaded=home ?: return false
        val api=runtime.client ?: return false
        val owner=runtime.userId
        val request=++archiveRequestSerial
        fun current()=ticket==generation && request==archiveRequestSerial && home?.communityId==loaded.communityId && owner==runtime.userId && mutable.value.space!=null
        val rows=try { api.archivedTopics(token,loaded.communityId) }
            catch(e:CancellationException) { throw e }
            catch(e:CommunityClientException) { if(current() && runtime.accessToken()==token && current()) throw e else return false }
        if(!current() || runtime.accessToken()!=token || !current()) return false
        val retained=rows.mapNotNull { it.topicId }.toSet()
        val removed=mutable.value.archived.mapNotNull { it.topicId }.toSet()-retained-freshActiveTopics.mapNotNull { it.topicId }.toSet()
        forgetArchivedDrafts(removed)
        archiveAuthorityLoaded=true
        val previousActive=mutable.value.activeArchivedTopic
        val active=previousActive?.let { old -> rows.firstOrNull { it.topicId==old.topicId } }
        val restored=previousActive?.let { old -> freshActiveTopics.firstOrNull { it.topicId==old.topicId } }
        val closedAccess=mutable.value.access?.topicId in removed
        mutable.value=mutable.value.copy(archived=rows,activeArchivedTopic=active,
            access=if(closedAccess) null else mutable.value.access,accessApproval=if(closedAccess) null else mutable.value.accessApproval,
            spacePanel=if(closedAccess && mutable.value.spacePanel=="access") null else mutable.value.spacePanel)
        if(previousActive!=null && active==null && restored==null) {
            mutable.value=mutable.value.copy(draft="",replyTo=null,editing=null,composeContext=null)
            reconcileForbidden(CommunityClientException(CommunityClientFailure.NotFound),ticket);return false
        }
        if(active!=null) mutable.value=mutable.value.copy(canPost=false)
        return true
    }

    private suspend fun refreshTopics(token: String, ticket: Int) {
        val loaded = home ?: return
        val api = runtime.client ?: return
        try {
            val space = if (mutable.value.space != null) api.space(token, loaded.communityId) else null
            val result = space?.let { GroupTopicList(it.topics, it.desk.headman || "channels" in it.desk.mine) } ?: api.topics(token, loaded.communityId)
            if (ticket != generation) return
            mutable.value = mutable.value.copy(space = space ?: mutable.value.space, desk = space?.desk ?: mutable.value.desk)
            if(mutable.value.space!=null && (archiveAuthorityLoaded || mutable.value.spacePanel=="archive" || mutable.value.activeArchivedTopic!=null)) {
                if(!refreshArchiveAuthority(token,ticket,result.topics) || ticket!=generation) return
            }
            if (!mutable.value.direct && !mutable.value.showChannels && mutable.value.activeArchivedTopic==null && result.topics.none { it.topicId == mutable.value.activeTopicId }) {
                rememberDraft()
                showHomePane(false)
                mutable.value = mutable.value.copy(messages = emptyList(), forms = emptyList(), homework = emptyList(), spaceError = uiText(R.string.space_day_55))
            }
            topics = result
            mutable.value = mutable.value.copy(channels = orderedChannels(result.topics), canManageChannels = result.canManageChannels,
                canPost = mutable.value.activeArchivedTopic==null && (mutable.value.direct || result.topics.firstOrNull { it.topicId == mutable.value.activeTopicId }?.let { if (mutable.value.activeChannelKind == "ballots" && it.permissions.isNotEmpty()) "ballots" in it.permissions else it.canPost } == true),
                groupUnread = result.topics.sumOf { it.unread })
        } catch (e: CancellationException) {
            throw e
        } catch (e: CommunityClientException) {
            if (isAccessFailure(e)) reconcileForbidden(e, ticket)
        }
    }

    private suspend fun openBallots(topicId: String?, title: String) {
        val loaded = home ?: return
        val ballotDraftScope = BallotDraftKey(runtime.userId.orEmpty(), loaded.communityId, topicId)
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
            canPost = mutable.value.activeArchivedTopic==null && mutable.value.preview == null && (topicId == null || topics?.topics?.firstOrNull { it.topicId == topicId }?.let { if (it.permissions.isEmpty()) it.canPost else "ballots" in it.permissions } == true),
            chatTitle = title, direct = false, showPeople = false, showChannels = false, showTrusted = false,
            messages = emptyList(), hasMore = false, olderLoading = false, board = null,
            ballotRefreshFailed = false, ballotCreateFailed = false, ballotPendingIds = emptySet(), ballotAckRevision = null,
            ballotDraftKey = ballotDraftScope, ballotDraft = ballotDrafts.get(ballotDraftScope),
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
        } catch (e: CommunityClientException) {
            reconcileForbidden(e, ticket)
            if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
        } catch (e: Exception) {
            if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(chatLoading = false, failed = true)
        }
    }

    private fun currentBallots(ticket: Int, topicId: String?) = ticket == generation &&
        mutable.value.activeChannelKind == "ballots" && mutable.value.activeTopicId == topicId

    private suspend fun refreshBallots(topicId: String?, title: String) {
        if (mutable.value.board == null) { openBallots(topicId, title); return }
        val loaded = home ?: return
        val api = runtime.client ?: return
        val ticket = generation
        val startedEpoch = ballotBoardEpoch
        mutable.value = mutable.value.copy(chatLoading = true, ballotRefreshFailed = false)
        try {
            val token = runtime.accessToken() ?: throw IllegalStateException("session unavailable")
            val board = api.ballots(token, loaded.communityId, topicId)
            if (currentBallots(ticket, topicId) && startedEpoch == ballotBoardEpoch && mutable.value.ballotPendingIds.isEmpty())
                mutable.value = mutable.value.copy(board = board.copy(ballots = board.ballots.filter { topicId == null || it.topicId == topicId }),
                    ballotRefreshFailed = false)
        } catch (e: CancellationException) { throw e }
        catch (e: CommunityClientException) {
            reconcileForbidden(e, ticket)
            if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(ballotRefreshFailed = true)
        } catch (_: Exception) {
            if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(ballotRefreshFailed = true)
        } finally {
            if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(chatLoading = false)
        }
    }

    private suspend fun refreshMaterials(id: String) {
        val ticket = generation
        if (!current(ticket, id) || mutable.value.chatLoading) return
        mutable.value = mutable.value.copy(chatLoading = true, failed = false)
        try {
            pull(id)
            if (current(ticket, id) && !mutable.value.failed) arm(id)
        }
        catch (e: CancellationException) { throw e }
        catch (e: CommunityClientException) {
            reconcileForbidden(e, ticket)
            if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
        } catch (_: Exception) {
            if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
        } finally {
            if (current(ticket, id)) mutable.value = mutable.value.copy(chatLoading = false)
        }
    }

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
                    val startedEpoch = ballotBoardEpoch
                    val board = api.ballots(token, loaded.communityId, topicId)
                    if (currentBallots(ticket, topicId) && startedEpoch == ballotBoardEpoch && mutable.value.ballotPendingIds.isEmpty()) {
                        mutable.value = mutable.value.copy(board = board, failed = false, ballotRefreshFailed = false)
                        refreshTopics(token, ticket)
                    }
                } catch (e: CancellationException) { throw e }
                catch (e: CommunityClientException) { reconcileForbidden(e, ticket); if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(ballotRefreshFailed = true) }
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

    private fun submitCreation() {
        val submitted = mutable.value.creationDraft?.snapshot() ?: return
        if (submitted.ownerId != runtime.userId.orEmpty() || !submitted.canSubmit(mutable.value)) return
        val loaded = home ?: return
        val api = runtime.client ?: return
        if (!pendingCreations.add(submitted.communityId)) return
        val ticket = generation
        val serial = ++creationRequest
        val modern = mutable.value.space != null
        mutable.value = mutable.value.copy(channelBusy=true, creationSubmitting=true, failed=false, spaceError=null, creationNotice=null)
        viewModelScope.launch {
            try {
                val token = runtime.accessToken() ?: return@launch
                val result = api.createTopic(token,submitted.communityId,submitted.title,submitted.icon,GroupTemplates.kind(submitted.template),
                    submitted.description,submitted.accent,submitted.pinned,submitted.writePolicy,
                    if(modern) submitted.template else null,submitted.categoryId,submitted.position.toInt(),submitted.subject,
                    if(modern) submitted.initialRules() else null)
                // A filtered 201 list may omit the new topic when its creator has no read.
                // ACK applies only to the exact submitted draft, even after navigation.
                val cleared = creationDrafts.acknowledge(submitted)
                if (cleared && mutable.value.creationDraft == submitted) mutable.value=mutable.value.copy(creationDraft=null)
                if (serial!=creationRequest || ticket!=generation || home?.communityId!=loaded.communityId) return@launch
                topics=result
                mutable.value=mutable.value.copy(channels=orderedChannels(result.topics),canManageChannels=result.canManageChannels,
                    creationDraft=creationDrafts.get(submitted.communityId),failed=false,creationNotice=uiText(R.string.creation_success))
            } catch (e: CancellationException) { throw e }
            catch (e: CommunityClientException) {
                if(serial!=creationRequest || ticket!=generation || home?.communityId!=loaded.communityId) return@launch
                reconcileForbidden(e,ticket)
                if(home?.communityId==loaded.communityId) mutable.value=mutable.value.copy(failed=true,spaceError=uiText(if(e.failure==CommunityClientFailure.Forbidden) R.string.space_day_57 else R.string.space_day_59))
            } catch (_: Exception) {
                if(serial==creationRequest && ticket==generation && home?.communityId==loaded.communityId) mutable.value=mutable.value.copy(failed=true,spaceError=uiText(R.string.space_day_60))
            } finally {
                pendingCreations.remove(submitted.communityId)
                if (mutable.value.communityId==submitted.communityId) mutable.value=mutable.value.copy(creationSubmitting=false)
                if(serial==creationRequest && ticket==generation && home?.communityId==loaded.communityId) mutable.value=mutable.value.copy(channelBusy=false)
            }
        }
    }

    private fun manageChannel(requireChannels: Boolean = true, action: suspend (CommunityHttpClient, String, String) -> GroupTopicList) {
        if ((requireChannels && !mutable.value.canManageChannels) || mutable.value.channelBusy) return
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
                mutable.value = mutable.value.copy(channels = orderedChannels(result.topics), canManageChannels = result.canManageChannels, failed = false, channelSaveVersion = mutable.value.channelSaveVersion + 1)
                val active = mutable.value.activeTopicId
                if (mutable.value.space != null) {
                    val fresh = api.space(token, loaded.communityId)
                    mutable.value = mutable.value.copy(space = fresh, desk = fresh.desk)
                }
                if (active != null && result.topics.none { it.topicId == active }) showHomePane(false)
                else if (active != null) mutable.value = mutable.value.copy(chatTitle = result.topics.first { it.topicId == active }.let { ChannelIcons.title(it.icon, it.kind, it.title) })
            } catch (e: CancellationException) {
                throw e
            } catch (e: CommunityClientException) {
                if (e.failure in setOf(CommunityClientFailure.Conflict, CommunityClientFailure.RevisionConflict)) {
                    val token = runtime.accessToken()
                    if (token != null) refreshTopics(token, ticket)
                    mutable.value = mutable.value.copy(spaceError = uiText(R.string.space_day_58))
                } else reconcileForbidden(e, ticket)
                if (home?.communityId == loaded.communityId) mutable.value = mutable.value.copy(failed = true)
            } catch (_: Exception) {
                if (home?.communityId == loaded.communityId) mutable.value = mutable.value.copy(failed = true)
            } finally {
                if (home?.communityId == loaded.communityId) mutable.value = mutable.value.copy(channelBusy = false)
            }
        }
    }

    private fun isAccessFailure(error: CommunityClientException): Boolean = error.failure in setOf(
        CommunityClientFailure.InvalidSession, CommunityClientFailure.Forbidden, CommunityClientFailure.NotFound)

    /** Never retain protected content while a second request is in flight. Drafts stay scoped separately. */
    private suspend fun reconcileForbidden(error: CommunityClientException, ticket: Int) {
        if (!isAccessFailure(error) || ticket != generation) return
        val loaded = home ?: return
        val modern = mutable.value.space != null
        val legacyWithoutTopics = !modern && topics == null
        resetArchiveAuthority()
        rememberDraft()
        accessRequestSerial++
        poll?.cancel()
        mediaJobs.values.forEach { it.cancel() }; mediaJobs.clear()
        generation++
        val nextTicket = generation
        conversationId = null; draftKey = null
        pendingBytes = null; pendingKind = null; pendingName = ""; pendingDurationMs = null
        cachedMedia.values.forEach { runCatching { it.delete() } }; cachedMedia.clear()
        mutable.value = mutable.value.copy(accessRevoked = true, showChannels = true, showPeople = false, showTrusted = false,
            obligations = null, obligationsOpen = false, obligationsLoading = false, obligationFocusId = null,
            activeConversationId = null, activeTopicId = null, activeArchivedTopic=null, activeChannelKind = "chat", chatTitle = "", canPost = false,
            messages = emptyList(), board = null, subjectLesson = null, subjectHomework = emptyList(), subjectDetail = null, showSubjectTasks = false, forms = emptyList(), homework = emptyList(), completions = emptyMap(),
            responses = emptyMap(), responseCursors = emptyMap(), responseCounts = emptyMap(), channels = emptyList(),
            space = null, desk = null, spacePanel = null, accessApproval = null, lastSavedHomework = null, lastCreatedHomeworkOperationId = null, preview = null, previewSubject = null, archived = emptyList(), access = null, audit = emptyList(),
            people = emptyList(), directs = emptyList(), mediaFiles = emptyMap(), mediaLoadingIds = emptySet(), mediaFailedIds = emptySet(),
            mediaSavingIds = emptySet(), mediaSavedIds = emptySet(), mediaSaveFailedIds = emptySet(),
            mediaLoadingId = null, mediaError = false, channelBusy = false, chatLoading = false, sending = false, attachmentPending = false,
            replyTo = null, editing = null, draft = "", composeContext = null, hasMore = false, olderLoading = false,
            canManageChannels = false, failed = true, spaceError = uiText(R.string.space_day_55))
        if (error.failure == CommunityClientFailure.InvalidSession) return
        viewModelScope.launch {
            try {
                val api = runtime.client ?: return@launch
                val token = runtime.accessToken() ?: return@launch
                if (legacyWithoutTopics) {
                    val refreshed = api.groupHome(token,loaded.communityId)
                    val page = api.messages(token,refreshed.groupChat.conversationId)
                    if (nextTicket!=generation || home?.communityId!=loaded.communityId) return@launch
                    home=refreshed
                    conversationId=refreshed.groupChat.conversationId
                    draftKey=conversationId
                    mutable.value=mutable.value.copy(messages=page.messages.map(::row),hasMore=page.hasMore,hasHome=true,
                        activeConversationId=conversationId,activeTopicId=null,activeChannelKind="chat",showChannels=false,
                        chatTitle=refreshed.groupChat.title,accessRevoked=false,canPost=false,failed=true,
                        draft=drafts[draftKey].orEmpty(),people=refreshed.classmates.map(::person),directs=refreshed.directs.map(::chat))
                    arm(conversationId!!)
                    return@launch
                }
                val space = if (modern) api.space(token, loaded.communityId) else null
                val rows = space?.let { GroupTopicList(it.topics, it.desk.headman || "channels" in it.desk.mine) } ?: api.topics(token, loaded.communityId)
                if (nextTicket != generation || home?.communityId != loaded.communityId) return@launch
                topics = rows
                mutable.value = mutable.value.copy(space = space, desk = space?.desk, channels = orderedChannels(rows.topics),
                    canManageChannels = rows.canManageChannels, people = loaded.classmates.map(::person), directs = loaded.directs.map(::chat),
                    accessRevoked = false, canPost = false)
                armChannels()
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) { /* The already cleared neutral surface is retained. */ }
        }
    }

    private fun uiText(id: Int, vararg args: Any): String = runtime.context?.getString(id, *args) ?: ""

    private suspend fun openSpecialized(topic: GroupTopic) {
        accessRequestSerial++
        rememberDraft(); ++generation; poll?.cancel(); conversationId = null; draftKey = null
        mutable.value = mutable.value.copy(activeTopicId = topic.topicId, activeConversationId = null, activeChannelKind = topic.kind,
            chatTitle = topic.title, access = null, accessApproval = null, spacePanel = null, showChannels = false, showPeople = false, canPost = false, channelBusy = false, messages = emptyList(), forms = emptyList(), homework = emptyList(), responses = emptyMap(), spaceError = null)
        if (!topic.supported) { mutable.value = mutable.value.copy(spaceError = uiText(R.string.space_day_56)); return }
        spaceAction(GroupSpaceAction.ReloadContent)
        val ticket = generation
        poll = viewModelScope.launch {
            while (isActive && ticket == generation) {
                delay(8_000)
                try {
                    val token = runtime.accessToken() ?: continue
                    refreshTopics(token, ticket)
                    if (ticket == generation && !mutable.value.showChannels && mutable.value.activeTopicId == topic.topicId) spaceAction(GroupSpaceAction.ReloadContent)
                } catch (e: CancellationException) { throw e } catch (_: Exception) { }
            }
        }
    }

    private fun spaceAction(rawAction: GroupSpaceAction) {
        val action = when(rawAction) {
            is GroupSpaceAction.Access -> rawAction.copy(rules=GroupAccessPresets.topicRules(rawAction.rules).toList())
            is GroupSpaceAction.PreviewAccess -> rawAction.copy(rules=GroupAccessPresets.topicRules(rawAction.rules).toList())
            else -> rawAction
        }
        if (action is GroupSpaceAction.Panel) {
            if(action.name=="archive" && mutable.value.space==null) return
            accessRequestSerial++
            mutable.value = mutable.value.copy(spacePanel = action.name, access = null, accessApproval = null, channelBusy = false, spaceError = null)
            if (action.name == "archive") spaceAction(GroupSpaceAction.LoadArchive)
            if (action.name == "audit") spaceAction(GroupSpaceAction.LoadAudit)
            return
        }
        if (action == GroupSpaceAction.EndPreview) { mutable.value = mutable.value.copy(preview = null, previewSubject = null); showHomePane(false); return }
        if (mutable.value.preview != null && action !is GroupSpaceAction.Preview && action != GroupSpaceAction.ReloadContent && action !is GroupSpaceAction.ScheduleDate) return
        if (mutable.value.channelBusy) return
        if (action is GroupSpaceAction.DeleteCategory && action.communityId != null && action.communityId != home?.communityId) return
        if (action is GroupSpaceAction.Role && action.communityId != null && action.communityId != home?.communityId) return
        if (action is GroupSpaceAction.DeleteCategory &&
            (mutable.value.space?.categories?.none { it.categoryId == action.id } != false ||
                (mutable.value.desk?.headman != true && "channels" !in mutable.value.desk?.mine.orEmpty()))) return
        if (action is GroupSpaceAction.Role) {
            val desk = mutable.value.desk ?: return
            val current = desk.roles.firstOrNull { it.roleId == action.role.roleId } ?: return
            if (action.role.revision != current.revision || !RoleManagementPolicy.positionAllowed(action.role.position, desk, mutable.value.people) ||
                RoleManagementPolicy.roleReason(desk, mutable.value.people, current, "roles") != null) return
        }
        if((action==GroupSpaceAction.LoadArchive || action is GroupSpaceAction.Archive) && mutable.value.space==null) return
        if(action is GroupSpaceAction.Archive && !action.archived) {
            val fresh=mutable.value.archived.firstOrNull { it.topicId==action.topic.topicId } ?: return
            if(!archiveAuthorityLoaded || fresh.revision!=action.topic.revision || !GroupActions.canManageTopic(mutable.value,fresh)) return
        }
        if (action is GroupSpaceAction.Access && mutable.value.accessApproval?.matches(action.topicId, action.revision, action.rules) != true) return
        val api = runtime.client ?: return
        val loaded = home ?: return
        val ticket = generation
        val activeTopic = mutable.value.activeTopicId
        val accessBound = action is GroupSpaceAction.LoadAccess || action is GroupSpaceAction.PreviewAccess || action is GroupSpaceAction.Access
        val accessTicket = if (accessBound) ++accessRequestSerial else accessRequestSerial
        mutable.value = mutable.value.copy(channelBusy = true, spaceError = null, formRefreshFailed = false)
        fun update(block: (GroupUiState) -> GroupUiState) {
            if (ticket == generation && home?.communityId == loaded.communityId && (!accessBound || accessTicket == accessRequestSerial)) mutable.value = block(mutable.value)
        }
        viewModelScope.launch {
            try {
                val token = runtime.accessToken() ?: return@launch
                var result: GroupSpace? = null
                var desk: GroupDesk? = null
                when (action) {
                    is GroupSpaceAction.Category -> result = api.saveCategory(token, loaded.communityId, action.id, action.title, action.position, action.revision)
                    is GroupSpaceAction.DeleteCategory -> result = api.deleteCategory(token, loaded.communityId, action.id)
                    is GroupSpaceAction.Archive -> {
                        result = api.archiveTopic(token, loaded.communityId, action.topic.topicId ?: return@launch, action.archived, action.topic.revision)
                        refreshArchiveAuthority(token,ticket,result.topics)
                    }
                    GroupSpaceAction.LoadArchive -> { refreshArchiveAuthority(token,ticket) }
                    is GroupSpaceAction.LoadAccess -> { val access = api.topicAccess(token, loaded.communityId, action.topicId); update { it.copy(access = access, spacePanel = "access", accessApproval = null) } }
                    is GroupSpaceAction.PreviewAccess -> {
                        val preview = api.topicAccessPreview(token, loaded.communityId, action.topicId, action.rules, action.revision)
                        update { it.copy(accessApproval = GroupAccessApproval(action.rules.toList(), preview)) }
                    }
                    is GroupSpaceAction.Access -> {
                        result = api.setTopicAccess(token, loaded.communityId, action.topicId, action.rules, action.revision)
                        val access = api.topicAccess(token, loaded.communityId, action.topicId)
                        update { it.copy(access = access, accessApproval = null) }
                    }
                    GroupSpaceAction.LoadAudit -> { val audit = api.groupAudit(token, loaded.communityId); update { it.copy(audit = audit) } }
                    is GroupSpaceAction.Preview -> { val preview = api.previewPermissions(token, loaded.communityId, action.userId, action.roleId); update { it.copy(preview = preview, showChannels = true, previewSubject = action.userId?.let { id -> it.people.firstOrNull { person -> person.id == id }?.name }
                            ?: action.roleId?.let { id -> it.desk?.roles?.firstOrNull { role -> role.roleId == id }?.name } ?: uiText(R.string.space_day_148)) } }
                    is GroupSpaceAction.Role -> desk = api.saveRoleSettings(token, loaded.communityId, action.role)
                    is GroupSpaceAction.CreateRole -> desk = api.createRole(token, loaded.communityId, action.name)
                    is GroupSpaceAction.DeleteRole -> desk = api.deleteRole(token, loaded.communityId, action.roleId)
                    is GroupSpaceAction.RoleImpact -> { val impact = api.roleImpact(token, loaded.communityId, action.roleId); update { it.copy(roleImpact = impact) } }
                    is GroupSpaceAction.Power -> desk = api.setPower(token, loaded.communityId, action.roleId, action.power, action.on)
                    is GroupSpaceAction.Grant -> desk = if (action.on) api.grantRole(token, loaded.communityId, action.roleId, action.userId) else api.revokeRole(token, loaded.communityId, action.roleId, action.userId)
                    is GroupSpaceAction.CreateForm -> {
                        val forms = api.createForm(token, loaded.communityId, activeTopic ?: return@launch, action.title, action.description, action.deadline, action.anonymous, action.questions)
                        update { it.copy(forms = forms, formCreateVersion = it.formCreateVersion + 1) }
                    }
                    is GroupSpaceAction.SubmitForm -> {
                        val acknowledged = api.submitForm(token, loaded.communityId, action.formId, action.answers)
                        update { it.copy(forms = mergeFormSubmitAck(it.forms, acknowledged)) }
                        try {
                            val forms = api.forms(token, loaded.communityId, activeTopic ?: return@launch)
                            update { it.copy(forms = mergeFormRefreshWithAck(forms, acknowledged)) }
                        } catch (e: CancellationException) { throw e }
                        catch (_: Exception) {
                            update { it.copy(spaceError = uiText(R.string.ux60_form_saved_refresh_failed),
                                formRefreshFailed = true) }
                        }
                    }
                    is GroupSpaceAction.Responses -> {
                        if (action.after != null && mutable.value.responseCursors[action.formId] != action.after) return@launch
                        val page = api.formResponsesPage(token, loaded.communityId, action.formId, action.after)
                        if (page.nextCursor != null && page.nextCursor == action.after) throw CommunityClientException(CommunityClientFailure.InvalidPayload)
                        update { it.copy(responses = it.responses + (action.formId to (if (action.after == null) page.responses else it.responses[action.formId].orEmpty() + page.responses)), responseCursors = it.responseCursors + (action.formId to page.nextCursor), responseCounts = it.responseCounts + (action.formId to page.totalResponses)) }
                    }
                    is GroupSpaceAction.SaveHomework -> {
                        val audienceSupported = mutable.value.space?.capabilities?.homeworkAudience == true
                        if (action.audience?.selected == true && !audienceSupported) throw CommunityClientException(CommunityClientFailure.InvalidRequest)
                        val audience = action.audience.takeIf { audienceSupported }
                        val saved = if (action.id == null) api.shareHomework(token, loaded.communityId, action.title, action.body, 0, action.deadline, activeTopic, audience, action.operationId.takeIf { audienceSupported })
                            else api.updateHomework(token, loaded.communityId, action.id, action.title, action.body, action.revision, action.deadline, activeTopic, audience)
                        update { state -> state.copy(homework = state.homework.filterNot { it.homeworkId == saved.homeworkId } + saved,
                            lastSavedHomework = saved,
                            homeworkCreateVersion = state.homeworkCreateVersion + if (action.id == null) 1 else 0,
                            lastCreatedHomeworkOperationId = if (action.id == null) action.operationId else state.lastCreatedHomeworkOperationId) }
                    }
                    is GroupSpaceAction.CompleteHomework -> {
                        val completed = api.upsertCompletion(token, loaded.communityId, action.id, action.on, mutable.value.completions[action.id]?.revision ?: 0)
                        update { state -> state.copy(completions = state.completions + (action.id to completed), subjectHomework = state.subjectHomework.map { if (it.sharedId == action.id) it.copy(done = completed.completed) else it }, subjectDetail = state.subjectDetail?.let { if (it.sharedId == action.id) it.copy(done = completed.completed) else it }) }
                    }
                    is GroupSpaceAction.ScheduleDate -> { val rows = runtime.scheduleRows(loaded.groupName, action.date); update { it.copy(scheduleDate = action.date, scheduleRows = rows) } }
                    GroupSpaceAction.ReloadContent -> when (mutable.value.activeChannelKind) {
                        "forms" -> { val forms = api.forms(token, loaded.communityId, activeTopic ?: return@launch); update { state -> state.copy(forms = if (state.preview == null) forms else forms.map { it.copy(canRespond = false, canViewResponses = false, ownResponse = null) }) } }
                        "homework" -> { val rows = api.listHomework(token, loaded.communityId).filter { it.topicId == activeTopic }; val done = if (mutable.value.preview == null) api.homeworkCompletions(token, loaded.communityId, rows, activeTopic) else emptyMap(); update { it.copy(homework = rows, completions = done) } }
                        "schedule" -> { val rows = runtime.scheduleRows(loaded.groupName, mutable.value.scheduleDate); update { it.copy(scheduleRows = rows) } }
                    }
                    else -> Unit
                }
                if (home?.communityId != loaded.communityId || ticket != generation || (accessBound && accessTicket != accessRequestSerial)) return@launch
                result?.let { topics = GroupTopicList(it.topics, it.desk.headman || "channels" in it.desk.mine); if (ticket == generation && home?.communityId == loaded.communityId) mutable.value = mutable.value.copy(space = it, desk = it.desk, channels = orderedChannels(it.topics), canManageChannels = topics?.canManageChannels == true) }
                desk?.let { if (ticket == generation && home?.communityId == loaded.communityId) mutable.value = mutable.value.copy(desk = it, space = mutable.value.space?.copy(desk = it)) }
                if (result != null && activeTopic != null && result.topics.none { it.topicId == activeTopic } && !mutable.value.showChannels) {
                    showHomePane(false)
                    mutable.value = mutable.value.copy(spaceError = uiText(R.string.space_day_57))
                }
            } catch (e: CancellationException) { throw e }
            catch (e: CommunityClientException) {
                if (ticket == generation) {
                    if (ticket == generation && home?.communityId == loaded.communityId) mutable.value = mutable.value.copy(spaceError = if (e.failure == CommunityClientFailure.Forbidden) uiText(R.string.space_day_57) else if (e.failure in setOf(CommunityClientFailure.Conflict, CommunityClientFailure.RevisionConflict)) uiText(R.string.space_day_58) else uiText(R.string.space_day_59))
                    if (e.failure in setOf(CommunityClientFailure.Conflict, CommunityClientFailure.RevisionConflict)) {
                        if (accessBound) update { it.copy(accessApproval = null) }
                        try {
                        val token = runtime.accessToken()
                        if (token != null) {
                            if (action is GroupSpaceAction.CompleteHomework) {
                                val current = api.getCompletion(token, loaded.communityId, action.id)
                                update { state -> state.copy(completions = state.completions + (action.id to current),
                                    subjectHomework = state.subjectHomework.map { if (it.sharedId == action.id) it.copy(done = current.completed) else it },
                                    subjectDetail = state.subjectDetail?.let { if (it.sharedId == action.id) it.copy(done = current.completed) else it }) }
                            } else if (action is GroupSpaceAction.SaveHomework) {
                                val rows = api.listHomework(token, loaded.communityId).filter { it.topicId == activeTopic }
                                update { it.copy(homework = rows) }
                            } else if (action is GroupSpaceAction.Access || action is GroupSpaceAction.PreviewAccess) {
                                val id = if (action is GroupSpaceAction.Access) action.topicId else (action as GroupSpaceAction.PreviewAccess).topicId
                                val access = api.topicAccess(token, loaded.communityId, id)
                                update { it.copy(access = access, accessApproval = null) }
                            } else refreshTopics(token, ticket)
                        }
                                            } catch (error: CancellationException) { throw error }
                        catch (_: Exception) { update { it.copy(spaceError=uiText(R.string.space_day_60),accessApproval=null) } }
                    }
                    if (isAccessFailure(e)) reconcileForbidden(e, ticket)
                }
            } catch (_: Exception) { if (ticket == generation && home?.communityId == loaded.communityId) mutable.value = mutable.value.copy(spaceError = uiText(R.string.space_day_60)) }
            finally { update { it.copy(channelBusy = false) } }
        }
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

    private fun ballotAction(created: Boolean = false, ballotId: String? = null, draftRevision: Long = 0,
        action: suspend (CommunityHttpClient, String, String, String?) -> BallotBoard) {
        if (mutable.value.activeChannelKind != "ballots" || mutable.value.channelBusy ||
            (ballotId != null && ballotId in mutable.value.ballotPendingIds)) return
        val loaded = home ?: return
        val api = runtime.client ?: return
        val topicId = mutable.value.activeTopicId
        val ticket = generation
        val submittedDraftKey = if (created) mutable.value.ballotDraftKey else null
        val submittedDraft = if (created) mutable.value.ballotDraft else null
        mutable.value = mutable.value.copy(channelBusy = if (created) true else mutable.value.channelBusy,
            ballotPendingIds = if (ballotId == null) mutable.value.ballotPendingIds else mutable.value.ballotPendingIds + ballotId,
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
                if (created && submittedDraftKey != null && submittedDraft?.revision == draftRevision &&
                    submittedDraftKey.ownerId == runtime.userId.orEmpty() &&
                    submittedDraftKey.communityId == loaded.communityId) {
                    if (ballotDrafts.acknowledge(submittedDraftKey, draftRevision) &&
                        mutable.value.ballotDraftKey == submittedDraftKey)
                        mutable.value = mutable.value.copy(ballotDraft = ballotDrafts.get(submittedDraftKey))
                }
                if (!currentBallots(ticket, topicId)) return@launch
                ballotBoardEpoch++
                val scoped = mergeBallotPost(mutable.value.board, posted, topicId, ballotId)
                mutable.value = mutable.value.copy(board = scoped, failed = false, ballotRefreshFailed = false,
                    ballotCreateFailed = false,
                    ballotAckRevision = if (created) draftRevision else mutable.value.ballotAckRevision,
                    ballotCreateVersion = mutable.value.ballotCreateVersion + if (created) 1 else 0)
                try {
                    api.ballots(token, loaded.communityId, topicId)
                    // POST is the acknowledged result. A read replica can lag behind it.
                    if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(ballotRefreshFailed = false)
                } catch (e: CancellationException) { throw e }
                catch (e: CommunityClientException) { reconcileForbidden(e, ticket); if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(ballotRefreshFailed = true) }
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
                if (currentBallots(ticket, topicId)) mutable.value = mutable.value.copy(
                    channelBusy = if (created) false else mutable.value.channelBusy,
                    ballotPendingIds = if (ballotId == null) mutable.value.ballotPendingIds else mutable.value.ballotPendingIds - ballotId)
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
        } catch (e: CommunityClientException) {
            reconcileForbidden(e, ticket)
            if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
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
        val token = runtime.accessToken() ?: run { reconcileForbidden(CommunityClientException(CommunityClientFailure.InvalidSession),ticket); return }
        if (!current(ticket, id)) return
        val atRequest = mutable.value.messages
        val last = pulledCursor
        val topic = topicQuery(mutable.value.direct, mutable.value.activeTopicId)
        val page = ru.bgtu_voenmeh.zapara.data.communities.loadGroupChatUpdates(last) { after ->
            val result = api.messages(token, id, after = after, topic = topic)
            if (!current(ticket, id)) throw CancellationException("Conversation changed")
            result
        }
        if (!current(ticket, id)) return
        val recent = if (last == null) page.messages else if (page.hasMore) emptyList() else try {
            api.messages(token, id, topic = topic).messages
        } catch (e: CancellationException) {
            throw e
        } catch (e: CommunityClientException) {
            if (isAccessFailure(e)) throw e
            emptyList()
        }
        if (!current(ticket, id)) return
        val merged = mergeGroupHistory(mutable.value.messages, atRequest,
            page.messages.map { row(it) }, recent.map { row(it) })
        pulledCursor = page.messages.lastOrNull()?.messageId ?: pulledCursor
        mutable.value = mutable.value.copy(
            messages = merged,
            hasMore = if (last == null) page.hasMore else mutable.value.hasMore,
            failed = false,
            chatLoading = false
        )
        // Latest-page refresh above only amends existing rows. Acknowledge the published
        // contiguous cursor, never a newer row that was not added to this conversation.
        pulledCursor?.let { acknowledge(token, id, ticket, it) }
        if (!mutable.value.direct && topics != null) {
            refreshTopics(token,ticket)
            if (current(ticket,id)) loadSubjectContext(token,ticket,mutable.value.activeTopicId)
        }
    }

    private suspend fun acknowledge(token: String, id: String, ticket: Int, throughMessageId: String? = null) {
        val api = runtime.client ?: return
        val target = throughMessageId ?: pulledCursor ?: return
        try {
            val read = api.markRead(token, id, target)
            if (!current(ticket, id)) return
            mutable.value = if (read.kind == "group") {
                mutable.value.copy(groupUnread = read.unread)
            } else {
                mutable.value.copy(directs = mutable.value.directs.map { if (it.id == id) it.copy(unread = read.unread) else it })
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: CommunityClientException) { reconcileForbidden(e,ticket) }
    }

    private fun attach(kind: String, name: String, bytes: ByteArray, durationMs: Int? = null) {
        if (conversationId == null || mutable.value.editing != null || !mutable.value.canPost) return
        if (draftKey in sendingKeys || pendingBytes != null) {
            mutable.value = mutable.value.copy(failed = true)
            return
        }
        val maxBytes = when (kind) { "voice" -> 4 * 1024 * 1024; "circle" -> 24 * 1024 * 1024; else -> GroupMedia.maxBytes }
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
                val limit = if (event.kind == "voice") 4L * 1024 * 1024 else 24L * 1024 * 1024
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
                val token = runtime.accessToken() ?: error(runtime.context?.getString(R.string.face_session_unavailable) ?: "session")
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
            } catch (e: CommunityClientException) {
                reconcileForbidden(e,ticket)
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
            } catch (e: CommunityClientException) {
                reconcileForbidden(e,ticket)
            } catch (_: Exception) {
                if (current(ticket, id)) mutable.value = mutable.value.copy(mediaError = true)
            } finally {
                if (current(ticket, id)) mutable.value = mutable.value.copy(mediaLoadingId = null)
            }
        }
    }

    private fun saveMedia(event: GroupEvent.SaveMedia) {
        val id = conversationId ?: return
        val message = mutable.value.messages.firstOrNull { it.id == event.target.messageId }
        if (event.target.ownerId != runtime.userId ||
            !canSaveGroupAttachment(mutable.value.ownerId, mutable.value.communityId, id,
                mutable.value.activeTopicId, event.target, message) ||
            event.target.messageId in mutable.value.mediaSavingIds) return
        val api = runtime.client ?: return
        val ticket = generation
        mutable.value = mutable.value.copy(mediaSavingIds = mutable.value.mediaSavingIds + event.target.messageId,
            mediaSavedIds = mutable.value.mediaSavedIds - event.target.messageId,
            mediaSaveFailedIds = mutable.value.mediaSaveFailedIds - event.target.messageId)
        viewModelScope.launch {
            try {
                val token = runtime.accessToken() ?: error(runtime.context?.getString(R.string.face_session_unavailable) ?: "session")
                if (!mediaSaveTargetCurrent(ticket, id, event.target)) return@launch
                val bytes = api.downloadMedia(token, id, event.target.messageId)
                if (!mediaSaveTargetCurrent(ticket, id, event.target)) return@launch
                require(bytes.isNotEmpty() && bytes.size <= 24 * 1024 * 1024)
                if (!runtime.saveMedia(event.destination, bytes)) error("attachment_save_failed")
                if (mediaSaveTargetCurrent(ticket, id, event.target)) mutable.value = mutable.value.copy(
                    mediaSavedIds = mutable.value.mediaSavedIds + event.target.messageId,
                    mediaSaveFailedIds = mutable.value.mediaSaveFailedIds - event.target.messageId)
            } catch (cancel: CancellationException) {
                throw cancel
            } catch (error: CommunityClientException) {
                reconcileForbidden(error, ticket)
                if (mediaSaveTargetCurrent(ticket, id, event.target)) mutable.value = mutable.value.copy(
                    mediaSaveFailedIds = mutable.value.mediaSaveFailedIds + event.target.messageId)
            } catch (_: Exception) {
                if (mediaSaveTargetCurrent(ticket, id, event.target)) mutable.value = mutable.value.copy(
                    mediaSaveFailedIds = mutable.value.mediaSaveFailedIds + event.target.messageId)
            } finally {
                if (current(ticket, id)) mutable.value = mutable.value.copy(
                    mediaSavingIds = mutable.value.mediaSavingIds - event.target.messageId)
            }
        }
    }

    private fun mediaSaveTargetCurrent(ticket: Int, id: String, target: GroupMediaSaveTarget): Boolean {
        val state = mutable.value
        val message = state.messages.firstOrNull { it.id == target.messageId }
        return current(ticket, id) && target.ownerId == runtime.userId &&
            canSaveGroupAttachment(state.ownerId, state.communityId, state.activeConversationId,
                state.activeTopicId, target, message)
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
                    if (topics == null || mutable.value.direct) null else mutable.value.activeTopicId,
                    blankLabel = { kind ->
                        runtime.context?.getString(when (kind) {
                            "image" -> R.string.face_photo
                            "video" -> R.string.face_video
                            else -> R.string.face_document
                        }) ?: "file"
                    })
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
        val rawDraft = mutable.value.draft
        val composeContext = mutable.value.composeContext
        val preview = groupMessagePreview(rawDraft, composeContext, mutable.value.editing != null)
        if (!preview.ready) return
        val body = preview.body
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
                restoreDraft(id, key, draftVersion, rawDraft, replyTo, editing)
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
                    messages = messages,
                    composeContext = if (unchanged) null else mutable.value.composeContext
                )
                if (unchanged) composeContexts.remove(key)
            }
        } catch (e: CancellationException) {
            restoreDraft(id, key, draftVersion, rawDraft, replyTo, editing)
            throw e
        } catch (e: CommunityClientException) {
            restoreDraft(id, key, draftVersion, rawDraft, replyTo, editing)
            reconcileForbidden(e, ticket)
        } catch (e: Exception) {
            restoreDraft(id, key, draftVersion, rawDraft, replyTo, editing)
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
        composeContexts[key] = mutable.value.composeContext
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
                } catch (e: CommunityClientException) {
                    reconcileForbidden(e, ticket)
                    if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
                } catch (e: Exception) {
                    android.util.Log.w("ZaparaGroup", "pull", e)
                    if (current(ticket, id)) mutable.value = mutable.value.copy(failed = true)
                }
            }
        }
    }

    private fun loadObligations() {
        val loaded = home ?: return
        val api = runtime.client ?: return
        if (mutable.value.preview != null || mutable.value.accessRevoked || mutable.value.space == null) return
        val serial = ++obligationsSerial
        val ticket = generation
        val owner = runtime.userId
        var topics = mutable.value.channels.toList()
        fun currentScope() = serial == obligationsSerial && ticket == generation && owner == runtime.userId &&
            home?.communityId == loaded.communityId && mutable.value.preview == null && !mutable.value.accessRevoked &&
            obligationAuthority(mutable.value.channels) == obligationAuthority(topics)
        mutable.value = mutable.value.copy(obligationsOpen = true, obligationsLoading = true, obligations = null)
        viewModelScope.launch {
            try {
                val token = runtime.accessToken() ?: return@launch
                val authority = api.space(token, loaded.communityId)
                if (!currentScope()) return@launch
                topics = authority.topics.toList()
                mutable.value = mutable.value.copy(space = authority, desk = authority.desk, channels = topics)
                val result = collectGroupObligations(api, token, loaded.communityId, topics, ::currentScope)
                if (currentScope()) mutable.value = mutable.value.copy(obligations = result)
            } catch (cancel: CancellationException) { throw cancel }
            catch (error: CommunityClientException) {
                if (currentScope()) reconcileForbidden(error, ticket)
            }
            catch (_: Exception) { if (currentScope()) mutable.value = mutable.value.copy(spaceError = uiText(R.string.ux300_group_obligations_failed)) }
            finally { if (serial == obligationsSerial) mutable.value = mutable.value.copy(obligationsLoading = false) }
        }
    }

    private fun leave() {
        obligationsSerial++
        mutable.value = mutable.value.copy(obligations = null, obligationsOpen = false, obligationsLoading = false, obligationFocusId = null)
        resetArchiveAuthority()
        accessRequestSerial++
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
            hasHome = false, communityId = null, creationDraft=null, creationNotice=null, creationSubmitting=false, messages = emptyList(), subjectLesson = null, subjectHomework = emptyList(), subjectDetail = null, showSubjectTasks = false,
            space = null, spacePanel = null, preview = null, previewSubject = null, forms = emptyList(), responses = emptyMap(), responseCursors = emptyMap(), responseCounts = emptyMap(),
            archived = emptyList(), access = null, audit = emptyList(), homework = emptyList(), completions = emptyMap(), scheduleRows = emptyList(), subjects = emptyList(), spaceError = null, composeContext = null, hasMore = false, olderLoading = false,
            activeConversationId = null, activeTopicId = null, activeArchivedTopic=null, activeChannelKind = "chat", canPost = true,
            channels = emptyList(), canManageChannels = false, board = null, ballotRefreshFailed = false, desk = null,
            channelBusy = false, direct = false, failed = false, attachmentPending = false,
            showPeople = false, showChannels = false, showTrusted = false, chatLoading = false, sending = false,
            draft = "", replyTo = null, editing = null,
            mediaLoadingId = null, mediaError = false,
            mediaFiles = emptyMap(), mediaLoadingIds = emptySet(), mediaFailedIds = emptySet(),
            mediaSavingIds = emptySet(), mediaSavedIds = emptySet(), mediaSaveFailedIds = emptySet()
        )
    }

    private fun showHomePane(people: Boolean) {
        accessRequestSerial++
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
        mutable.value = mutable.value.copy(showPeople = people, showChannels = !people, showTrusted = false, desk = mutable.value.space?.desk, spacePanel = null, access = null, accessApproval = null,
            forms = emptyList(), homework = emptyList(), completions = emptyMap(), responses = emptyMap(), composeContext = null,
            subjectLesson = null, subjectHomework = emptyList(), subjectDetail = null, showSubjectTasks = false,
            activeConversationId = null, activeTopicId = null, activeArchivedTopic=null, activeChannelKind = "chat", canPost = true, direct = false,
            messages = emptyList(), hasMore = false, olderLoading = false,
            board = null, ballotRefreshFailed = false, draft = "", replyTo = null, editing = null, sending = false,
            chatLoading = false, channelBusy = false, attachmentPending = false,
            mediaLoadingId = null, mediaError = false, mediaFiles = emptyMap(),
            mediaLoadingIds = emptySet(), mediaFailedIds = emptySet(), mediaSavingIds = emptySet(),
            mediaSavedIds = emptySet(), mediaSaveFailedIds = emptySet())
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
    private fun chat(item: Conversation) = GroupChatUi(item.conversationId, item.title, item.lastBody ?: "", item.unread, item.peerUserId)
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
                val next = GroupHold.perform(message, action, id, token, port, GroupHoldState(mutable.value.draft, mutable.value.replyTo, mutable.value.editing), GroupActions.canModerate(mutable.value))
                if (!current(ticket, id)) return@launch
                if (mutable.value.replyTo == null && mutable.value.editing == null &&
                    (next.replyTo != null || next.editing != null)) draftKey?.let { plainDrafts[it] = mutable.value.draft }
                val messages = if (next.removed) mutable.value.messages.map { if (it.id == messageId) it.copy(deleted = true, body = "", reactions = emptyList()) else it } else mutable.value.messages
                mutable.value = mutable.value.copy(draft = next.draft, replyTo = next.replyTo, editing = next.editing, messages = messages)
                rememberDraft()
                if (next.replyTo != null || next.editing != null) draftKey?.let(::bumpDraftVersion)
            } catch (e: CancellationException) {
                throw e
            } catch (e: CommunityClientException) {
                reconcileForbidden(e,ticket)
                if (current(ticket,id)) mutable.value = mutable.value.copy(failed=true)
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
            } catch (e: CommunityClientException) {
                reconcileForbidden(e,ticket)
                if (current(ticket,id)) mutable.value = mutable.value.copy(failed=true)
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
        fun factory(container: AppContainer, initialCommunityId: String? = null, initialConversationId: String? = null, initialContext: String? = null) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                GroupViewModel(container, initialCommunityId, initialConversationId, initialContext) as T
        }
    }
}
