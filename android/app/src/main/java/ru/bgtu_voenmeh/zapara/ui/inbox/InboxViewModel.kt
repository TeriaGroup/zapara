package ru.bgtu_voenmeh.zapara.ui.inbox

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ToastKind
import ru.bgtu_voenmeh.zapara.data.api.UrlConnectionTransport
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException
import ru.bgtu_voenmeh.zapara.data.social.*
import java.io.File
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

data class InboxUiState(val guest: Boolean = false, val loading: Boolean = false, val error: String? = null, val rows: List<InboxRow> = emptyList(), val code: String = "", val incoming: List<SocialInvite> = emptyList(), val outgoing: List<SocialInvite> = emptyList(), val active: InboxRow? = null, val messages: List<SocialMessage> = emptyList(), val hasMore: Boolean = false, val composer: PersonalComposer = PersonalComposer(), val inviteCode: String = "", val userId: String = "", val mediaFiles: Map<String, File> = emptyMap(), val mediaLoading: Set<String> = emptySet(), val mediaErrors: Set<String> = emptySet(), val sending: Boolean = false, val historyLoaded: Boolean = false, val inboxLoaded: Boolean = false,
    val respondingId: String? = null, val draftPreviews: Map<String, String> = emptyMap(),
    val profileDatabaseName: String = "", val pendingRecording: PendingPersonalRecording? = null,
    val mediaNotFound: Set<String> = emptySet()) {
    val draft get() = composer.text
    val reply get() = composer.reply
    val editing get() = composer.editing
    internal fun withoutMediaStatus() = copy(mediaLoading = emptySet(), mediaErrors = emptySet(),
        mediaNotFound = emptySet())
}
sealed interface InboxEvent {
    data class Upload(val conversationId: String, val uri: android.net.Uri) : InboxEvent
    data class UploadRecorded(val scope: PersonalRecordingScope, val kind: String, val file: File, val durationMs: Int) : InboxEvent
    data class RetryRecording(val scope: PersonalRecordingScope) : InboxEvent
    data class DiscardRecording(val scope: PersonalRecordingScope, val file: File) : InboxEvent
    data class LoadMedia(val message: SocialMessage) : InboxEvent
    data class LocalError(val message: String) : InboxEvent
    data class Save(val message: SocialMessage, val uri: android.net.Uri) : InboxEvent
    data object Refresh : InboxEvent
    data object Back : InboxEvent
    data object Older : InboxEvent
    data object Send : InboxEvent
    data object Invite : InboxEvent
    data object CancelCompose : InboxEvent
    data class Open(val row: InboxRow) : InboxEvent
    data class Draft(val value: String) : InboxEvent
    data class Code(val value: String) : InboxEvent
    data class Respond(val id: String, val accept: Boolean) : InboxEvent
    data object CopyCode : InboxEvent
    data class CopyMessage(val id: String) : InboxEvent
    data class Reply(val message: SocialMessage) : InboxEvent
    data class Edit(val message: SocialMessage) : InboxEvent
    data class Delete(val message: SocialMessage) : InboxEvent
    data class React(val message: SocialMessage, val emoji: String) : InboxEvent
}
class InboxViewModel(private val container: AppContainer) : ViewModel() {
    private val social = container.communities?.let { SocialHttpClient(UrlConnectionTransport(), it.scope) }
    private val mutable = MutableStateFlow(InboxUiState(guest = container.profile.isGuest,
        userId = container.profile.userId.orEmpty(), profileDatabaseName = container.profile.databaseName))
    val state: StateFlow<InboxUiState> = mutable.asStateFlow()
    private var operation: Job? = null
    private val composers = PersonalComposerSessions()
    private val pendingRecordings = PendingPersonalRecordingStore()
    private val historyVersions = PersonalHistoryVersions()
    private val mediaJobs = ConcurrentHashMap<String, Job>()
    private val mediaDirectory = File(container.app.cacheDir, "personal-chat-${UUID.randomUUID()}")
    private var generation = 0
    init { onEvent(InboxEvent.Refresh) }
    fun onEvent(event: InboxEvent) {
        when(event) {
            is InboxEvent.LocalError -> updateComposer { it.copy(error = event.message) }
            is InboxEvent.LoadMedia -> loadMedia(event.message)
            is InboxEvent.Upload -> if (state.value.active?.id == event.conversationId) execute(event)
            is InboxEvent.UploadRecorded -> receiveRecorded(event)
            is InboxEvent.RetryRecording -> retryRecorded(event.scope)
            is InboxEvent.DiscardRecording -> discardRecorded(event.scope, event.file)
            is InboxEvent.Draft -> updateComposer { it.type(event.value) }
            is InboxEvent.Code -> mutable.update { it.copy(inviteCode = event.value.take(64)) }
            InboxEvent.CopyCode -> copyCode()
            is InboxEvent.CopyMessage -> copyMessage(event.id)
            is InboxEvent.Respond -> {
                if (state.value.respondingId == null && state.value.incoming.any { it.id == event.id })
                    execute(event)
            }
            is InboxEvent.Reply -> updateComposer { it.replyTo(event.message) }
            is InboxEvent.Edit -> updateComposer { it.edit(event.message) }
            InboxEvent.CancelCompose -> updateComposer { it.cancel() }
            InboxEvent.Back -> navigate(null)
            is InboxEvent.Open -> navigate(event.row)
            else -> execute(event)
        }
    }
    private fun updateComposer(change: (PersonalComposer) -> PersonalComposer) {
        val current = state.value
        val id = current.active?.id ?: return
        val next = change(current.composer)
        composers.save(id, next)
        mutable.update { it.copy(composer = next, draftPreviews = composers.draftPreviews()) }
    }

