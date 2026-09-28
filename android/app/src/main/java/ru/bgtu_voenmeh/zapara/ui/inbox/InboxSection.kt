@file:OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class, androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package ru.bgtu_voenmeh.zapara.ui.inbox

import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.Lifecycle
import androidx.compose.ui.platform.LocalLifecycleOwner
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import ru.bgtu_voenmeh.zapara.data.social.InboxSource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.media.ChatMediaBubble
import ru.bgtu_voenmeh.zapara.ui.media.ChatMediaCaptureHost
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun InboxSection(state: InboxUiState, onEvent: (InboxEvent) -> Unit, onOpenGroup: (communityId: String, conversationId: String) -> Unit, modifier: Modifier = Modifier) {
    var inboxQuery by rememberSaveable { mutableStateOf("") }
    var inboxSource by rememberSaveable { mutableStateOf(InboxSourceFilter.All.name) }
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val currentEvent by rememberUpdatedState(onEvent)
    LaunchedEffect(lifecycle, state.guest) {
        if (!state.guest) lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) { delay(15_000); currentEvent(InboxEvent.Refresh) }
        }
    }
    if (state.active != null) BackHandler { onEvent(InboxEvent.Back) }
    Column(modifier.fillMaxSize()) {
        if (state.active != null && !state.guest) {
            Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).background(Zapara.colors.canvas)
                .padding(horizontal = Zapara.space.l), verticalAlignment = Alignment.CenterVertically) {
                ChatHeaderAction(R.drawable.ic_chevron_left, stringResource(R.string.chat_header_back), true,
                    "Inbox.Back", { onEvent(InboxEvent.Back) })
                Text(state.active.title, Modifier.weight(1f), color = Zapara.colors.text1,
                    style = Zapara.typography.title, maxLines = 1, overflow = TextOverflow.Ellipsis)
                ChatHeaderAction(R.drawable.ic_refresh, stringResource(R.string.chat_header_refresh), !state.loading,
                    "Inbox.Refresh", { onEvent(InboxEvent.Refresh) })
            }
        } else ZTopBar(stringResource(R.string.chat_header_chats)) {
            if (!state.guest) ChatHeaderAction(R.drawable.ic_refresh, stringResource(R.string.chat_header_refresh), !state.loading,
                "Inbox.Refresh", { onEvent(InboxEvent.Refresh) })
        }
        if (state.guest) {
            Text(stringResource(R.string.face_inbox_guest), Modifier.padding(Zapara.space.l), color = Zapara.colors.text2)
        } else {
            state.error?.let { Text(it, Modifier.padding(horizontal = Zapara.space.l, vertical = 8.dp).testTag("Inbox.Error"), color = Zapara.colors.bad) }
            if (state.active == null) InboxList(state, onEvent, onOpenGroup,
                inboxQuery, { inboxQuery = it }, inboxSource, { inboxSource = it }, Modifier.weight(1f))
            else PersonalChat(state, onEvent, Modifier.weight(1f))
        }
    }
}

@Composable
private fun ChatHeaderAction(icon: Int, description: String, enabled: Boolean, tag: String, onClick: () -> Unit) {
    ZIconButton(icon, description, onClick, tag, enabled = enabled)
}

