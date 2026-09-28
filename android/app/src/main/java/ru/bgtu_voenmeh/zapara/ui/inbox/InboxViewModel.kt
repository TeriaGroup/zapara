package ru.bgtu_voenmeh.zapara.ui.inbox

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.api.UrlConnectionTransport
import ru.bgtu_voenmeh.zapara.data.social.*
import java.io.File
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

data class InboxUiState(val guest: Boolean = false, val loading: Boolean = false, val error: String? = null, val rows: List<InboxRow> = emptyList(), val code: String = "", val incoming: List<SocialInvite> = emptyList(), val outgoing: List<SocialInvite> = emptyList(), val active: InboxRow? = null, val messages: List<SocialMessage> = emptyList(), val hasMore: Boolean = false, val composer: PersonalComposer = PersonalComposer(), val inviteCode: String = "", val userId: String = "", val mediaFiles: Map<String, File> = emptyMap(), val mediaLoading: Set<String> = emptySet(), val mediaErrors: Set<String> = emptySet(), val sending: Boolean = false, val historyLoaded: Boolean = false, val inboxLoaded: Boolean = false) {
    val draft get() = composer.text
    val reply get() = composer.reply
    val editing get() = composer.editing
}
sealed interface InboxEvent {
    data class Upload(val conversationId: String, val uri: android.net.Uri) : InboxEvent
    data class UploadRecorded(val conversationId: String, val kind: String, val file: File, val durationMs: Int) : InboxEvent
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
    data class Reply(val message: SocialMessage) : InboxEvent
    data class Edit(val message: SocialMessage) : InboxEvent
    data class Delete(val message: SocialMessage) : InboxEvent
    data class React(val message: SocialMessage, val emoji: String) : InboxEvent
}
class InboxViewModel(private val container: AppContainer) : ViewModel() {
    private val social = container.communities?.let { SocialHttpClient(UrlConnectionTransport(), it.scope) }
    private val mutable = MutableStateFlow(InboxUiState(guest = container.profile.isGuest, userId = container.profile.userId.orEmpty()))
    val state: StateFlow<InboxUiState> = mutable.asStateFlow()
    private var operation: Job? = null
    private val composers = PersonalComposerSessions()
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
            is InboxEvent.UploadRecorded -> {
                if (state.value.active?.id != event.conversationId) {
                    event.file.delete()
                } else if (state.value.guest || operation?.isActive == true || state.value.sending) {
                    event.file.delete()
                    mutable.update { it.copy(error = container.app.getString(R.string.face_wait_send)) }
                } else execute(event)
            }
            is InboxEvent.Draft -> updateComposer { it.type(event.value) }
            is InboxEvent.Code -> mutable.update { it.copy(inviteCode = event.value.take(64)) }
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
        mutable.update { it.copy(composer = next) }
    }
    private fun navigate(row: InboxRow?) {
        generation++
        operation?.cancel()
        operation = null
        cancelMediaLoads()
        mutable.update { it.copy(active = row, messages = emptyList(), hasMore = false,
            composer = row?.id?.let(composers::restore) ?: PersonalComposer(), error = null,
            historyLoaded = false, loading = false, mediaLoading = emptySet()) }
        onEvent(InboxEvent.Refresh)
    }
    private fun execute(event: InboxEvent) {
        val composing = event == InboxEvent.Send || event is InboxEvent.Upload || event is InboxEvent.UploadRecorded
        if (state.value.guest || operation?.isActive == true || (composing && state.value.sending)) return
        if (event == InboxEvent.Send && (!state.value.composer.canSend || state.value.active == null)) return
        val started = generation
        val snapshot = state.value
        if (composing) {
            updateComposer { it.copy(error = null) }
            mutable.update { it.copy(sending = true) }
        } else mutable.update { it.copy(loading = true, error = null) }
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
                            container.app.contentResolver.openOutputStream(event.uri)?.use { it.write(bytes) } ?: error(container.app.getString(R.string.face_save_failed))
                        }
                        is InboxEvent.UploadRecorded -> snapshot.active?.takeIf { it.id == event.conversationId }?.let { row ->
                            try {
                                val cap = if (event.kind == "voice") 2 * 1024 * 1024 else 8 * 1024 * 1024
                                require(event.file.length() in 1..cap.toLong())
                                val bytes = event.file.readBytes()
                                val result = api.uploadRecording(token, row.id, event.kind, bytes, event.durationMs, snapshot.reply?.id)
                                accepted = true
                                acknowledge(row.id, result, snapshot.composer, attachment = true)
                            } finally { event.file.delete() }
                        }
                        InboxEvent.Invite -> {
                            api.invite(token, snapshot.inviteCode)
                            withContext(Dispatchers.Main.immediate) {
                                if (generation == started) mutable.update { current ->
                                    if (current.inviteCode == snapshot.inviteCode) current.copy(inviteCode = "") else current
                                }
                            }
                        }
                        is InboxEvent.Respond -> api.respond(token, event.id, event.accept)
                        InboxEvent.Send -> snapshot.active?.let { row ->
                            val result = snapshot.editing?.let { api.edit(token, row.id, it.id, snapshot.composer.sendText) } ?: api.send(token, row.id, snapshot.composer.sendText, snapshot.reply?.id)
                            accepted = true
                            acknowledge(row.id, result, snapshot.composer)
                        }
                        is InboxEvent.Delete -> snapshot.active?.let { publishMessage(started, it.id, api.delete(token, it.id, event.message.id)) }
                        is InboxEvent.React -> snapshot.active?.let { publishMessage(started, it.id, api.react(token, it.id, event.message.id, event.emoji)) }
                        else -> Unit
                    }
                    if (snapshot.active != null) {
                        val historyVersion = historyVersions.current(snapshot.active.id)
                        val page = if (event == InboxEvent.Older) api.messages(token, snapshot.active.id, snapshot.messages.firstOrNull()?.id)
                        else loadSocialUpdates(snapshot.messages.map { it.id }.toSet()) { before -> api.messages(token, snapshot.active.id, before) }
                        withContext(Dispatchers.Main.immediate) {
                            if (generation == started) mutable.update { current ->
                                if (current.active?.id == snapshot.active.id && historyVersions.isCurrent(snapshot.active.id, historyVersion)) current.copy(
                                    messages = if (event == InboxEvent.Older) merge(page.messages, current.messages) else merge(current.messages, page.messages),
                                    hasMore = if (event == InboxEvent.Older || snapshot.messages.isEmpty()) page.hasMore else current.hasMore,
                                    historyLoaded = true)
                                else current
                            }
                        }
                    } else {
                        var partial = false
                        val home = try { api.home(token) } catch (cancel: CancellationException) { throw cancel } catch (_: Exception) { partial = true; null }
                        val groupRows = mutableListOf<InboxRow>()
                        try {
                            container.communities?.list(token)?.filter { it.role != null }?.forEach { community ->
                                try { groupRows += groupInboxRows(container.communities.groupHome(token, community.communityId)) }
                                catch (cancel: CancellationException) { throw cancel }
                                catch (_: Exception) { partial = true; groupRows += state.value.rows.filter { it.communityId == community.communityId } }
                            }
                        } catch (cancel: CancellationException) { throw cancel } catch (_: Exception) { partial = true; groupRows += state.value.rows.filter { it.communityId != null } }
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
            } catch (cancel: CancellationException) { throw cancel }
            catch (e: Exception) {
                val message = when {
                e is SocialFailure && e.status == 401 -> container.app.getString(R.string.face_session_expired)
                e is SocialFailure && e.status == 413 -> container.app.getString(R.string.face_attachment_too_large)
                composing && !accepted -> container.app.getString(if (snapshot.editing != null) R.string.personal_edit_failed else R.string.personal_send_uncertain)
                event is InboxEvent.UploadRecorded -> container.app.getString(R.string.face_record_failed)
                else -> container.app.getString(R.string.face_chats_failed)
                }
                if (composing && !accepted && snapshot.active != null) {
                    val id = snapshot.active.id
                    val current = composers.restore(id)
                    val next = current.copy(error = message)
                    composers.save(id, next)
                    mutable.update { if (it.active?.id == id) it.copy(composer = next) else it }
                } else if (generation == started) mutable.update { it.copy(error = message) }
            }
            finally {
                if (event is InboxEvent.UploadRecorded) event.file.delete()
                if (composing) mutable.update { it.copy(sending = false) }
                else if (generation == started) mutable.update { it.copy(loading = false) }
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
        mutable.update { if (it.active?.id == conversationId) it.copy(composer = next,
            messages = merge(it.messages, listOf(message))) else it }
    }
    private suspend fun publishMessage(started: Int, conversationId: String, message: SocialMessage,
        amend: (InboxUiState) -> InboxUiState = { it }) = withContext(Dispatchers.Main.immediate) {
        historyVersions.acknowledge(conversationId)
        if (generation == started) mutable.update { current ->
            if (current.active?.id == conversationId) amend(current.copy(messages = merge(current.messages, listOf(message))))
            else current
        }
    }
    private fun merge(a: List<SocialMessage>, b: List<SocialMessage>) = (a + b).associateBy { it.id }.values.sortedBy { it.createdAt }
    private fun loadMedia(message: SocialMessage) {
        val attachment = message.attachmentId ?: return
        if (message.deleted || message.kind !in setOf("image", "voice", "circle")) return
        if (state.value.mediaFiles[attachment]?.isFile == true || mediaJobs[attachment]?.isActive == true) return
        val api = social ?: return
        mutable.update { it.copy(mediaLoading = it.mediaLoading + attachment, mediaErrors = it.mediaErrors - attachment) }
        mediaJobs[attachment] = viewModelScope.launch(Dispatchers.IO) {
            try {
                val token = container.accessToken() ?: throw SocialFailure(401)
                val bytes = api.download(token, attachment)
                ensureActive()
                val cap = when (message.kind) { "voice" -> 2 * 1024 * 1024; "circle" -> 8 * 1024 * 1024; else -> 20 * 1024 * 1024 }
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
                    if (!temporary.renameTo(destination)) error(container.app.getString(R.string.face_save_attachment_failed))
                } finally { temporary.delete() }
                withContext(Dispatchers.Main.immediate) {
                    mutable.update { current ->
                        val next = current.mediaFiles + (attachment to destination)
                        val ordered = next.entries.sortedByDescending { it.value.lastModified() }
                        var total = 0L
                        val kept = ordered.filter { entry ->
                            val keep = total + entry.value.length() <= 64L * 1024 * 1024 && total >= 0 && ordered.indexOf(entry) < 32
                            if (keep) total += entry.value.length() else entry.value.delete()
                            keep
                        }.associate { it.key to it.value }
                        current.copy(mediaFiles = kept, mediaLoading = current.mediaLoading - attachment, mediaErrors = current.mediaErrors - attachment)
                    }
                }
            } catch (cancel: CancellationException) { throw cancel }
            catch (_: Exception) { mutable.update { it.copy(mediaLoading = it.mediaLoading - attachment, mediaErrors = it.mediaErrors + attachment) } }
            finally { coroutineContext[Job]?.let { mediaJobs.remove(attachment, it) } }
        }
    }
    private fun cancelMediaLoads() {
        mediaJobs.values.forEach { it.cancel() }
        mediaJobs.clear()
    }
    override fun onCleared() {
        composers.clear()
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
