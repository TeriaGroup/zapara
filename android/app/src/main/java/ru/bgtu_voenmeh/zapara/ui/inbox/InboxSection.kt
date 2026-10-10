@file:OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class, androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package ru.bgtu_voenmeh.zapara.ui.inbox

import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
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
import androidx.compose.ui.platform.LocalDensity
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.Alignment
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalContext
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
import ru.bgtu_voenmeh.zapara.ui.chat.KeepLatestVisible
import ru.bgtu_voenmeh.zapara.ui.chat.ChatAvatar
import ru.bgtu_voenmeh.zapara.ui.chat.chatMessagePreview
import ru.bgtu_voenmeh.zapara.ui.chat.chatDayLabel
import ru.bgtu_voenmeh.zapara.ui.chat.chatListTime
import ru.bgtu_voenmeh.zapara.ui.chat.samePersonalCluster
import ru.bgtu_voenmeh.zapara.data.avatars.AvatarKind
import ru.bgtu_voenmeh.zapara.data.avatars.AvatarTarget
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import androidx.compose.foundation.lazy.itemsIndexed
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun InboxSection(state: InboxUiState, onEvent: (InboxEvent) -> Unit,
    onOpenGroup: (communityId: String, conversationId: String) -> Unit, modifier: Modifier = Modifier,
    onOpenAccount: () -> Unit = {}) {
    var inboxQuery by rememberSaveable(state.userId) { mutableStateOf("") }
    var inboxSource by rememberSaveable(state.userId) { mutableStateOf(InboxSourceFilter.All.name) }
    var unreadOnly by rememberSaveable(state.userId) { mutableStateOf(false) }
    var draftsOnly by rememberSaveable(state.userId) { mutableStateOf(false) }
    var unreadFirst by rememberSaveable(state.userId) { mutableStateOf(false) }
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val currentEvent by rememberUpdatedState(onEvent)
    LaunchedEffect(lifecycle, state.guest) {
        if (!state.guest) lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) { delay(15_000); currentEvent(InboxEvent.Refresh) }
        }
    }
    if (state.active != null) BackHandler { onEvent(InboxEvent.Back) }
    ru.bgtu_voenmeh.zapara.ui.shell.ReportConversationOpen(state.active != null) // #108 / AN-17: без нижней панели в беседе
    Column(modifier.fillMaxSize()) {
        if (state.active != null && !state.guest) {
            BoxWithConstraints(Modifier.fillMaxWidth().background(Zapara.colors.canvas)) {
                val stacked = maxWidth < 320.dp || LocalDensity.current.fontScale >= 1.5f
                if (stacked) Column(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l,
                    vertical = Zapara.space.xs)) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        ChatHeaderAction(R.drawable.ic_chevron_left, stringResource(R.string.chat_header_back), true,
                            "Inbox.Back", { onEvent(InboxEvent.Back) })
                        ChatAvatar(state.active.title, state.active.avatarTarget(), 36.dp,
                            Modifier.padding(start = Zapara.space.s))
                        Spacer(Modifier.weight(1f))
                        ChatHeaderAction(R.drawable.ic_refresh, stringResource(R.string.chat_header_refresh), !state.loading,
                            "Inbox.Refresh", { onEvent(InboxEvent.Refresh) })
                    }
                    Text(state.active.title, color = Zapara.colors.text1, style = Zapara.typography.bodyStrong,
                        modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), softWrap = false)
                    Text(inboxSubtitle(state.active), color = Zapara.colors.text2, style = Zapara.typography.caption,
                        modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), softWrap = false)
                } else Row(Modifier.fillMaxWidth().heightIn(min = 56.dp)
                    .padding(horizontal = Zapara.space.l), verticalAlignment = Alignment.CenterVertically) {
                    ChatHeaderAction(R.drawable.ic_chevron_left, stringResource(R.string.chat_header_back), true,
                        "Inbox.Back", { onEvent(InboxEvent.Back) })
                    ChatAvatar(state.active.title, state.active.avatarTarget(), 36.dp, Modifier.padding(end = 8.dp))
                    Column(Modifier.weight(1f)) {
                        Text(state.active.title, color = Zapara.colors.text1, style = Zapara.typography.bodyStrong,
                            modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), softWrap = false)
                        Text(inboxSubtitle(state.active), color = Zapara.colors.text2, style = Zapara.typography.caption,
                            modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), softWrap = false)
                    }
                    ChatHeaderAction(R.drawable.ic_refresh, stringResource(R.string.chat_header_refresh), !state.loading,
                        "Inbox.Refresh", { onEvent(InboxEvent.Refresh) })
                }
            }
        } else ZTopBar(stringResource(R.string.chat_header_chats)) {
            if (!state.guest) ChatHeaderAction(R.drawable.ic_refresh, stringResource(R.string.chat_header_refresh), !state.loading,
                "Inbox.Refresh", { onEvent(InboxEvent.Refresh) })
        }
        if (state.guest) {
            ru.bgtu_voenmeh.zapara.ui.components.EmptyState(R.drawable.ic_chat,
                stringResource(R.string.face_inbox_guest), actionText = stringResource(R.string.ux30_open_account),
                onAction = onOpenAccount, tag = "Inbox.Guest")
        } else {
            state.error?.let { Text(it, Modifier.padding(horizontal = Zapara.space.l, vertical = 8.dp).testTag("Inbox.Error"), color = Zapara.colors.bad, maxLines = 2, overflow = TextOverflow.Ellipsis) }
            if (state.active == null) InboxList(state, onEvent, onOpenGroup,
                inboxQuery, { inboxQuery = it }, inboxSource, { inboxSource = it },
                unreadOnly, { unreadOnly = it }, draftsOnly, { draftsOnly = it },
                unreadFirst, { unreadFirst = it }, Modifier.weight(1f))
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
    unreadOnly: Boolean, onUnreadOnly: (Boolean) -> Unit,
    draftsOnly: Boolean, onDraftsOnly: (Boolean) -> Unit,
    unreadFirst: Boolean, onUnreadFirst: (Boolean) -> Unit, modifier: Modifier) {
    var adding by remember { mutableStateOf(false) }
    var invitesExpanded by rememberSaveable(state.userId) { mutableStateOf(false) }
    var filtersExpanded by rememberSaveable(state.userId) { mutableStateOf(false) }
    val largeText = LocalDensity.current.fontScale >= 1.5f
    val sourceFilter = InboxSourceFilter.entries.firstOrNull { it.name == source } ?: InboxSourceFilter.All
    val activeFilters = listOf(sourceFilter != InboxSourceFilter.All, unreadOnly, draftsOnly, unreadFirst).count { it }
    val visible = browseInbox(state.rows, query, sourceFilter, unreadOnly, state.draftPreviews.keys, draftsOnly, unreadFirst)
    val filtered = query.isNotBlank() || sourceFilter != InboxSourceFilter.All || unreadOnly || draftsOnly
    LazyColumn(modifier, contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item {
            ZTextField(query, onQuery, label = { Text(stringResource(R.string.ux30_chat_inbox_search)) },
                singleLine = true, modifier = Modifier.fillMaxWidth().testTag("Inbox.Search"))
        }
        // #108 / AN-13: фильтры — только от 10 чатов (или пока фильтр включён, чтобы его можно было снять).
        if (InboxDensity.showFilters(state.rows.size, activeFilters)) item {
            ZDisclosureButton(
                text = if (activeFilters == 0) stringResource(R.string.inbox_compact_filters)
                    else stringResource(R.string.inbox_compact_filters_count, activeFilters),
                expanded = filtersExpanded,
                onClick = { filtersExpanded = !filtersExpanded },
                tag = "Inbox.Filters")
            if (filtersExpanded) {
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
                    ZChip(stringResource(R.string.ux30_inbox_unread_only), selected = unreadOnly,
                        onClick = { onUnreadOnly(!unreadOnly) }, tag = "Inbox.UnreadOnly")
                    ZChip(stringResource(R.string.ux30_chat_drafts_only), selected = draftsOnly,
                        onClick = { onDraftsOnly(!draftsOnly) }, tag = "Inbox.DraftsOnly")
                    ZChip(stringResource(R.string.ux30_chat_unread_first), selected = unreadFirst,
                        onClick = { onUnreadFirst(!unreadFirst) }, tag = "Inbox.UnreadFirst")
                }
            }
        }
        // #108 / AN-13: вместо трёх строк статистики — одна строка «Найдено», и только пока список сужен.
        if (filtered || unreadFirst) item {
            if (filtered) Text(stringResource(R.string.inbox_results, visible.size, state.rows.size),
                color = Zapara.colors.text2, style = Zapara.typography.caption, modifier = Modifier.testTag("Inbox.Found"))
            ZButton(stringResource(R.string.inbox_compact_reset_search_filters), {
                onQuery(""); onSource(InboxSourceFilter.All.name); onUnreadOnly(false); onDraftsOnly(false); onUnreadFirst(false)
            }, ghost = true, tag = "Inbox.Reset")
        }
        item {
            ZButton(if (adding) stringResource(R.string.face_close_invites) else stringResource(R.string.face_new_direct), { adding = !adding }, modifier = Modifier.fillMaxWidth(), ghost = true)
        }
        if (adding) item {
            ZCard(modifier = Modifier.fillMaxWidth()) {
                SelectionContainer { Text(stringResource(R.string.face_your_code, state.code.ifEmpty { stringResource(R.string.face_loading) }), color = Zapara.colors.text1) }
                ZButton(stringResource(R.string.ux30_copy_code_label), { onEvent(InboxEvent.CopyCode) },
                    enabled = state.code.isNotBlank(), ghost = true, tag = "Inbox.CopyCode")
                Text(stringResource(R.string.face_invite_hint), color = Zapara.colors.text2, style = Zapara.typography.caption)
                ZTextField(state.inviteCode, { onEvent(InboxEvent.Code(it)) }, label = { Text(stringResource(R.string.face_friend_code)) }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                ZButton(stringResource(R.string.face_invite), { onEvent(InboxEvent.Invite) }, enabled = !state.loading && state.inviteCode.isNotBlank())
                state.outgoing.forEach { Text(stringResource(R.string.face_invite_sent, it.name), color = Zapara.colors.text2) }
            }
        }
        if (state.incoming.isNotEmpty()) item("invite-heading") {
            ZButton(stringResource(if (invitesExpanded) R.string.ux300_android_hide_invites
                else R.string.ux300_android_show_invites, state.incoming.size),
                { invitesExpanded = !invitesExpanded }, ghost = true,
                tag = "Inbox.InvitesToggle")
        }
        if (invitesExpanded) items(state.incoming, key = { "invite:${it.id}" }) { invite ->
            ZCard(modifier = Modifier.fillMaxWidth()) {
                Text(stringResource(R.string.face_invites_you, invite.name), color = Zapara.colors.text1)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    ZButton(stringResource(R.string.face_accept), { onEvent(InboxEvent.Respond(invite.id, true)) },
                        enabled = !state.loading && state.respondingId == null)
                    ZButton(stringResource(R.string.face_decline), { onEvent(InboxEvent.Respond(invite.id, false)) },
                        enabled = !state.loading && state.respondingId == null, ghost = true)
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
            }
        }
        items(visible, key = { it.id }) { row ->
            ZCard(onClick = { row.communityId?.let { onOpenGroup(it, row.id) } ?: onEvent(InboxEvent.Open(row)) }, modifier = Modifier.fillMaxWidth(), tag = "Inbox.Chat.${row.id}") {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    ChatAvatar(row.title, row.avatarTarget(), 44.dp)
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Text(row.title, Modifier.weight(1f), color = Zapara.colors.text1,
                                style = Zapara.typography.bodyStrong, maxLines = if (largeText) 2 else 1,
                                overflow = TextOverflow.Ellipsis)
                            if (!largeText) row.lastAt?.let { Text(chatListTime(it, java.time.LocalDate.now(), ZoneId.systemDefault(), stringResource(R.string.yesterday)),
                                color = Zapara.colors.text2, style = Zapara.typography.caption) }
                            if (!largeText && row.unread > 0) {
                                val description = stringResource(R.string.group_unread_count, row.unread)
                                ZChip(if (row.unread > 99) "99+" else row.unread.toString(), selected = true,
                                    modifier = Modifier.semantics { contentDescription = description })
                            }
                        }
                        state.draftPreviews[row.id]?.let { draft ->
                            Text(stringResource(R.string.ux60_chat_draft_preview, draft),
                                color = Zapara.colors.accent, maxLines = if (largeText) 2 else 1,
                                overflow = TextOverflow.Ellipsis)
                        }
                        Text(row.lastBody?.takeIf { it.isNotBlank() } ?: stringResource(R.string.face_no_messages_yet),
                            color = Zapara.colors.text2, maxLines = if (largeText) 3 else 2,
                            overflow = TextOverflow.Ellipsis)
                        if (row.source != InboxSource.Friend) Text(inboxSubtitle(row),
                            color = Zapara.colors.text2, style = Zapara.typography.caption, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        if (largeText && (row.lastAt != null || row.unread > 0)) {
                            FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp),
                                verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                if (largeText) row.lastAt?.let {
                                    Text(chatListTime(it, java.time.LocalDate.now(), ZoneId.systemDefault(), stringResource(R.string.yesterday)),
                                        color = Zapara.colors.text2, style = Zapara.typography.caption)
                                }
                                if (row.unread > 0) {
                                    val description = stringResource(R.string.group_unread_count, row.unread)
                                    ZChip(if (row.unread > 99) "99+" else row.unread.toString(), selected = true,
                                        modifier = Modifier.semantics { contentDescription = description })
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

private fun InboxRow.avatarTarget(): AvatarTarget? = when {
    peerUserId != null -> AvatarTarget(AvatarKind.User, peerUserId)
    source == InboxSource.Group && communityId != null -> AvatarTarget(AvatarKind.Group, communityId)
    else -> null
}

@Composable
private fun PersonalChat(state: InboxUiState, onEvent: (InboxEvent) -> Unit, modifier: Modifier) {
    val activeId = state.active?.id ?: return
    val composerFocus = remember(activeId) { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    LaunchedEffect(activeId, state.editing?.id, state.reply?.id) {
        if (state.editing != null || state.reply != null) { if (runCatching { composerFocus.requestFocus() }.isSuccess) keyboard?.show() }
    }
    var historyQuery by rememberSaveable(activeId) { mutableStateOf("") }
    var historyKind by rememberSaveable(state.userId, state.profileDatabaseName, activeId) { mutableStateOf(PersonalHistoryKind.All.name) }
    var historyAuthor by rememberSaveable(state.userId, state.profileDatabaseName, activeId) { mutableStateOf(PersonalHistoryAuthor.All.name) }
    var historyDate by rememberSaveable(state.userId, state.profileDatabaseName, activeId) { mutableStateOf("") }
    val historyContext = LocalContext.current
    val historyCalendarTheme = if (Zapara.colors.isDark) R.style.Zapara_DatePicker_Dark
        else R.style.Zapara_DatePicker_Light
    var historyFiltersOpen by rememberSaveable(state.userId, state.profileDatabaseName, activeId) { mutableStateOf(false) }
    var historySearchOpen by rememberSaveable(activeId) { mutableStateOf(false) }
    val kindFilter = PersonalHistoryKind.entries.firstOrNull { it.name == historyKind } ?: PersonalHistoryKind.All
    val authorFilter = PersonalHistoryAuthor.entries.firstOrNull { it.name == historyAuthor } ?: PersonalHistoryAuthor.All
    val historyOptionsActive = kindFilter != PersonalHistoryKind.All ||
        authorFilter != PersonalHistoryAuthor.All || historyDate.isNotBlank()
    val historyFiltered = historyQuery.isNotBlank() || kindFilter != PersonalHistoryKind.All ||
        authorFilter != PersonalHistoryAuthor.All || historyDate.isNotBlank()
    val historyFilterCount = listOf(historyQuery.isNotBlank(), kindFilter != PersonalHistoryKind.All,
        authorFilter != PersonalHistoryAuthor.All, historyDate.isNotBlank()).count { it }
    fun clearHistoryFilters() { historyQuery = ""; historyKind = PersonalHistoryKind.All.name;
        historyAuthor = PersonalHistoryAuthor.All.name; historyDate = "" }
    val visibleMessages = browseLoadedPersonalHistory(state.messages, historyQuery, kindFilter, authorFilter, state.userId)
        .filter { historyDate.isBlank() || it.createdAt.atZone(ZoneId.systemDefault()).toLocalDate().toString() == historyDate }
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
    var discardPending by remember(activeId) { mutableStateOf<PendingPersonalRecording?>(null) }
    var attachOpen by remember(activeId) { mutableStateOf(false) }
    val list = rememberLazyListState()
    var pendingQuote by remember(activeId) { mutableStateOf<String?>(null) }
    var highlightedQuote by remember(activeId) { mutableStateOf<String?>(null) }
    var quoteNotice by remember(activeId) { mutableStateOf<PersonalQuoteTarget?>(null) }
    KeepLatestVisible(list, activeId, enabled = !historyFiltered && pendingQuote == null && highlightedQuote == null)
    LaunchedEffect(historyQuery, historyKind, historyAuthor, historyDate) { if (historyFiltered) list.scrollToItem(0) }
    var lastId by remember(state.active?.id) { mutableStateOf<String?>(null) }
    discardPending?.let { pending ->
        AlertDialog(
            onDismissRequest = { discardPending = null },
            title = { Text(stringResource(R.string.ux60_chat_record_pending_discard_title)) },
            text = { Text(stringResource(R.string.ux60_chat_record_pending_discard_body)) },
            confirmButton = { ZButton(stringResource(R.string.ux60_chat_record_pending_discard_confirm), {
                onEvent(InboxEvent.DiscardRecording(pending.scope, pending.file))
                discardPending = null
            }, destructive = true, leadingIcon = R.drawable.ic_trash) },
            dismissButton = { ZButton(stringResource(R.string.face_cancel),
                { discardPending = null }, ghost = true) }
        )
    }
    LaunchedEffect(state.messages.lastOrNull()?.id, historyQuery, historyKind, historyAuthor) {
        if (historyFiltered || pendingQuote != null || highlightedQuote != null) return@LaunchedEffect
        val next = state.messages.lastOrNull()?.id
        val total = list.layoutInfo.totalItemsCount
        val lastVisible = list.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
        val nearBottom = total == 0 || lastVisible >= total - 2
        if (shouldAutoScroll(lastId, next, nearBottom))
            list.animateScrollToItem((visibleMessages.size - 1 + if (state.hasMore) 1 else 0).coerceAtLeast(0))
        lastId = next
    }
    LaunchedEffect(pendingQuote, visibleMessages.map { it.id }, state.hasMore) {
        val target = pendingQuote ?: return@LaunchedEffect
        val index = visibleMessages.indexOfFirst { it.id == target }
        if (index >= 0) {
            list.animateScrollToItem(index + if (state.hasMore) 1 else 0)
            highlightedQuote = target
            pendingQuote = null
            quoteNotice = null
            delay(3000)
            if (highlightedQuote == target) highlightedQuote = null
        }
    }
    Column(modifier) {
        ZButton(when {
            historySearchOpen -> stringResource(R.string.chat_polish_hide_search)
            historyFiltered -> stringResource(R.string.chat_polish_search_active, historyFilterCount)
            else -> stringResource(R.string.chat_polish_search_messages)
        }, { historySearchOpen = !historySearchOpen },
            modifier = Modifier.padding(horizontal = Zapara.space.l),
            ghost = true, tag = "Inbox.HistorySearchToggle", leadingIcon = R.drawable.ic_search)
        if (historySearchOpen) {
        ZTextField(historyQuery, { historyQuery = it },
            modifier = Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l).testTag("Inbox.HistorySearch"),
            placeholder = { Text(stringResource(R.string.ux30_inbox_history_search)) }, singleLine = true,
            trailingIcon = if (historyQuery.isNotBlank()) ({
                ZIconButton(R.drawable.ic_x, stringResource(R.string.next_teachers_clear),
                    { historyQuery = "" }, "Inbox.HistoryClear")
            }) else null)
        FlowRow(Modifier.padding(horizontal = Zapara.space.l).padding(top = Zapara.space.s),
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            ZChip(stringResource(R.string.ux30_chat_history_filters), selected = historyFiltersOpen || historyOptionsActive,
                onClick = { historyFiltersOpen = !historyFiltersOpen }, tag = "Inbox.HistoryFilters")
            if (historyFiltered) ZButton(stringResource(R.string.group_message_reset), ::clearHistoryFilters,
                ghost = true, tag = "Inbox.HistoryReset")
        }
        }
        if (historyFiltered && !historySearchOpen) FlowRow(
            Modifier.padding(horizontal = Zapara.space.l),
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            if (historyQuery.isNotBlank()) Text(historyQuery, maxLines = 1,
                overflow = TextOverflow.Ellipsis, style = Zapara.typography.caption,
                color = Zapara.colors.text2)
            if (historyDate.isNotBlank()) Text(historyDate, style = Zapara.typography.caption,
                color = Zapara.colors.text2)
            Text(stringResource(R.string.ux30_chat_result_count, visibleMessages.size, state.messages.size),
                style = Zapara.typography.caption, color = Zapara.colors.text2)
            ZButton(stringResource(R.string.group_message_reset), ::clearHistoryFilters,
                ghost = true, tag = "Inbox.HistoryReset")
        }
        if (historyDate.isNotBlank() && historySearchOpen) Text(historyDate,
            modifier = Modifier.padding(horizontal = Zapara.space.l), color = Zapara.colors.text2,
            style = Zapara.typography.caption)
        if (historyFiltered && historySearchOpen) Text(stringResource(R.string.ux30_chat_result_count, visibleMessages.size, state.messages.size),
            modifier = Modifier.padding(horizontal = Zapara.space.l), color = Zapara.colors.text2,
            style = Zapara.typography.caption)
        if (historyFiltered && historySearchOpen) Text(stringResource(R.string.ux30_inbox_history_scope),
            modifier = Modifier.padding(horizontal = Zapara.space.l), color = Zapara.colors.text2,
            style = Zapara.typography.caption)
        if (quoteNotice != null) Text(stringResource(when {
            quoteNotice == PersonalQuoteTarget.Deleted -> R.string.next_quote_deleted
            state.hasMore -> R.string.next_quote_earlier
            else -> R.string.next_quote_missing
        }), modifier = Modifier.padding(horizontal = Zapara.space.l), color = Zapara.colors.warn,
            style = Zapara.typography.caption)
        if (quoteNotice == PersonalQuoteTarget.Earlier && state.hasMore)
            ZButton(stringResource(R.string.ux300_android_find_quoted_message),
                { onEvent(InboxEvent.Older) }, enabled = !state.loading,
                ghost = true, tag = "Inbox.LoadQuotedMessage")
        val scope = rememberCoroutineScope()
        val showJump by remember(list, state.messages.size, historyFiltered) { derivedStateOf {
            val totalItems = list.layoutInfo.totalItemsCount
            val lastVisible = list.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
            !historyFiltered && state.messages.size > 4 && totalItems > 0 && lastVisible < totalItems - 2
        } }
        Box(Modifier.weight(1f).fillMaxWidth()) {
        LazyColumn(Modifier.fillMaxSize().testTag("Inbox.Messages"), state = list, contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            if (state.hasMore) item { ZButton(stringResource(R.string.face_earlier), { onEvent(InboxEvent.Older) }, enabled = !state.loading, ghost = true) }
            if (state.messages.isEmpty() && state.loading) item {
                Text(stringResource(R.string.inbox_loading), color = Zapara.colors.text2)
            }
            if (!historyFiltered && state.messages.isEmpty() && state.historyLoaded && state.error == null && !state.loading) item { Text(stringResource(R.string.face_write_first), color = Zapara.colors.text2) }
            if (historyFiltered && visibleMessages.isEmpty() && state.historyLoaded && !state.loading) item {
                ZCard(Modifier.fillMaxWidth(), tag = "Inbox.HistoryEmpty") {
                    Text(stringResource(R.string.ux30_inbox_history_empty), color = Zapara.colors.text2)
                    ZButton(stringResource(R.string.group_message_reset), ::clearHistoryFilters, ghost = true)
                }
            }
            itemsIndexed(visibleMessages, key = { _, message -> message.id }) { index, message ->
                val mine = message.senderId == state.userId
                val zone = ZoneId.systemDefault()
                val previous = visibleMessages.getOrNull(index - 1)
                val cluster = samePersonalCluster(previous, message, zone)
                val day = message.createdAt.atZone(zone).toLocalDate()
                Column(Modifier.fillMaxWidth()) {
                if (highlightedQuote == message.id) Text(stringResource(R.string.next_quote_found),
                    color = Zapara.colors.accent, style = Zapara.typography.caption)
                if (previous == null || previous.createdAt.atZone(zone).toLocalDate() != day)
                    Text(chatDayLabel(day, java.time.LocalDate.now(zone), stringResource(R.string.today), stringResource(R.string.yesterday)),
                        Modifier.align(Alignment.CenterHorizontally).padding(vertical = 8.dp),
                        style = Zapara.typography.caption, color = Zapara.colors.text2)
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.Bottom,
                    horizontalArrangement = if (mine) Arrangement.End else Arrangement.Start) {
                    if (!mine) {
                        if (!cluster) ChatAvatar(message.senderName, AvatarTarget(AvatarKind.User, message.senderId), 28.dp)
                        else Spacer(Modifier.width(28.dp))
                        Spacer(Modifier.width(8.dp))
                    }
                    Surface(color = if (mine) Zapara.colors.chip else Zapara.colors.card,
                        shape = RoundedCornerShape(Zapara.radii.card),
                        border = BorderStroke(Zapara.space.hairline, if (mine) Zapara.colors.lineStrong else Zapara.colors.line),
                        modifier = Modifier.weight(1f, fill = false).widthIn(max = 320.dp).testTag("Inbox.Message.${message.id}").combinedClickable(onClick = {}, onLongClick = { if (!message.deleted) selected = message })) {
                        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            message.replyTo?.takeIf { !message.deleted }?.let { target -> ZButton(
                                "↳ ${message.replyBody?.take(80) ?: stringResource(R.string.face_message)}",
                                {
                                    when (val found = personalQuoteTarget(state.messages, target)) {
                                        PersonalQuoteTarget.Loaded -> {
                                            quoteNotice = null
                                            clearHistoryFilters()
                                            pendingQuote = target
                                        }
                                        PersonalQuoteTarget.Earlier -> {
                                            quoteNotice = found
                                            clearHistoryFilters()
                                            pendingQuote = target
                                            if (state.hasMore) onEvent(InboxEvent.Older)
                                        }
                                        else -> quoteNotice = found
                                    }
                                }, ghost = true, tag = "Inbox.Quote.${message.id}") }
                            if (message.deleted) Text(stringResource(R.string.face_message_deleted), color = Zapara.colors.text1)
                            else if (message.kind in setOf("image", "voice", "circle") && message.attachmentId != null) {
                                val attachment = message.attachmentId
                                ChatMediaBubble(message.kind, state.mediaFiles[attachment], message.durationMs,
                                    attachment in state.mediaLoading, attachment in state.mediaErrors,
                                    onLoad = { onEvent(InboxEvent.LoadMedia(message)) },
                                    notFound = attachment in state.mediaNotFound)
                            } else SelectionContainer { Text(message.body ?: message.fileName ?: stringResource(R.string.face_attachment), color = Zapara.colors.text1) }
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Text(message.createdAt.atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("HH:mm")) +
                                    (if (message.edited) stringResource(R.string.face_edited) else "") +
                                    (if (mine) stringResource(if (message.read) R.string.face_read else R.string.face_sent) else ""),
                                    color = Zapara.colors.text2, style = Zapara.typography.caption,
                                    modifier = Modifier.testTag("Inbox.MessageTime.${message.id}"))
                                if (!message.deleted) IconButton({ selected = message },
                                    modifier = Modifier.size(Zapara.space.minTouch)
                                        .testTag("Inbox.MessageActions.${message.id}")) {
                                    Icon(painterResource(R.drawable.ic_ellipsis),
                                        contentDescription = stringResource(R.string.ux100_chat_message_actions),
                                        tint = Zapara.colors.text2)
                                }
                            }
                            if (!message.deleted && message.reactions.isNotEmpty()) FlowRow(
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
        }
        if (showJump) ZButton(stringResource(R.string.inbox_jump_latest), {
            val last = list.layoutInfo.totalItemsCount - 1
            if (last >= 0) scope.launch { list.animateScrollToItem(last) }
        }, modifier = Modifier.align(Alignment.BottomEnd).padding(Zapara.space.s),
            ghost = true, tag = "Inbox.JumpLatest")
        }
        key(activeId) {
            state.pendingRecording?.let { pending ->
                Surface(color = Zapara.colors.card, shape = RoundedCornerShape(Zapara.radii.card),
                    border = BorderStroke(Zapara.space.hairline, Zapara.colors.line),
                    modifier = Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l, vertical = Zapara.space.xs)
                        .testTag("Inbox.PendingRecording")) {
                    Column(Modifier.padding(Zapara.space.m), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        Text(stringResource(if (pending.inFlight) R.string.ux60_chat_record_pending_sending
                            else R.string.ux60_chat_record_pending_warning), color = Zapara.colors.text1,
                            style = Zapara.typography.caption)
                        if (pending.uncertain) Text(stringResource(R.string.ux60_chat_record_pending_check_history),
                            color = Zapara.colors.bad, style = Zapara.typography.caption)
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                            if (pending.uncertain) ZButton(stringResource(R.string.ux60_chat_record_pending_retry),
                                { onEvent(InboxEvent.RetryRecording(pending.scope)) },
                                enabled = !state.sending && !state.loading, tag = "Inbox.PendingRecording.Retry")
                            ZButton(stringResource(R.string.ux60_chat_record_pending_discard),
                                { discardPending = pending }, enabled = !pending.inFlight,
                                ghost = true, tag = "Inbox.PendingRecording.Discard")
                        }
                    }
                }
            }
            ChatMediaCaptureHost(enabled = !state.sending && state.editing == null && state.pendingRecording == null,
                onRecorded = { kind, file, duration ->
                    val scope = PersonalRecordingScope(state.userId, state.profileDatabaseName, activeId)
                    onEvent(InboxEvent.UploadRecorded(scope, kind, file, duration))
                },
                onError = { onEvent(InboxEvent.LocalError(it)) },
                modifier = Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l, vertical = 8.dp)) { startVoice, startCircle ->
                Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    state.composer.error?.let {
                        Text(it, Modifier.testTag("Inbox.ComposeError"), color = Zapara.colors.bad,
                            style = Zapara.typography.caption, maxLines = 2, overflow = TextOverflow.Ellipsis)
                    }
                    if (state.sending) Text(stringResource(R.string.personal_sending),
                        Modifier.testTag("Inbox.Sending"), color = Zapara.colors.text2,
                        style = Zapara.typography.caption)
                    if (state.reply != null || state.editing != null) Row(verticalAlignment = Alignment.CenterVertically) {
                        val target = (state.editing ?: state.reply)!!
                        Column(Modifier.weight(1f)) {
                            Text(if (state.editing != null) stringResource(R.string.face_editing)
                                else stringResource(R.string.ux30_chat_reply_author, target.senderName),
                                color = Zapara.colors.text2, style = Zapara.typography.caption, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(chatMessagePreview(target.kind, target.body, target.fileName, target.deleted),
                                color = Zapara.colors.text1, maxLines = 2, overflow = TextOverflow.Ellipsis)
                        }
                        ZButton(stringResource(R.string.face_cancel), { onEvent(InboxEvent.CancelCompose) },
                            ghost = true, quiet = true, tag = "Inbox.CancelCompose")
                    }
                    Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        if (state.editing == null) Box {
                            ZIconButton(R.drawable.ic_paperclip, stringResource(R.string.face_attach_hint),
                                { attachOpen = true }, "Inbox.Attach", enabled = !state.sending)
                            DropdownMenu(expanded = attachOpen, onDismissRequest = { attachOpen = false }) {
                                DropdownMenuItem(text = { Text(stringResource(R.string.ux100_chat_image_pick)) },
                                    onClick = { attachOpen = false; pickingFor = activeId; pick.launch(arrayOf("image/*")) },
                                    modifier = Modifier.testTag("Inbox.Photo"))
                                DropdownMenuItem(text = { Text(stringResource(R.string.face_attachment)) },
                                    onClick = { attachOpen = false; pickingFor = activeId; pick.launch(arrayOf("*/*")) },
                                    modifier = Modifier.testTag("Inbox.File"))
                                DropdownMenuItem(text = { Text(stringResource(R.string.face_record_voice)) },
                                    enabled = !state.sending && state.pendingRecording == null,
                                    onClick = { attachOpen = false; startVoice() },
                                    modifier = Modifier.testTag("Inbox.RecordVoice"))
                                DropdownMenuItem(text = { Text(stringResource(R.string.face_record_circle)) },
                                    enabled = !state.sending && state.pendingRecording == null,
                                    onClick = { attachOpen = false; startCircle() },
                                    modifier = Modifier.testTag("Inbox.Circle"))
                            }
                        }
                        ZTextField(state.draft, { onEvent(InboxEvent.Draft(it)) },
                            modifier = Modifier.weight(1f).focusRequester(composerFocus).testTag("Inbox.Draft"),
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
                                { keyboard?.hide(); onEvent(InboxEvent.Send) }, "Inbox.Send",
                                enabled = !state.sending && state.composer.canSend, primary = true)
                        } else {
                            ZIconButton(R.drawable.ic_mic, stringResource(R.string.face_record_voice),
                                startVoice, "Inbox.Voice", enabled = !state.sending && state.pendingRecording == null)
                        }
                    }
                }
            }
        }
    }
    if (historyFiltersOpen) ZBottomSheet(onDismiss = { historyFiltersOpen = false }, tag = "Inbox.HistoryFilterSheet",
        scrollable = true, footer = {
            ZButton(stringResource(R.string.group_message_filters_done), { historyFiltersOpen = false },
                tag = "Inbox.HistoryFiltersDone", modifier = Modifier.fillMaxWidth())
        }) {
        Text(stringResource(R.string.ux30_chat_history_filters), style = Zapara.typography.section)
        ZButton(stringResource(R.string.ux300_android_chat_pick_date), {
            val date = runCatching { java.time.LocalDate.parse(historyDate) }
                .getOrDefault(java.time.LocalDate.now())
            android.app.DatePickerDialog(historyContext, historyCalendarTheme,
                { _, year, month, day -> historyDate = java.time.LocalDate.of(year, month + 1, day).toString() },
                date.year, date.monthValue - 1, date.dayOfMonth).show()
        }, ghost = true, tag = "Inbox.HistoryDate")
        if (historyDate.isNotBlank()) ZButton(stringResource(R.string.ux300_android_chat_any_date),
            { historyDate = "" }, ghost = true, tag = "Inbox.HistoryAnyDate")
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf(PersonalHistoryAuthor.All to R.string.group_author_all,
                PersonalHistoryAuthor.Mine to R.string.group_author_mine,
                PersonalHistoryAuthor.Others to R.string.group_author_others).forEach { (value, label) ->
                ZChip(stringResource(label), selected = authorFilter == value,
                    onClick = { historyAuthor = value.name }, tag = "Inbox.HistoryAuthor.${value.name}")
            }
        }
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf(PersonalHistoryKind.All to R.string.group_kind_all, PersonalHistoryKind.Text to R.string.group_kind_text,
                PersonalHistoryKind.PhotoVideo to R.string.group_kind_photo_video,
                PersonalHistoryKind.Documents to R.string.group_kind_documents,
                PersonalHistoryKind.VoiceCircle to R.string.group_kind_voice_circle).forEach { (value, label) ->
                ZChip(stringResource(label), selected = kindFilter == value,
                    onClick = { historyKind = value.name }, tag = "Inbox.HistoryKind.${value.name}")
            }
        }
        Text(stringResource(R.string.ux30_chat_result_count, visibleMessages.size, state.messages.size), style = Zapara.typography.caption)
    }
    selected?.let { message ->
        AlertDialog(onDismissRequest = { selected = null }, title = { Text(message.senderName) }, text = {
            Column {
                Text(chatMessagePreview(message.kind, message.body, message.fileName, message.deleted), maxLines = 4,
                    overflow = TextOverflow.Ellipsis, style = Zapara.typography.body)
                Text(message.createdAt.atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm")), style = Zapara.typography.caption)
                ZButton(stringResource(R.string.face_reply), { onEvent(InboxEvent.Reply(message)); selected = null }, ghost = true, quiet = true)
                if (copyablePersonalText(message) != null) ZButton(stringResource(R.string.inbox_compact_copy_text),
                    { onEvent(InboxEvent.CopyMessage(message.id)); selected = null }, ghost = true,
                    quiet = true, tag = "Inbox.CopyMessage")
                if (!message.deleted && message.attachmentId != null) ZButton(stringResource(R.string.face_save), {
                    saving = message; selected = null; save.launch(message.fileName ?: "attachment")
                }, enabled = !state.loading, ghost = true, quiet = true, tag = "Inbox.SaveMessage.${message.id}")
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
    deleting?.let { message -> AlertDialog(onDismissRequest = { deleting = null }, title = { Text(stringResource(R.string.face_delete_message)) }, text = {
        Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Text(message.senderName, style = Zapara.typography.bodyStrong)
            Text(chatMessagePreview(message.kind, message.body, message.fileName, message.deleted), maxLines = 4, overflow = TextOverflow.Ellipsis)
            Text(stringResource(R.string.face_delete_message_body))
        }
    }, confirmButton = { ZButton(stringResource(R.string.face_delete),
        { onEvent(InboxEvent.Delete(message)); deleting = null },
        destructive = true, leadingIcon = R.drawable.ic_trash) },
        dismissButton = { ZButton(stringResource(R.string.face_cancel), { deleting = null }, ghost = true, quiet = true) }) }
}
private fun emoji(code: String) = when(code) { "like" -> "👍"; "heart" -> "❤️"; "laugh" -> "😂"; "wow" -> "😮"; "sad" -> "😢"; else -> code }

/** #106 / AN-28: «Личный чат», «Личный чат · ИВТ-1», «Учебная группа» — из ресурсов; явная подпись строки важнее. */
@Composable
internal fun inboxSubtitle(row: InboxRow): String = when (row.source) {
    InboxSource.GroupDirect -> if (row.subtitle.isBlank()) stringResource(R.string.inbox_subtitle_personal)
        else stringResource(R.string.inbox_subtitle_personal_in, row.subtitle)
    InboxSource.Group -> row.subtitle.ifBlank { stringResource(R.string.inbox_subtitle_group) }
    else -> row.subtitle.ifBlank { stringResource(R.string.inbox_subtitle_personal) }
}