@Composable
private fun InboxList(state: InboxUiState, onEvent: (InboxEvent) -> Unit,
    onOpenGroup: (communityId: String, conversationId: String) -> Unit,
    query: String, onQuery: (String) -> Unit, source: String, onSource: (String) -> Unit,
    modifier: Modifier) {
    var adding by remember { mutableStateOf(false) }
    val sourceFilter = InboxSourceFilter.entries.firstOrNull { it.name == source } ?: InboxSourceFilter.All
    val visible = browseInbox(state.rows, query, sourceFilter)
    val filtered = query.isNotBlank() || sourceFilter != InboxSourceFilter.All
    LazyColumn(modifier, contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item {
            ZTextField(query, onQuery, label = { Text(stringResource(R.string.inbox_search)) },
                singleLine = true, modifier = Modifier.fillMaxWidth().testTag("Inbox.Search"))
        }
        item {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                listOf(InboxSourceFilter.All to R.string.inbox_source_all,
                    InboxSourceFilter.Group to R.string.inbox_source_group,
                    InboxSourceFilter.GroupDirect to R.string.inbox_source_group_direct,
                    InboxSourceFilter.Friend to R.string.inbox_source_friend).forEach { (value, label) ->
                    ZChip(stringResource(label), selected = sourceFilter == value,
                        onClick = { onSource(value.name) }, tag = "Inbox.Source.${value.name}",
                        modifier = Modifier.semantics { selected = sourceFilter == value })
                }
            }
        }
        item {
            Text(stringResource(R.string.inbox_results, visible.size, state.rows.size),
                color = Zapara.colors.text2, style = Zapara.typography.caption)
            Text(stringResource(R.string.inbox_unread_total, totalInboxUnread(state.rows)),
                color = Zapara.colors.text2, style = Zapara.typography.caption)
            if (filtered) ZButton(stringResource(R.string.inbox_search_reset), {
                onQuery(""); onSource(InboxSourceFilter.All.name)
            }, ghost = true, tag = "Inbox.Reset")
        }
        item {
            ZButton(if (adding) stringResource(R.string.face_close_invites) else stringResource(R.string.face_new_direct), { adding = !adding }, modifier = Modifier.fillMaxWidth(), ghost = true)
        }
        if (adding) item {
            ZCard(modifier = Modifier.fillMaxWidth()) {
                SelectionContainer { Text(stringResource(R.string.face_your_code, state.code.ifEmpty { stringResource(R.string.face_loading) }), color = Zapara.colors.text1) }
                Text(stringResource(R.string.face_invite_hint), color = Zapara.colors.text2, style = Zapara.typography.caption)
                ZTextField(state.inviteCode, { onEvent(InboxEvent.Code(it)) }, label = { Text(stringResource(R.string.face_friend_code)) }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                ZButton(stringResource(R.string.face_invite), { onEvent(InboxEvent.Invite) }, enabled = !state.loading && state.inviteCode.isNotBlank())
                state.outgoing.forEach { Text(stringResource(R.string.face_invite_sent, it.name), color = Zapara.colors.text2) }
            }
        }
        items(state.incoming, key = { "invite:${it.id}" }) { invite ->
            ZCard(modifier = Modifier.fillMaxWidth()) {
                Text(stringResource(R.string.face_invites_you, invite.name), color = Zapara.colors.text1)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    ZButton(stringResource(R.string.face_accept), { onEvent(InboxEvent.Respond(invite.id, true)) }, enabled = !state.loading)
                    ZButton(stringResource(R.string.face_decline), { onEvent(InboxEvent.Respond(invite.id, false)) }, enabled = !state.loading, ghost = true)
                }
            }
        }
        if (state.rows.isEmpty() && state.loading) item {
            Text(stringResource(R.string.inbox_loading), color = Zapara.colors.text2, style = Zapara.typography.caption)
            SkeletonList()
        }
        if (state.rows.isEmpty() && state.inboxLoaded && state.error == null && !state.loading && !filtered) item { Text(stringResource(R.string.face_inbox_empty), color = Zapara.colors.text2) }
        if (state.inboxLoaded && state.error == null && !state.loading && visible.isEmpty() && filtered) item {
            ZCard(modifier = Modifier.fillMaxWidth(), tag = "Empty.InboxSearch") {
                Text(stringResource(R.string.inbox_no_results), color = Zapara.colors.text2)
                ZButton(stringResource(R.string.inbox_search_reset), {
                    onQuery(""); onSource(InboxSourceFilter.All.name)
                }, ghost = true)
            }
        }
        items(visible, key = { it.id }) { row ->
            ZCard(onClick = { row.communityId?.let { onOpenGroup(it, row.id) } ?: onEvent(InboxEvent.Open(row)) }, modifier = Modifier.fillMaxWidth(), tag = "Inbox.Chat.${row.id}") {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(row.title, Modifier.weight(1f), color = Zapara.colors.text1, style = Zapara.typography.bodyStrong, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    if (row.unread > 0) {
                        val description = stringResource(R.string.group_unread_count, row.unread)
                        ZChip(if (row.unread > 99) "99+" else row.unread.toString(), selected = true,
                            modifier = Modifier.semantics { contentDescription = description })
                    }
                }
                ZChip(stringResource(when (row.source) {
                    InboxSource.Group -> R.string.inbox_source_group
                    InboxSource.GroupDirect -> R.string.inbox_source_group_direct
                    InboxSource.Friend -> R.string.inbox_source_friend
                }))
                if (row.source == InboxSource.GroupDirect) Text(row.subtitle,
                    color = Zapara.colors.text2, style = Zapara.typography.caption)
                row.lastAt?.let { Text(stringResource(R.string.inbox_last_at, formatInboxTime(it, ZoneId.systemDefault())),
                    color = Zapara.colors.text2, style = Zapara.typography.caption) }
                Text(row.lastBody ?: stringResource(R.string.face_no_messages_yet), color = Zapara.colors.text2, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
private fun PersonalChat(state: InboxUiState, onEvent: (InboxEvent) -> Unit, modifier: Modifier) {
    val activeId = state.active?.id ?: return
    var saving by remember { mutableStateOf<SocialMessage?>(null) }
    var pickingFor by remember { mutableStateOf<String?>(null) }
    val pick = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        val origin = pickingFor
        pickingFor = null
        if (uri != null && origin != null) onEvent(InboxEvent.Upload(origin, uri))
    }
    val save = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/octet-stream")) { uri -> val message = saving; if (uri != null && message != null) onEvent(InboxEvent.Save(message, uri)); saving = null }
    var selected by remember(state.active?.id) { mutableStateOf<SocialMessage?>(null) }
    var deleting by remember { mutableStateOf<SocialMessage?>(null) }
    val list = rememberLazyListState()
    var lastId by remember(state.active?.id) { mutableStateOf<String?>(null) }
    LaunchedEffect(state.messages.lastOrNull()?.id) {
        val next = state.messages.lastOrNull()?.id
        val total = list.layoutInfo.totalItemsCount
        val lastVisible = list.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
        val nearBottom = total == 0 || lastVisible >= total - 2
        if (shouldAutoScroll(lastId, next, nearBottom))
            list.animateScrollToItem((state.messages.size - 1 + if (state.hasMore) 1 else 0).coerceAtLeast(0))
        lastId = next
    }
    Column(modifier) {
        val scope = rememberCoroutineScope()
        val totalItems = list.layoutInfo.totalItemsCount
        val lastVisible = list.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
        val showJump = state.messages.size > 4 && totalItems > 0 && lastVisible < totalItems - 2
        Box(Modifier.weight(1f).fillMaxWidth()) {
        LazyColumn(Modifier.fillMaxSize(), state = list, contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            if (state.hasMore) item { ZButton(stringResource(R.string.face_earlier), { onEvent(InboxEvent.Older) }, enabled = !state.loading, ghost = true) }
            if (state.messages.isEmpty() && state.loading) item {
                Text(stringResource(R.string.inbox_loading), color = Zapara.colors.text2)
            }
            if (state.messages.isEmpty() && state.historyLoaded && state.error == null && !state.loading) item { Text(stringResource(R.string.face_write_first), color = Zapara.colors.text2) }
            items(state.messages, key = { it.id }) { message ->
                val mine = message.senderId == state.userId
                Row(Modifier.fillMaxWidth(), horizontalArrangement = if (mine) Arrangement.End else Arrangement.Start) {
                    Surface(color = if (mine) Zapara.colors.chip else Zapara.colors.card,
                        shape = RoundedCornerShape(Zapara.radii.card),
                        border = BorderStroke(Zapara.space.hairline, if (mine) Zapara.colors.lineStrong else Zapara.colors.line),
                        modifier = Modifier.widthIn(max = 320.dp).combinedClickable(onClick = {}, onLongClick = { if (!message.deleted) selected = message })) {
                        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Text(message.senderName, color = Zapara.colors.text2, style = Zapara.typography.caption)
                            message.replyBody?.let { Text("↳ $it", color = Zapara.colors.text2, maxLines = 2, overflow = TextOverflow.Ellipsis) }
                            if (message.deleted) Text(stringResource(R.string.face_message_deleted), color = Zapara.colors.text1)
                            else if (message.kind in setOf("image", "voice", "circle") && message.attachmentId != null) {
                                val attachment = message.attachmentId
                                ChatMediaBubble(message.kind, state.mediaFiles[attachment], message.durationMs,
                                    attachment in state.mediaLoading, attachment in state.mediaErrors,
                                    onLoad = { onEvent(InboxEvent.LoadMedia(message)) })
                            } else SelectionContainer { Text(message.body ?: message.fileName ?: stringResource(R.string.face_attachment), color = Zapara.colors.text1) }
                            if (!message.deleted && message.attachmentId != null) ZButton(stringResource(R.string.face_save),
                                { saving = message; save.launch(message.fileName ?: "attachment") },
                                enabled = !state.loading, ghost = true, quiet = true)
                            Text(message.createdAt.atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("HH:mm")) + (if (message.edited) stringResource(R.string.face_edited) else "") + (if (mine) stringResource(if (message.read) R.string.face_read else R.string.face_sent) else ""), color = Zapara.colors.text2, style = Zapara.typography.caption)
                            if (message.reactions.isNotEmpty()) FlowRow(
                                horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
                                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                                message.reactions.forEach { reaction ->
                                    ZButton("${emoji(reaction.emoji)} ${reaction.count}${if (reaction.mine) " ✓" else ""}",
                                        { onEvent(InboxEvent.React(message, reaction.emoji)) },
                                        enabled = !state.loading, ghost = true)
                                }
                            }
                        }
                    }
                }
            }
        }
        if (showJump) ZButton(stringResource(R.string.inbox_jump_latest), {
            val last = list.layoutInfo.totalItemsCount - 1
            if (last >= 0) scope.launch { list.animateScrollToItem(last) }
        }, modifier = Modifier.align(Alignment.BottomEnd).padding(Zapara.space.s),
            ghost = true, tag = "Inbox.JumpLatest")
        }
        key(activeId) {
            ChatMediaCaptureHost(enabled = !state.loading && !state.sending && state.editing == null,
                onRecorded = { kind, file, duration -> onEvent(InboxEvent.UploadRecorded(activeId, kind, file, duration)) },
                onError = { onEvent(InboxEvent.LocalError(it)) },
                modifier = Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l, vertical = 8.dp)) { startVoice, startCircle ->
                Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    state.composer.error?.let {
                        Text(it, Modifier.testTag("Inbox.ComposeError"), color = Zapara.colors.bad,
                            style = Zapara.typography.caption)
                    }
                    if (state.sending) Text(stringResource(R.string.personal_sending),
                        Modifier.testTag("Inbox.Sending"), color = Zapara.colors.text2,
                        style = Zapara.typography.caption)
                    if (state.reply != null || state.editing != null) Row(verticalAlignment = Alignment.CenterVertically) {
                        Text((if (state.editing != null) stringResource(R.string.face_editing) else stringResource(R.string.face_replying)) + (state.editing ?: state.reply)?.body.orEmpty(), Modifier.weight(1f), color = Zapara.colors.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        ZButton(stringResource(R.string.face_cancel), { onEvent(InboxEvent.CancelCompose) },
                            ghost = true, quiet = true, tag = "Inbox.CancelCompose")
                    }
                    Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        ZTextField(state.draft, { onEvent(InboxEvent.Draft(it)) },
                            modifier = Modifier.weight(1f).testTag("Inbox.Draft"),
                            placeholder = { Text(stringResource(R.string.face_message)) }, maxLines = 4,
                            isError = state.composer.messageLength > PERSONAL_MESSAGE_LIMIT,
                            supportingText = if (state.composer.messageLength >= 1800) ({
                                Text(stringResource(if (state.composer.messageLength > PERSONAL_MESSAGE_LIMIT)
                                    R.string.personal_message_too_long else R.string.personal_message_count,
                                    state.composer.messageLength, PERSONAL_MESSAGE_LIMIT),
                                    Modifier.testTag("Inbox.Length"))
                            }) else null)
                        if (state.draft.isNotBlank() || state.editing != null) {
                            ZIconButton(R.drawable.ic_send,
                                if (state.editing != null) stringResource(R.string.face_save) else stringResource(R.string.face_send),
                                { onEvent(InboxEvent.Send) }, "Inbox.Send",
                                enabled = !state.loading && !state.sending && state.composer.canSend, primary = true)
                        } else {
                            ZIconButton(R.drawable.ic_mic, stringResource(R.string.face_record_voice),
                                startVoice, "Inbox.Voice", enabled = !state.loading && !state.sending)
                        }
                    }
                    if (state.editing == null) FlowRow(
                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        ZIconButton(R.drawable.ic_paperclip, stringResource(R.string.face_attach_hint),
                            { pickingFor = activeId; pick.launch(arrayOf("*/*")) }, "Inbox.Attach", enabled = !state.loading && !state.sending)
                        if (state.draft.isBlank()) ZIconButton(R.drawable.ic_video_circle,
                            stringResource(R.string.face_record_circle), startCircle, "Inbox.Circle", enabled = !state.loading && !state.sending)
                    }
                }
            }
        }
    }
    selected?.let { message ->
        AlertDialog(onDismissRequest = { selected = null }, title = { Text(stringResource(R.string.face_message)) }, text = {
            Column {
                ZButton(stringResource(R.string.face_reply), { onEvent(InboxEvent.Reply(message)); selected = null }, ghost = true, quiet = true)
                if (message.senderId == state.userId) {
                    if (message.kind == "text") ZButton(stringResource(R.string.face_edit), { onEvent(InboxEvent.Edit(message)); selected = null }, ghost = true, quiet = true)
                    ZButton(stringResource(R.string.face_delete), { deleting = message; selected = null }, ghost = true, quiet = true)
                }
                FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    listOf("like", "heart", "laugh", "wow", "sad").forEach { code ->
                        ZButton(emoji(code), { onEvent(InboxEvent.React(message, code)); selected = null }, ghost = true)
                    }
                }
            }
        }, confirmButton = { ZButton(stringResource(R.string.face_close), { selected = null }, ghost = true, quiet = true) })
    }
    deleting?.let { message -> AlertDialog(onDismissRequest = { deleting = null }, title = { Text(stringResource(R.string.face_delete_message)) }, text = { Text(stringResource(R.string.face_delete_message_body)) }, confirmButton = { ZButton(stringResource(R.string.face_delete), { onEvent(InboxEvent.Delete(message)); deleting = null }) }, dismissButton = { ZButton(stringResource(R.string.face_cancel), { deleting = null }, ghost = true, quiet = true) }) }
}
private fun emoji(code: String) = when(code) { "like" -> "👍"; "heart" -> "❤️"; "laugh" -> "😂"; "wow" -> "😮"; "sad" -> "😢"; else -> code }