    private fun recordingScope(conversationId: String) = PersonalRecordingScope(
        container.profile.userId.orEmpty(), container.profile.databaseName, conversationId)

    private fun recordingScopeMatches(scope: PersonalRecordingScope, current: InboxUiState = state.value): Boolean =
        !current.guest && scope.profileId == container.profile.userId.orEmpty() &&
            scope.databaseName == container.profile.databaseName && current.userId == scope.profileId &&
            current.profileDatabaseName == scope.databaseName && current.active?.id == scope.conversationId

    private fun receiveRecorded(event: InboxEvent.UploadRecorded) {
        val current = state.value
        if (!recordingScopeMatches(event.scope, current) || current.sending) {
            event.file.delete()
            if (current.active?.id == event.scope.conversationId && current.sending)
                updateComposer { it.copy(error = container.app.getString(R.string.face_wait_send)) }
            return
        }
        if (pendingRecordings.get(event.scope) != null || !pendingRecordings.begin(
                PendingPersonalRecording(event.scope, event.kind, event.file, event.durationMs))) {
            event.file.delete()
            updateComposer { it.copy(error = container.app.getString(R.string.ux60_chat_recording_pending)) }
            return
        }
        mutable.update { currentState ->
            if (recordingScopeMatches(event.scope, currentState))
                currentState.copy(pendingRecording = pendingRecordings.get(event.scope)) else currentState
        }
        execute(event, recordingPrepared = true)
    }

    private fun retryRecorded(scope: PersonalRecordingScope) {
        val current = state.value
        if (!recordingScopeMatches(scope, current) || current.sending) return
        val pending = pendingRecordings.retry(scope) ?: return
        mutable.update { state ->
            if (recordingScopeMatches(scope, state)) state.copy(pendingRecording = pending) else state
        }
        execute(InboxEvent.UploadRecorded(scope, pending.kind, pending.file, pending.durationMs), recordingPrepared = true)
    }

