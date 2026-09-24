@file:OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
package ru.bgtu_voenmeh.zapara.ui.inbox

import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.Lifecycle
import androidx.compose.ui.platform.LocalLifecycleOwner
import kotlinx.coroutines.delay
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun InboxSection(state: InboxUiState, onEvent: (InboxEvent) -> Unit, onOpenGroup: (communityId: String, conversationId: String) -> Unit, modifier: Modifier = Modifier) {
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val currentEvent by rememberUpdatedState(onEvent)
    LaunchedEffect(lifecycle, state.guest) {
        if (!state.guest) lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) { delay(15_000); currentEvent(InboxEvent.Refresh) }
        }
    }
    if (state.active != null) BackHandler { onEvent(InboxEvent.Back) }
    Column(modifier.fillMaxSize()) {
        ZTopBar(state.active?.title ?: "Чаты")
        if (state.guest) {
            Text("Войдите в аккаунт, чтобы общаться с группой и друзьями. Расписание и карты доступны без входа.", Modifier.padding(Zapara.space.l), color = Zapara.colors.text2)
        } else {
            Row(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l), verticalAlignment = Alignment.CenterVertically) {
                if (state.active != null) ZButton("К чатам", { onEvent(InboxEvent.Back) }, ghost = true)
                Spacer(Modifier.weight(1f))
                ZButton(if (state.loading) "Загрузка…" else "Обновить", { onEvent(InboxEvent.Refresh) }, enabled = !state.loading, ghost = true)
            }
            state.error?.let { Text(it, Modifier.padding(horizontal = Zapara.space.l, vertical = 8.dp).testTag("Inbox.Error"), color = Zapara.colors.bad) }
            if (state.active == null) InboxList(state, onEvent, onOpenGroup, Modifier.weight(1f))
            else PersonalChat(state, onEvent, Modifier.weight(1f))
        }
    }
}

