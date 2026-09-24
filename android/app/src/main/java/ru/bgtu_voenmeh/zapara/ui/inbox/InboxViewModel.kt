package ru.bgtu_voenmeh.zapara.ui.inbox

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.api.UrlConnectionTransport
import ru.bgtu_voenmeh.zapara.data.social.*

data class InboxUiState(val guest: Boolean = false, val loading: Boolean = false, val error: String? = null, val rows: List<InboxRow> = emptyList(), val code: String = "", val incoming: List<SocialInvite> = emptyList(), val outgoing: List<SocialInvite> = emptyList(), val active: InboxRow? = null, val messages: List<SocialMessage> = emptyList(), val hasMore: Boolean = false, val draft: String = "", val inviteCode: String = "", val reply: SocialMessage? = null, val editing: SocialMessage? = null, val userId: String = "")
sealed interface InboxEvent {
    data class Upload(val uri: android.net.Uri) : InboxEvent
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
    private var generation = 0
    init { onEvent(InboxEvent.Refresh) }
    fun onEvent(event: InboxEvent) {
        when(event) {
            is InboxEvent.Draft -> mutable.update { it.copy(draft = event.value.take(4000)) }
            is InboxEvent.Code -> mutable.update { it.copy(inviteCode = event.value.take(64)) }
            is InboxEvent.Reply -> mutable.update { it.copy(reply = event.message, editing = null) }
            is InboxEvent.Edit -> mutable.update { it.copy(editing = event.message, reply = null, draft = event.message.body.orEmpty()) }
            InboxEvent.CancelCompose -> mutable.update { it.copy(editing = null, reply = null, draft = if (it.editing != null) "" else it.draft) }
            InboxEvent.Back -> { generation++; operation?.cancel(); mutable.update { it.copy(active = null, messages = emptyList(), draft = "", reply = null, editing = null, loading = false) }; onEvent(InboxEvent.Refresh) }
            is InboxEvent.Open -> { generation++; operation?.cancel(); mutable.update { it.copy(active = event.row, messages = emptyList(), draft = "", reply = null, editing = null, loading = false) }; onEvent(InboxEvent.Refresh) }
            else -> execute(event)
        }
    }
    private fun execute(event: InboxEvent) {
        if (state.value.guest || operation?.isActive == true) return
        val started = generation
        val snapshot = state.value
        operation = viewModelScope.launch {
            mutable.update { it.copy(loading = true, error = null) }
            try {
                val api = social ?: error("Нет подключения")
                withContext(Dispatchers.IO) {
                    val token = container.accessToken() ?: throw SocialFailure(401)
                    when(event) {
                        is InboxEvent.Upload -> snapshot.active?.let { row ->
                            val resolver = container.app.contentResolver
                            val name = resolver.query(event.uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor -> if (cursor.moveToFirst()) cursor.getString(0) else null } ?: "document"
                            val bytes = resolver.openInputStream(event.uri)?.use { input ->
                                val out = java.io.ByteArrayOutputStream()
                                val buffer = ByteArray(8192)
                                while (true) { val count = input.read(buffer); if (count < 0) break; require(out.size() + count <= 20 * 1024 * 1024); out.write(buffer, 0, count) }
                                out.toByteArray()
                            } ?: error("Файл недоступен")
                            publishMessage(started, row.id, api.upload(token, row.id, name, bytes, resolver.getType(event.uri)?.startsWith("image/") == true, snapshot.reply?.id)) {
                                it.copy(reply = if (it.reply?.id == snapshot.reply?.id) null else it.reply)
                            }
                        }
                        is InboxEvent.Save -> {
                            val bytes = api.download(token, event.message.attachmentId ?: error("Нет вложения"))
                            container.app.contentResolver.openOutputStream(event.uri)?.use { it.write(bytes) } ?: error("Не удалось сохранить")
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
                            val result = snapshot.editing?.let { api.edit(token, row.id, it.id, snapshot.draft) } ?: api.send(token, row.id, snapshot.draft, snapshot.reply?.id)
                            publishMessage(started, row.id, result) { current ->
                                if (current.draft == snapshot.draft && current.reply?.id == snapshot.reply?.id && current.editing?.id == snapshot.editing?.id)
                                    current.copy(draft = "", reply = null, editing = null)
                                else current
                            }
                        }
                        is InboxEvent.Delete -> snapshot.active?.let { publishMessage(started, it.id, api.delete(token, it.id, event.message.id)) }
                        is InboxEvent.React -> snapshot.active?.let { publishMessage(started, it.id, api.react(token, it.id, event.message.id, event.emoji)) }
                        else -> Unit
                    }
                    if (snapshot.active != null) {
                        val page = if (event == InboxEvent.Older) api.messages(token, snapshot.active.id, snapshot.messages.firstOrNull()?.id)
                        else loadSocialUpdates(snapshot.messages.map { it.id }.toSet()) { before -> api.messages(token, snapshot.active.id, before) }
                        withContext(Dispatchers.Main.immediate) {
                            if (generation == started) mutable.update { current ->
                                if (current.active?.id == snapshot.active.id) current.copy(
                                    messages = if (event == InboxEvent.Older) merge(page.messages, current.messages) else merge(current.messages, page.messages),
                                    hasMore = if (event == InboxEvent.Older || snapshot.messages.isEmpty()) page.hasMore else current.hasMore)
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
                                    outgoing = home?.outgoing ?: current.outgoing,
                                    error = if (partial) "Часть чатов недоступна. Обновите список позже." else null)
                            }
                        }
                    }
                }
            } catch (cancel: CancellationException) { throw cancel }
            catch (e: Exception) { if (generation == started) mutable.update { it.copy(error = if (e is SocialFailure && e.status == 401) "Сессия истекла. Войдите в аккаунт снова." else "Не удалось обновить чаты. Проверьте подключение и повторите.") } }
            finally { if (generation == started) mutable.update { it.copy(loading = false) } }
        }
    }
    private suspend fun publishMessage(started: Int, conversationId: String, message: SocialMessage,
        amend: (InboxUiState) -> InboxUiState = { it }) = withContext(Dispatchers.Main.immediate) {
        if (generation == started) mutable.update { current ->
            if (current.active?.id == conversationId) amend(current.copy(messages = merge(current.messages, listOf(message))))
            else current
        }
    }
    private fun merge(a: List<SocialMessage>, b: List<SocialMessage>) = (a + b).associateBy { it.id }.values.sortedBy { it.createdAt }
    companion object {
        fun factory(container: AppContainer): ViewModelProvider.Factory = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST") override fun <T : ViewModel> create(modelClass: Class<T>): T = InboxViewModel(container) as T
        }
    }
}