    private fun discardRecorded(scope: PersonalRecordingScope, file: File) {
        val current = state.value
        if (!recordingScopeMatches(scope, current) || current.sending ||
            !pendingRecordings.discard(scope, file)) return
        mutable.update { state ->
            if (recordingScopeMatches(scope, state)) state.copy(pendingRecording = null,
                composer = state.composer.copy(error = null)) else state
        }
    }
    private fun navigate(row: InboxRow?) {
        generation++
        operation?.cancel()
        operation = null
        cancelMediaLoads()
        mutable.update { it.withoutMediaStatus().copy(active = row, messages = emptyList(), hasMore = false,
            composer = row?.id?.let(composers::restore) ?: PersonalComposer(), error = null,
            historyLoaded = false, loading = false, respondingId = null,
            userId = container.profile.userId.orEmpty(), profileDatabaseName = container.profile.databaseName,
            pendingRecording = row?.id?.let { pendingRecordings.get(recordingScope(it)) },
            draftPreviews = composers.draftPreviews()) }
        onEvent(InboxEvent.Refresh)
    }
    private fun execute(event: InboxEvent, recordingPrepared: Boolean = false) {
        val composing = event == InboxEvent.Send || event is InboxEvent.Upload || event is InboxEvent.UploadRecorded
        if (state.value.guest || !canStartPersonalOperation(composing, operation?.isActive == true, state.value.sending)) {
            if (recordingPrepared && event is InboxEvent.UploadRecorded) {
                pendingRecordings.markUncertain(event.scope, event.file)
                mutable.update { current -> if (recordingScopeMatches(event.scope, current))
                    current.copy(pendingRecording = pendingRecordings.get(event.scope)) else current }
            }
            return
        }
        if (event == InboxEvent.Send && (!state.value.composer.canSend || state.value.active == null)) return
        val started = generation
        val snapshot = state.value
        if (composing) {
            updateComposer { it.copy(error = null) }
            mutable.update { it.copy(sending = true) }
        } else mutable.update { it.copy(loading = true, error = null,
            respondingId = if (event is InboxEvent.Respond) event.id else it.respondingId) }
        val job = viewModelScope.launch(start = CoroutineStart.LAZY) {
            var accepted = false
            try {
                val api = social ?: error(container.app.getString(R.string.face_no_connection))
                withContext(Dispatchers.IO) {
                    val token = container.accessToken() ?: throw SocialFailure(401)
                    when(event) {
                        is InboxEvent.Upload -> snapshot.active?.takeIf { it.id == event.conversationId }?.let { row ->
                            val resolver = container.app.contentResolver
                            val name = resolver.query(event.uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor -> if (cursor.moveToFirst()) cursor.getString(0) else null } ?: "document"
                            val bytes = resolver.openInputStream(event.uri)?.use { input ->
                                val out = java.io.ByteArrayOutputStream()
                                val buffer = ByteArray(8192)
                                while (true) { val count = input.read(buffer); if (count < 0) break; require(out.size() + count <= 20 * 1024 * 1024); out.write(buffer, 0, count) }
                                out.toByteArray()
                            } ?: error(container.app.getString(R.string.face_file_unavailable))
                            val result = api.upload(token, row.id, name, bytes, resolver.getType(event.uri)?.startsWith("image/") == true, snapshot.reply?.id)
                            accepted = true
                            acknowledge(row.id, result, snapshot.composer, attachment = true)
                        }
                        is InboxEvent.Save -> {
                            val bytes = api.download(token, event.message.attachmentId ?: error(container.app.getString(R.string.face_no_attachment)))
                            container.app.contentResolver.openOutputStream(event.uri, "wt")?.use { it.write(bytes) } ?: error(container.app.getString(R.string.face_save_failed))
                        }
                        is InboxEvent.UploadRecorded -> snapshot.active?.takeIf {
                            it.id == event.scope.conversationId && recordingScopeMatches(event.scope)
                        }?.let { row ->
                            val cap = if (event.kind == "voice") 4 * 1024 * 1024 else 24 * 1024 * 1024
                            require(event.file.isFile && event.file.length() in 1..cap.toLong())
                            val bytes = event.file.readBytes()
                            val result = api.uploadRecording(token, row.id, event.kind, bytes, event.durationMs, snapshot.reply?.id)
                            accepted = true
                            acknowledge(row.id, result, snapshot.composer, attachment = true)
                        }
                        InboxEvent.Invite -> {
                            api.invite(token, snapshot.inviteCode)
                            withContext(Dispatchers.Main.immediate) {
                                if (generation == started) mutable.update { current ->
                                    if (current.inviteCode == snapshot.inviteCode) current.copy(inviteCode = "") else current
                                }
                            }
                        }
                        is InboxEvent.Respond -> {
                            api.respond(token, event.id, event.accept)
                            withContext(Dispatchers.Main.immediate) {
                                if (generation == started) mutable.update { current ->
                                    current.copy(incoming = current.incoming.filterNot { it.id == event.id })
                                }
                            }
                        }
                        InboxEvent.Send -> snapshot.active?.let { row ->
                            val result = snapshot.editing?.let { api.edit(token, row.id, it.id, snapshot.composer.sendText) } ?: api.send(token, row.id, snapshot.composer.sendText, snapshot.reply?.id)
                            accepted = true
                            acknowledge(row.id, result, snapshot.composer)
                        }
                        is InboxEvent.Delete -> snapshot.active?.let { publishMessage(started, it.id, api.delete(token, it.id, event.message.id)) }
                        is InboxEvent.React -> snapshot.active?.let { publishMessage(started, it.id, api.react(token, it.id, event.message.id, event.emoji), reactionOnly = true) }
                        else -> Unit
                    }
                    if (snapshot.active != null) {
                        val historyVersion = historyVersions.current(snapshot.active.id)
                        val page = if (event == InboxEvent.Older) api.messages(token, snapshot.active.id, snapshot.messages.firstOrNull()?.id)
                        else loadSocialUpdates(snapshot.messages.map { it.id }.toSet()) { before -> api.messages(token, snapshot.active.id, before) }
                        withContext(Dispatchers.Main.immediate) {
                            if (generation == started) mutable.update { current ->
                                if (current.active?.id == snapshot.active.id) current.copy(
                                    messages = mergePersonalHistory(current.messages, page.messages,
                                        event != InboxEvent.Older && historyVersions.isCurrent(snapshot.active.id, historyVersion)),
                                    hasMore = if (event == InboxEvent.Older || snapshot.messages.isEmpty()) page.hasMore else current.hasMore,
                                    historyLoaded = true)
                                else current
                            }
                        }
                        if (event != InboxEvent.Older) {
                            val fresh = try { api.home(token) } catch (cancel: CancellationException) { throw cancel } catch (_: Exception) { null }
                            fresh?.let { home -> withContext(Dispatchers.Main.immediate) {
                                if (generation == started) mutable.update { current ->
                                    current.copy(active = home.friends.firstOrNull { it.id == current.active?.id } ?: current.active,
                                        rows = orderInbox(home.friends + current.rows.filter { it.communityId != null }),
                                        code = home.code, incoming = home.incoming, outgoing = home.outgoing)
                                }
                            } }
                        }
                    } else {
                        var partial = false
                        val home = try { api.home(token) } catch (cancel: CancellationException) { throw cancel } catch (e: Exception) {
                            runCatching { android.util.Log.w("ZaparaInbox", "phase=social_home category=${e.javaClass.simpleName}") }
                            partial = true; null
                        }
                        val groupRows = mutableListOf<InboxRow>()
                        try {
                            container.communities?.list(token)?.filter { it.role != null }?.forEach { community ->
                                try { groupRows += groupInboxRows(container.communities.groupHome(token, community.communityId)) }
                                catch (cancel: CancellationException) { throw cancel }
                                catch (e: Exception) {
                                    runCatching { android.util.Log.w("ZaparaInbox", "phase=group_home category=${if (e is CommunityClientException) e.failure.name else e.javaClass.simpleName}") }
                                    partial = true; groupRows += state.value.rows.filter { it.communityId == community.communityId }
                                }
                            }
                        } catch (cancel: CancellationException) { throw cancel } catch (e: Exception) {
                            runCatching { android.util.Log.w("ZaparaInbox", "phase=community_list category=${if (e is CommunityClientException) e.failure.name else e.javaClass.simpleName}") }
                            partial = true; groupRows += state.value.rows.filter { it.communityId != null }
                        }
                        withContext(Dispatchers.Main.immediate) {
                            if (generation == started) mutable.update { current ->
                                if (current.active != null) current else current.copy(
                                    rows = orderInbox((home?.friends ?: current.rows.filter { row -> row.communityId == null }) + groupRows),
                                    code = home?.code ?: current.code, incoming = home?.incoming ?: current.incoming,
                                    outgoing = home?.outgoing ?: current.outgoing, inboxLoaded = current.inboxLoaded || !partial,
                                    error = if (partial) container.app.getString(R.string.face_chats_partial) else null)
                            }
                        }
                    }
                }
            } catch (cancel: CancellationException) {
                if (event is InboxEvent.UploadRecorded) pendingRecordings.removeCancelled(event.scope, event.file)
                throw cancel
            }
            catch (e: Exception) {
                if (event is InboxEvent.UploadRecorded && !accepted) {
                    pendingRecordings.markUncertain(event.scope, event.file)
                    mutable.update { current -> if (recordingScopeMatches(event.scope, current))
                        current.copy(pendingRecording = pendingRecordings.get(event.scope)) else current }
                }
                val message = when {
                e is SocialFailure && e.status == 401 -> container.app.getString(R.string.face_session_expired)
                e is SocialFailure && e.status == 413 -> container.app.getString(R.string.face_attachment_too_large)
                composing && !accepted -> container.app.getString(if (snapshot.editing != null) R.string.personal_edit_failed else R.string.personal_send_uncertain)
                event is InboxEvent.UploadRecorded -> container.app.getString(R.string.face_record_failed)
                else -> container.app.getString(R.string.face_chats_failed)
                }
                if (event is InboxEvent.UploadRecorded) {
                    // The retry card carries the explicit uncertain-outcome warning and actions.
                } else if (composing && !accepted && snapshot.active != null) {
                    val id = snapshot.active.id
                    val current = composers.restore(id)
                    val next = current.copy(error = message)
                    composers.save(id, next)
                    mutable.update { state ->
                        val previews = composers.draftPreviews()
                        if (state.active?.id == id) state.copy(composer = next, draftPreviews = previews)
                        else state.copy(draftPreviews = previews)
                    }
                } else if (generation == started) mutable.update { it.copy(error = message) }
            }
            finally {
                if (event is InboxEvent.UploadRecorded && accepted) {
                    pendingRecordings.removeAccepted(event.scope, event.file)
                    event.file.delete()
                    mutable.update { current -> if (current.pendingRecording?.let {
                        it.scope == event.scope && it.file == event.file
                    } == true) current.copy(pendingRecording = null) else current }
                }
                if (composing) mutable.update { it.copy(sending = false) }
                else if (generation == started) mutable.update { it.copy(loading = false,
                    respondingId = if (event is InboxEvent.Respond && it.respondingId == event.id) null else it.respondingId) }
            }
        }
        if (!composing) operation = job
        job.start()
    }
    private suspend fun acknowledge(conversationId: String, message: SocialMessage, sent: PersonalComposer,
        attachment: Boolean = false) = withContext(Dispatchers.Main.immediate) {
        historyVersions.acknowledge(conversationId)
        val current = composers.restore(conversationId)
        val next = if (attachment) current.acknowledgeAttachment(sent) else current.acknowledge(sent)
        composers.save(conversationId, next)
        mutable.update { state ->
            val previews = composers.draftPreviews()
            if (state.active?.id == conversationId) state.copy(composer = next, draftPreviews = previews,
                messages = mergePersonalHistory(state.messages, listOf(personalReceipt(state.messages.firstOrNull { row -> row.id == message.id },
                    message, editing = sent.editing != null)), true))
            else state.copy(draftPreviews = previews)
        }
    }
    private suspend fun publishMessage(started: Int, conversationId: String, message: SocialMessage,
        reactionOnly: Boolean = false) = withContext(Dispatchers.Main.immediate) {
        historyVersions.acknowledge(conversationId)
        if (generation == started) mutable.update { current ->
            if (current.active?.id == conversationId) current.copy(messages = mergePersonalHistory(current.messages,
                listOf(personalReceipt(current.messages.firstOrNull { it.id == message.id }, message, reactionOnly = reactionOnly)), true))
            else current
        }
    }
    private fun loadMedia(message: SocialMessage) {
        val attachment = message.attachmentId ?: return
        if (message.deleted || message.kind !in setOf("image", "voice", "circle")) return
        val requestedGeneration = generation
        val requestedConversation = state.value.active?.id ?: return
        if (!personalMediaRequestIsCurrent(state.value, requestedConversation, message)) return
        if (state.value.mediaFiles[attachment]?.isFile == true || mediaJobs[attachment]?.isActive == true) return
        val api = social ?: return
        mutable.update { it.copy(mediaLoading = it.mediaLoading + attachment, mediaErrors = it.mediaErrors - attachment,
            mediaNotFound = it.mediaNotFound - attachment) }
        mediaJobs[attachment] = viewModelScope.launch(Dispatchers.IO) {
            try {
                val token = container.accessToken() ?: throw SocialFailure(401)
                val bytes = api.download(token, attachment)
                ensureActive()
                if (!isMediaRequestCurrent(requestedGeneration, requestedConversation, message)) return@launch
                val cap = when (message.kind) { "voice" -> 4 * 1024 * 1024; "circle" -> 24 * 1024 * 1024; else -> 20 * 1024 * 1024 }
                require(bytes.isNotEmpty() && bytes.size <= cap)
                if (!mediaDirectory.isDirectory && !mediaDirectory.mkdirs()) error(container.app.getString(R.string.face_no_space))
                val suffix = when (message.kind) {
                    "voice" -> if (message.fileName?.endsWith(".webm", true) == true) ".webm" else if (message.fileName?.endsWith(".ogg", true) == true) ".ogg" else ".m4a"
                    "circle" -> if (message.fileName?.endsWith(".webm", true) == true) ".webm" else ".mp4"
                    else -> ".image"
                }
                val destination = File(mediaDirectory, "$attachment$suffix")
                val temporary = File.createTempFile("media-", ".tmp", mediaDirectory)
                try {
                    temporary.writeBytes(bytes)
                    ensureActive()
                    if (!isMediaRequestCurrent(requestedGeneration, requestedConversation, message)) return@launch
                    if (!temporary.renameTo(destination)) error(container.app.getString(R.string.face_save_attachment_failed))
                } finally { temporary.delete() }
                withContext(Dispatchers.Main.immediate) {
                    if (!isMediaRequestCurrent(requestedGeneration, requestedConversation, message)) {
                        destination.delete()
                        return@withContext
                    }
                    mutable.update { current ->
                        val next = current.mediaFiles + (attachment to destination)
                        val ordered = next.entries.sortedByDescending { it.value.lastModified() }
                        var total = 0L
                        val kept = ordered.filter { entry ->
                            val keep = total + entry.value.length() <= 64L * 1024 * 1024 && total >= 0 && ordered.indexOf(entry) < 32
                            if (keep) total += entry.value.length() else entry.value.delete()
                            keep
                        }.associate { it.key to it.value }
                        current.copy(mediaFiles = kept, mediaLoading = current.mediaLoading - attachment,
                            mediaErrors = current.mediaErrors - attachment, mediaNotFound = current.mediaNotFound - attachment)
                    }
                }
            } catch (cancel: CancellationException) { throw cancel }
            catch (e: Exception) {
                runCatching { android.util.Log.w("ZaparaInbox", "phase=media_load category=${when (e) {
                    is SocialFailure -> "SocialHttp${e.status}"
                    is CommunityClientException -> "Community${e.failure.name}"
                    else -> e.javaClass.simpleName
                }}") }
                if (isMediaRequestCurrent(requestedGeneration, requestedConversation, message))
                    mutable.update { personalMediaFailureState(it, requestedConversation, message, e) }
            }
            finally {
                if (isMediaRequestCurrent(requestedGeneration, requestedConversation, message))
                    mutable.update { it.copy(mediaLoading = it.mediaLoading - attachment) }
                coroutineContext[Job]?.let { mediaJobs.remove(attachment, it) }
            }
        }
    }
    private fun cancelMediaLoads() {
        mediaJobs.values.forEach { it.cancel() }
        mediaJobs.clear()
    }
    private fun isMediaRequestCurrent(requestedGeneration: Int, conversationId: String, message: SocialMessage): Boolean =
        generation == requestedGeneration && state.value.active?.id == conversationId &&
            personalMediaRequestIsCurrent(state.value, conversationId, message)