@Composable
private fun InboxList(state: InboxUiState, onEvent: (InboxEvent) -> Unit, onOpenGroup: (communityId: String, conversationId: String) -> Unit, modifier: Modifier) {
    var adding by remember { mutableStateOf(false) }
    LazyColumn(modifier, contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item {
            ZButton(if (adding) "Закрыть приглашения" else "Новый личный чат", { adding = !adding }, modifier = Modifier.fillMaxWidth(), ghost = true)
        }
        if (adding) item {
            ZCard(modifier = Modifier.fillMaxWidth()) {
                SelectionContainer { Text("Ваш код: ${state.code.ifEmpty { "загружается…" }}", color = Zapara.colors.text1) }
                Text("Отправьте свой код другу или введите его код. Чат появится после принятия приглашения.", color = Zapara.colors.text2, style = Zapara.typography.caption)
                OutlinedTextField(state.inviteCode, { onEvent(InboxEvent.Code(it)) }, label = { Text("Код друга") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                ZButton("Пригласить", { onEvent(InboxEvent.Invite) }, enabled = !state.loading && state.inviteCode.isNotBlank())
                state.outgoing.forEach { Text("Приглашение отправлено: ${it.name}", color = Zapara.colors.text2) }
            }
        }
        items(state.incoming, key = { "invite:${it.id}" }) { invite ->
            ZCard(modifier = Modifier.fillMaxWidth()) {
                Text("${invite.name} приглашает в чат", color = Zapara.colors.text1)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    ZButton("Принять", { onEvent(InboxEvent.Respond(invite.id, true)) }, enabled = !state.loading)
                    ZButton("Отклонить", { onEvent(InboxEvent.Respond(invite.id, false)) }, enabled = !state.loading, ghost = true)
                }
            }
        }
        if (state.rows.isEmpty() && !state.loading) item { Text("Здесь появятся ваши группы и личные чаты. Начните общение по коду друга.", color = Zapara.colors.text2) }
        items(state.rows, key = { it.id }) { row ->
            ZCard(onClick = { row.communityId?.let { onOpenGroup(it, row.id) } ?: onEvent(InboxEvent.Open(row)) }, modifier = Modifier.fillMaxWidth(), tag = "Inbox.Chat.${row.id}") {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(row.title, Modifier.weight(1f), color = Zapara.colors.text1, style = Zapara.typography.bodyStrong, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    if (row.unread > 0) Text("${row.unread}", color = Zapara.colors.accent)
                }
                Text(row.subtitle, color = Zapara.colors.text2, style = Zapara.typography.caption)
                Text(row.lastBody ?: "Сообщений пока нет", color = Zapara.colors.text2, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
private fun PersonalChat(state: InboxUiState, onEvent: (InboxEvent) -> Unit, modifier: Modifier) {
    var saving by remember { mutableStateOf<SocialMessage?>(null) }
    val pick = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri -> if (uri != null) onEvent(InboxEvent.Upload(uri)) }
    val save = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/octet-stream")) { uri -> val message = saving; if (uri != null && message != null) onEvent(InboxEvent.Save(message, uri)); saving = null }
    var selected by remember(state.active?.id) { mutableStateOf<SocialMessage?>(null) }
    var deleting by remember { mutableStateOf<SocialMessage?>(null) }
    val list = rememberLazyListState()
    var lastId by remember(state.active?.id) { mutableStateOf<String?>(null) }
    LaunchedEffect(state.messages.lastOrNull()?.id) {
        val next = state.messages.lastOrNull()?.id
        if (next != null && next != lastId) list.animateScrollToItem((state.messages.size - 1 + if (state.hasMore) 1 else 0).coerceAtLeast(0))
        lastId = next
    }
    Column(modifier) {
        LazyColumn(Modifier.weight(1f).fillMaxWidth(), state = list, contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            if (state.hasMore) item { ZButton("Ранние сообщения", { onEvent(InboxEvent.Older) }, enabled = !state.loading, ghost = true) }
            if (state.messages.isEmpty() && !state.loading) item { Text("Напишите первое сообщение", color = Zapara.colors.text2) }
            items(state.messages, key = { it.id }) { message ->
                val mine = message.senderId == state.userId
                Row(Modifier.fillMaxWidth(), horizontalArrangement = if (mine) Arrangement.End else Arrangement.Start) {
                    Surface(color = if (mine) Zapara.colors.accent.copy(alpha = 0.12f) else Zapara.colors.surface, shape = androidx.compose.foundation.shape.RoundedCornerShape(16.dp), modifier = Modifier.widthIn(max = 320.dp).combinedClickable(onClick = {}, onLongClick = { if (!message.deleted) selected = message })) {
                        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Text(message.senderName, color = Zapara.colors.text2, style = Zapara.typography.caption)
                            message.replyBody?.let { Text("↳ $it", color = Zapara.colors.text2, maxLines = 2, overflow = TextOverflow.Ellipsis) }
                            SelectionContainer { Text(if (message.deleted) "Сообщение удалено" else message.body ?: message.fileName ?: when(message.kind) { "image" -> "Фото"; "voice" -> "Голосовое сообщение"; "circle" -> "Видеосообщение"; else -> "Вложение" }, color = Zapara.colors.text1) }
                            if (!message.deleted && message.attachmentId != null) TextButton({ saving = message; save.launch(message.fileName ?: "attachment") }, enabled = !state.loading) { Text("Сохранить вложение") }
                            Text(message.createdAt.atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("HH:mm")) + (if (message.edited) " · изменено" else "") + (if (mine) if (message.read) " · прочитано" else " · отправлено" else ""), color = Zapara.colors.text2, style = Zapara.typography.caption)
                            if (message.reactions.isNotEmpty()) Row { message.reactions.forEach { reaction -> TextButton(onClick = { onEvent(InboxEvent.React(message, reaction.emoji)) }, enabled = !state.loading) { Text("${emoji(reaction.emoji)} ${reaction.count}${if (reaction.mine) " ✓" else ""}") } } }
                        }
                    }
                }
            }
        }
        Column(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l, vertical = 8.dp)) {
            if (state.reply != null || state.editing != null) Row(verticalAlignment = Alignment.CenterVertically) {
                Text((if (state.editing != null) "Изменение: " else "Ответ: ") + (state.editing ?: state.reply)?.body.orEmpty(), Modifier.weight(1f), color = Zapara.colors.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
                TextButton({ onEvent(InboxEvent.CancelCompose) }) { Text("Отмена") }
            }
            OutlinedTextField(state.draft, { onEvent(InboxEvent.Draft(it)) }, modifier = Modifier.fillMaxWidth().testTag("Inbox.Draft"), placeholder = { Text("Сообщение") }, maxLines = 4)
            if (state.editing == null) TextButton({ pick.launch(arrayOf("*/*")) }, enabled = !state.loading) { Text("Прикрепить фото или файл · до 20 МБ") }
            ZButton(if (state.editing != null) "Сохранить" else "Отправить", { onEvent(InboxEvent.Send) }, modifier = Modifier.align(Alignment.End), enabled = !state.loading && state.draft.isNotBlank())
        }
    }
    selected?.let { message ->
        AlertDialog(onDismissRequest = { selected = null }, title = { Text("Сообщение") }, text = {
            Column {
                TextButton({ onEvent(InboxEvent.Reply(message)); selected = null }) { Text("Ответить") }
                if (message.senderId == state.userId) {
                    if (message.kind == "text") TextButton({ onEvent(InboxEvent.Edit(message)); selected = null }) { Text("Изменить") }
                    TextButton({ deleting = message; selected = null }) { Text("Удалить") }
                }
                Row { listOf("like", "heart", "laugh", "wow", "sad").forEach { code -> TextButton({ onEvent(InboxEvent.React(message, code)); selected = null }, contentPadding = PaddingValues(4.dp), modifier = Modifier.weight(1f)) { Text(emoji(code)) } } }
            }
        }, confirmButton = { TextButton({ selected = null }) { Text("Закрыть") } })
    }
    deleting?.let { message -> AlertDialog(onDismissRequest = { deleting = null }, title = { Text("Удалить сообщение?") }, text = { Text("Сообщение будет удалено из переписки.") }, confirmButton = { TextButton({ onEvent(InboxEvent.Delete(message)); deleting = null }) { Text("Удалить") } }, dismissButton = { TextButton({ deleting = null }) { Text("Отмена") } }) }
}
private fun emoji(code: String) = when(code) { "like" -> "👍"; "heart" -> "❤️"; "laugh" -> "😂"; "wow" -> "😮"; "sad" -> "😢"; else -> code }