    private fun copyCode() {
        val current = mutable.value
        if (current.guest || current.userId != container.profile.userId.orEmpty() || current.code.isBlank()) {
            container.toasts.show(container.app.getString(R.string.ux30_copy_unavailable), ToastKind.Bad)
            return
        }
        copyText(current.code, container.app.getString(R.string.ux30_copy_code_label))
    }

    private fun copyMessage(id: String) {
        val current = mutable.value
        if (current.guest || current.active == null) return
        val content = copyablePersonalText(current.messages.firstOrNull { it.id == id })
        if (content == null) {
            container.toasts.show(container.app.getString(R.string.ux30_copy_unavailable), ToastKind.Bad)
            return
        }
        copyText(content, container.app.getString(R.string.ux30_copy_message_label))
    }

    private fun copyText(content: String, label: String) {
        try {
            val clipboard = container.app.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            clipboard.setPrimaryClip(ClipData.newPlainText(label, content))
            container.toasts.show(container.app.getString(R.string.ux30_copied), ToastKind.Ok)
        } catch (e: Exception) {
            android.util.Log.w("ZaparaInbox", "copy", e)
            container.toasts.show(container.app.getString(R.string.ux30_copy_failed), ToastKind.Bad)
        }
    }
    override fun onCleared() {
        composers.clear()
        pendingRecordings.clearAndDelete()
        cancelMediaLoads()
        mediaDirectory.deleteRecursively()
        super.onCleared()
    }
    companion object {
        fun factory(container: AppContainer): ViewModelProvider.Factory = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST") override fun <T : ViewModel> create(modelClass: Class<T>): T = InboxViewModel(container) as T
        }
    }
}
