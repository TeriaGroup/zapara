@file:OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class, androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package ru.bgtu_voenmeh.zapara.ui.groups

import android.net.Uri
import android.provider.OpenableColumns
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.platform.LocalContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.communities.Ballot
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic
import ru.bgtu_voenmeh.zapara.ui.chat.HoldDecision
import ru.bgtu_voenmeh.zapara.ui.media.ChatMediaBubble
import ru.bgtu_voenmeh.zapara.ui.media.ChatMediaCaptureHost
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun GroupSection(state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    var groupSearch by rememberSaveable { mutableStateOf("") }
    var peopleSearch by rememberSaveable(state.title) { mutableStateOf("") }
    var channelSearch by rememberSaveable(state.title) { mutableStateOf("") }
    var channelKind by rememberSaveable(state.title) { mutableStateOf("all") }
    var unreadOnly by rememberSaveable(state.title) { mutableStateOf(false) }
    if (state.hasHome) BackHandler {
        onEvent(when {
            state.showTrusted -> GroupEvent.CloseTrusted
            state.showChannels -> GroupEvent.Back
            else -> GroupEvent.Channels
        })
    }
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.group_title))
        when {
            state.guest -> EmptyState(R.drawable.ic_chat, stringResource(R.string.group_need_account), tag = "Empty.GroupAccount")
            state.loading && !state.hasHome -> Column(
                Modifier.padding(Zapara.space.l),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
            ) {
                Text(stringResource(R.string.group_loading), color = Zapara.colors.text2, style = Zapara.typography.caption)
                SkeletonList()
            }
            state.failed && !state.hasHome && state.communities.isEmpty() -> EmptyState(
                R.drawable.ic_chat,
                stringResource(R.string.group_load_failed),
                actionText = stringResource(R.string.group_retry),
                onAction = { onEvent(GroupEvent.Refresh) },
                tag = "Empty.GroupFailed"
            )
            state.empty && !state.hasHome -> EmptyState(
                R.drawable.ic_chat,
                stringResource(R.string.group_not_member),
                hint = stringResource(R.string.group_disclaimer),
                tag = "Empty.Group"
            )
            !state.hasHome -> Column(Modifier.fillMaxSize()) {
                if (state.failed) {
                    Row(Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s),
                        verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.group_load_failed), color = Zapara.colors.bad,
                            style = Zapara.typography.caption, modifier = Modifier.weight(1f).testTag("Group.Error"))
                        ZButton(stringResource(R.string.group_retry), { onEvent(GroupEvent.Refresh) }, ghost = true)
                    }
                }
                CommunityList(state, onEvent, groupSearch, { groupSearch = it }, Modifier.weight(1f))
            }
            else -> Home(state, onEvent, peopleSearch, { peopleSearch = it },
                channelSearch, { channelSearch = it }, channelKind, { channelKind = it },
                unreadOnly, { unreadOnly = it })
        }
    }
}

@Composable
private fun CommunityList(state: GroupUiState, onEvent: (GroupEvent) -> Unit, query: String,
                          onQuery: (String) -> Unit, modifier: Modifier = Modifier.fillMaxSize()) {
    val visible = browseCommunities(state.communities, query)
    LazyColumn(
        modifier,
        contentPadding = PaddingValues(Zapara.space.l),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
    ) {
        item {
            OutlinedTextField(query, onQuery, label = { Text(stringResource(R.string.group_search)) },
                singleLine = true, modifier = Modifier.fillMaxWidth().testTag("Group.Search"))
        }
        item { Text(stringResource(R.string.channel_results, visible.size, state.communities.size),
            style = Zapara.typography.caption, color = Zapara.colors.text2) }
        if (visible.isEmpty() && query.isNotBlank()) item {
            ZCard(Modifier.fillMaxWidth(), tag = "Empty.GroupSearch") {
                Text(stringResource(R.string.group_search_empty), style = Zapara.typography.body,
                    color = Zapara.colors.text2)
                ZButton(stringResource(R.string.group_search_clear), { onQuery("") }, ghost = true)
            }
        }
        items(visible, key = { it.id }) { item ->
            ZCard(onClick = { onEvent(GroupEvent.Open(item.id)) }, tag = "Group.Open.${item.id}", modifier = Modifier.fillMaxWidth()) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    Text(
                        item.name,
                        style = Zapara.typography.bodyStrong,
                        color = Zapara.colors.text1,
                        modifier = Modifier.weight(1f),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    RoleChip(item.role)
                }
            }
        }
    }
}

@Composable
private fun Home(state: GroupUiState, onEvent: (GroupEvent) -> Unit,
                 peopleSearch: String, onPeopleSearch: (String) -> Unit,
                 channelSearch: String, onChannelSearch: (String) -> Unit,
                 channelKind: String, onChannelKind: (String) -> Unit,
                 unreadOnly: Boolean, onUnreadOnly: (Boolean) -> Unit) {
    val c = Zapara.colors
    Column(
        Modifier.fillMaxSize().padding(horizontal = Zapara.space.l, vertical = Zapara.space.l),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
    ) {
        if (state.showChannels || state.showPeople) {
            ZButton(stringResource(R.string.group_list), { onEvent(GroupEvent.Back) }, ghost = true, tag = "Group.List")
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Text(state.title, style = Zapara.typography.section, color = c.text1,
                    modifier = Modifier.weight(1f), maxLines = 2, overflow = TextOverflow.Ellipsis)
                RoleChip(state.myRole)
            }
            Text(stringResource(R.string.group_disclaimer), style = Zapara.typography.caption, color = c.text2)
            ZSegmented(listOf(stringResource(R.string.channel_list), stringResource(R.string.group_people),
                stringResource(R.string.channel_general)),
                selected = if (state.showPeople) 1 else 0,
                onSelect = { onEvent(when (it) {
                    1 -> GroupEvent.People
                    2 -> GroupEvent.OpenChannel(null)
                    else -> GroupEvent.Chat
                }) },
                tag = "Group.Panes", modifier = Modifier.fillMaxWidth())
        } else {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.group_back), { onEvent(GroupEvent.Channels) }, ghost = true, tag = "Group.Back")
                Column(Modifier.weight(1f)) {
                    Text(state.title, style = Zapara.typography.caption, color = c.text2,
                        modifier = Modifier.fillMaxWidth())
                    Text(state.chatTitle, style = Zapara.typography.bodyStrong, color = c.text1,
                        modifier = Modifier.fillMaxWidth())
                }
            }
        }
        val nextUnread = nextUnreadChannel(state.channels, state.activeTopicId,
            inChannel = !state.showChannels && !state.showPeople && !state.direct &&
                (state.activeConversationId != null || state.activeTopicId != null))
        if (!state.direct) ZButton(stringResource(if (nextUnread == null) R.string.group_no_unread else R.string.group_next_unread),
            { nextUnread?.let { onEvent(GroupEvent.OpenChannel(it.topicId)) } },
            enabled = nextUnread != null, ghost = true, tag = "Group.NextUnread")
        if (state.failed) Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.group_failed), color = c.bad, modifier = Modifier.weight(1f).testTag("Group.Error"))
            if (!state.attachmentPending) ZButton(stringResource(R.string.group_refresh),
                { onEvent(GroupEvent.Refresh) }, ghost = true)
        }
        if (state.mediaError) Text(stringResource(R.string.group_media_failed), color = c.bad, modifier = Modifier.testTag("Group.MediaError"))
        if (!state.showPeople && !state.showChannels && !state.direct) {
            QuickChannels(state, onEvent)
            if (state.activeChannelKind == "chat") {
                val context = groupChatContext(state.channels, state.contextLesson)
                if (context.hasContent) GroupContextCard(state, context, onEvent)
            }
        }
        if (state.showPeople) {
            People(state, onEvent, peopleSearch, onPeopleSearch, Modifier.weight(1f))
        } else if (state.showChannels) {
            ChannelList(state, onEvent, channelSearch, onChannelSearch, channelKind, onChannelKind,
                unreadOnly, onUnreadOnly, Modifier.weight(1f))
        } else if (state.activeChannelKind == "ballots") {
            BallotChannel(state, onEvent, Modifier.weight(1f))
        } else {
            Messages(state, onEvent, Modifier.weight(1f))
            if (state.canPost) Composer(state, onEvent)
            else Text(stringResource(R.string.channel_read_only), style = Zapara.typography.caption, color = c.text2)
        }
    }
}

@Composable
private fun QuickChannels(state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val c = Zapara.colors
    val real = state.channels.filter { it.kind == "chat" || it.topicId != null }
    if (real.isEmpty()) return
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        LazyRow(Modifier.weight(1f).testTag("Group.QuickChannels"),
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            items(real, key = { "${it.kind}:${it.topicId ?: "general"}" }) { channel ->
                val active = channel.topicId == state.activeTopicId && channel.kind == state.activeChannelKind
                val description = if (channel.unread > 0)
                    "${channel.title}, ${stringResource(R.string.group_unread_count, channel.unread)}" else channel.title
                Surface(onClick = { if (!active) onEvent(GroupEvent.OpenChannel(channel.topicId)) },
                    shape = RoundedCornerShape(Zapara.radii.control),
                    color = if (active) c.selection else c.card,
                    border = BorderStroke(Zapara.space.hairline, if (active) c.lineStrong else c.line),
                    modifier = Modifier.widthIn(min = 100.dp, max = 184.dp).heightIn(min = Zapara.space.minTouch)
                        .semantics { selected = active; contentDescription = description }
                        .testTag("Group.QuickChannel.${channel.topicId ?: "general"}")) {
                    Row(Modifier.padding(horizontal = Zapara.space.s, vertical = Zapara.space.s),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        Text(channel.title, style = Zapara.typography.caption, color = c.text1,
                            maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                        if (channel.unread > 0) Text(if (channel.unread > 99) "99+" else channel.unread.toString(),
                            style = Zapara.typography.caption, color = c.text1)
                    }
                }
            }
        }
        ZButton(stringResource(R.string.chat_design_all_channels), { onEvent(GroupEvent.Channels) },
            ghost = true, tag = "Group.AllChannelsQuick")
    }
}

@Composable
private fun GroupContextCard(state: GroupUiState, context: GroupChatContext, onEvent: (GroupEvent) -> Unit) {
    val c = Zapara.colors
    var expanded by rememberSaveable(state.communityId) { mutableStateOf(true) }
    val lesson = context.nextLesson
    val firstBallot = state.channels.firstOrNull { it.topicId != null && it.kind == "ballots" && it.activeBallots > 0 }
    ZCard(Modifier.fillMaxWidth(), tag = "Group.Context") {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Text(stringResource(R.string.chat_design_context_title), style = Zapara.typography.bodyStrong,
                color = c.text1, modifier = Modifier.weight(1f))
            ZButton(stringResource(if (expanded) R.string.chat_design_context_collapse else R.string.chat_design_context_expand),
                { expanded = !expanded }, ghost = true, tag = "Group.ContextToggle")
        }
        if (expanded) {
            if (lesson != null) {
                val date = lesson.date.format(DateTimeFormatter.ofPattern("dd.MM"))
                Text(if (lesson.room.isBlank()) stringResource(R.string.chat_design_next_lesson,
                    date, lesson.time, lesson.subject) else stringResource(R.string.chat_design_next_lesson_room,
                    date, lesson.time, lesson.subject, lesson.room),
                    style = Zapara.typography.body, color = c.text1)
            }
            if (context.activeBallots > 0) Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.chat_design_active_ballots, context.activeBallots),
                    style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
                if (firstBallot != null) ZButton(stringResource(R.string.chat_design_open_ballots),
                    { onEvent(GroupEvent.OpenChannel(firstBallot.topicId)) }, ghost = true, tag = "Group.ContextBallots")
            }
            if (context.unread > 0) Text(stringResource(R.string.chat_design_channel_unread, context.unread),
                style = Zapara.typography.caption, color = c.text2)
        } else {
            val lines = listOfNotNull(
                lesson?.let { stringResource(R.string.chat_design_compact_lesson, it.time) },
                if (context.activeBallots > 0) stringResource(R.string.chat_design_compact_ballots, context.activeBallots) else null,
                if (context.unread > 0) stringResource(R.string.chat_design_compact_unread, context.unread) else null
            )
            Text(lines.joinToString(" · "), style = Zapara.typography.caption, color = c.text2,
                maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun ChannelList(state: GroupUiState, onEvent: (GroupEvent) -> Unit,
                        query: String, onQuery: (String) -> Unit, kind: String, onKind: (String) -> Unit,
                        unreadOnly: Boolean, onUnreadOnly: (Boolean) -> Unit, modifier: Modifier) {
    if (state.showTrusted) {
        TrustedPanel(state, onEvent, modifier)
        return
    }
    val c = Zapara.colors
    val allBallotsTitle = stringResource(R.string.channel_all_ballots)
    val visible = browseChannels(state.channels, query, kind, unreadOnly)
    val filtered = query.isNotBlank() || kind != "all" || unreadOnly
    val showAllBallots = !unreadOnly && kind != "chat" &&
        (query.isBlank() || allBallotsTitle.contains(query.trim(), ignoreCase = true))
    var managing by remember(state.title, state.canManageChannels) { mutableStateOf(false) }
    var creating by remember(state.title, state.canManageChannels) { mutableStateOf(false) }
    var editing by remember(state.title, state.canManageChannels) { mutableStateOf<GroupTopic?>(null) }
    var deleting by remember(state.title, state.canManageChannels) { mutableStateOf<GroupTopic?>(null) }
    var searchOpen by rememberSaveable(state.communityId) { mutableStateOf(false) }
    LazyColumn(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.channel_list_title), style = Zapara.typography.section,
                    color = c.text1, modifier = Modifier.weight(1f))
                IconButton(onClick = {
                    searchOpen = !searchOpen
                    if (!searchOpen) onQuery("")
                }, modifier = Modifier.size(Zapara.space.minTouch).testTag("Group.ChannelSearchToggle")) {
                    Icon(painterResource(R.drawable.ic_search), stringResource(R.string.channel_search), tint = c.text1)
                }
            }
        }
        if (searchOpen || query.isNotBlank()) item {
            OutlinedTextField(query, onQuery, label = { Text(stringResource(R.string.channel_search)) },
                placeholder = { Text(stringResource(R.string.channel_search_hint)) }, singleLine = true,
                modifier = Modifier.fillMaxWidth().testTag("Group.ChannelSearch"))
        }
        item {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                listOf("all" to R.string.channel_filter_all, "chat" to R.string.channel_filter_chats,
                    "ballots" to R.string.channel_filter_ballots).forEach { (value, label) ->
                    ZChip(stringResource(label), selected = kind == value, onClick = { onKind(value) },
                        tag = "Group.ChannelFilter.$value")
                }
                ZChip(stringResource(R.string.channel_filter_unread), selected = unreadOnly,
                    onClick = { onUnreadOnly(!unreadOnly) }, tag = "Group.ChannelFilter.Unread")
            }
        }
        if (filtered) item {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Text(stringResource(R.string.channel_results, visible.size, state.channels.size),
                    style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
                if (filtered) ZButton(stringResource(R.string.channel_reset_filters), {
                    onQuery(""); onKind("all"); onUnreadOnly(false)
                }, ghost = true, tag = "Group.ChannelReset")
            }
        }
        if (state.canManageChannels) item {
            ZButton(stringResource(if (managing) R.string.channel_close_manage else R.string.channel_manage), {
                managing = !managing
                if (!managing) { creating = false; editing = null }
            }, ghost = true, tag = "Group.ChannelManage")
        }
        if (state.canManageChannels && managing) item {
            if (creating) ChannelEditor(null, state.channelBusy,
                onSave = { title, icon, newKind, description, accent, pinned, policy ->
                    onEvent(GroupEvent.CreateChannel(title, icon, newKind, description, accent, pinned, policy)); creating = false },
                onCancel = { creating = false })
            else ZButton(stringResource(R.string.channel_create), { creating = true },
                enabled = !state.channelBusy, tag = "Group.ChannelCreate")
        }
        if (visible.isEmpty() && !showAllBallots) item {
            ZCard(Modifier.fillMaxWidth(), tag = "Empty.ChannelSearch") {
                Text(stringResource(R.string.channel_no_results), style = Zapara.typography.body, color = c.text2)
                if (filtered) ZButton(stringResource(R.string.channel_reset_filters), {
                    onQuery(""); onKind("all"); onUnreadOnly(false)
                }, ghost = true)
            }
        }
        items(visible, key = { it.topicId ?: "general" }) { channel ->
            val accent = when (channel.accent) {
                "blue" -> c.info
                "green" -> c.ok
                "purple" -> c.friends[3]
                "orange" -> c.warn
                "red" -> c.bad
                else -> null
            }
            val iconRes = when (channel.icon) {
                "💬" -> R.drawable.ic_chat
                "📌" -> R.drawable.ic_pin
                "🗳️" -> R.drawable.ic_ballot
                "📚" -> R.drawable.ic_homework
                else -> null
            }
            val preview = if (channel.kind == "ballots") stringResource(R.string.channel_ballot_count, channel.activeBallots)
                else if (!channel.lastBody.isNullOrBlank()) {
                    if (channel.lastAuthor.isNullOrBlank()) channel.lastBody!! else "${channel.lastAuthor}: ${channel.lastBody}"
                } else channel.description.ifBlank { stringResource(R.string.channel_no_messages) }
            Column {
                Surface(onClick = { onEvent(GroupEvent.OpenChannel(channel.topicId)) }, color = c.canvas,
                    modifier = Modifier.fillMaxWidth().testTag("Group.Channel.${channel.topicId ?: "general"}")) {
                    Row(Modifier.fillMaxWidth().heightIn(min = 64.dp).padding(vertical = Zapara.space.s),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        Surface(shape = RoundedCornerShape(Zapara.radii.control), color = c.chip,
                            modifier = Modifier.size(36.dp)) {
                            Box(contentAlignment = Alignment.Center) {
                                if (iconRes != null) Icon(painterResource(iconRes), null, tint = c.text1,
                                    modifier = Modifier.size(20.dp))
                                else Text(channel.icon, style = Zapara.typography.section, color = c.text1)
                            }
                        }
                        Column(Modifier.weight(1f)) {
                            Row(verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                                Text(channel.title, style = Zapara.typography.bodyStrong, color = c.text1,
                                    modifier = Modifier.weight(1f, fill = false))
                                if (channel.pinned) Icon(painterResource(R.drawable.ic_pin),
                                    stringResource(R.string.channel_pinned), tint = c.text2, modifier = Modifier.size(14.dp))
                                if (accent != null) Surface(shape = CircleShape, color = accent,
                                    modifier = Modifier.size(6.dp)) {}
                            }
                            Text(preview, style = Zapara.typography.caption, color = c.text2,
                                maxLines = 1, overflow = TextOverflow.Ellipsis)
                            if (channel.writePolicy == "managers") Text(stringResource(R.string.channel_writes_managers),
                                style = Zapara.typography.caption, color = c.text2)
                        }
                        Column(horizontalAlignment = Alignment.End) {
                            channel.lastAt?.let { lastAt ->
                                Text(DateTimeFormatter.ofPattern("dd.MM HH:mm").withZone(ZoneId.systemDefault()).format(lastAt),
                                    style = Zapara.typography.caption, color = c.text2)
                            }
                            UnreadBadge(channel.unread)
                        }
                        if (state.canManageChannels && managing && channel.topicId != null) {
                            var menuOpen by remember(channel.topicId) { mutableStateOf(false) }
                            Box {
                                IconButton(onClick = { menuOpen = true }, enabled = !state.channelBusy,
                                    modifier = Modifier.testTag("Group.ChannelActions.${channel.topicId}")) {
                                    Icon(painterResource(R.drawable.ic_menu), stringResource(R.string.channel_more), tint = c.text1)
                                }
                                DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
                                    DropdownMenuItem(text = { Text(stringResource(R.string.channel_edit)) }, onClick = {
                                        menuOpen = false; editing = channel
                                    }, modifier = Modifier.testTag("Group.ChannelEdit.${channel.topicId}"))
                                    if (channel.canDelete) DropdownMenuItem(text = { Text(stringResource(R.string.channel_delete)) }, onClick = {
                                        menuOpen = false; deleting = channel
                                    }, modifier = Modifier.testTag("Group.ChannelDelete.${channel.topicId}"))
                                }
                            }
                        }
                    }
                }
                HorizontalDivider(color = c.line, thickness = Zapara.space.hairline)
            }
            if (state.canManageChannels && managing && editing?.topicId == channel.topicId) {
                ChannelEditor(editing, state.channelBusy,
                    onSave = { title, icon, newKind, description, accent, pinned, policy ->
                        channel.topicId?.let { onEvent(GroupEvent.RenameChannel(it, title, icon, newKind, description, accent, pinned, policy)) }
                        editing = null },
                    onCancel = { editing = null })
            }
        }
        if (showAllBallots) item {
            Column {
                Surface(onClick = { onEvent(GroupEvent.GlobalBallots(allBallotsTitle)) }, color = c.canvas,
                    modifier = Modifier.fillMaxWidth().testTag("Group.AllBallots")) {
                    Row(Modifier.fillMaxWidth().heightIn(min = 64.dp).padding(vertical = Zapara.space.s),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        Surface(shape = RoundedCornerShape(Zapara.radii.control), color = c.chip,
                            modifier = Modifier.size(36.dp)) {
                            Box(contentAlignment = Alignment.Center) {
                                Icon(painterResource(R.drawable.ic_ballot), null, tint = c.text1,
                                    modifier = Modifier.size(20.dp))
                            }
                        }
                        Column {
                            Text(stringResource(R.string.channel_all_ballots), style = Zapara.typography.bodyStrong, color = c.text1)
                            Text(stringResource(R.string.channel_all_ballots_hint), style = Zapara.typography.caption, color = c.text2)
                        }
                    }
                }
                HorizontalDivider(color = c.line, thickness = Zapara.space.hairline)
            }
        }
        if (state.myRole == "headman" && managing) item {
            ZButton(stringResource(R.string.channel_trusted), { onEvent(GroupEvent.Trusted) },
                enabled = !state.channelBusy, ghost = true, tag = "Group.Trusted")
        }
    }
    if (state.canManageChannels) deleting?.let { channel ->
        AlertDialog(onDismissRequest = { deleting = null }, title = { Text(stringResource(R.string.channel_delete_question)) },
            text = { Text(stringResource(if (channel.kind == "ballots") R.string.channel_delete_ballot_warning
                else R.string.channel_delete_chat_warning)) },
            confirmButton = { ZButton(stringResource(R.string.channel_delete), {
                channel.topicId?.let { onEvent(GroupEvent.DeleteChannel(it)) }
                deleting = null
            }, enabled = !state.channelBusy) },
            dismissButton = { ZButton(stringResource(R.string.channel_cancel), { deleting = null }, ghost = true) })
    }
}

@Composable
private fun TrustedPanel(state: GroupUiState, onEvent: (GroupEvent) -> Unit, modifier: Modifier) {
    val c = Zapara.colors
    val desk = state.desk
    val defaultName = stringResource(R.string.channel_trusted_role_default)
    var roleName by remember { mutableStateOf(defaultName) }
    var selectedRoleId by remember { mutableStateOf<String?>(null) }
    val enabledRoles = desk?.let(::trustedChannelRoles).orEmpty()
    val selected = enabledRoles.firstOrNull { it.roleId == selectedRoleId } ?: enabledRoles.firstOrNull()
    LazyColumn(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item { ZButton(stringResource(R.string.channel_trusted_back), { onEvent(GroupEvent.CloseTrusted) }, ghost = true, tag = "Group.TrustedBack") }
        item { Text(stringResource(R.string.channel_trusted), style = Zapara.typography.section, color = c.text1) }
        item { Text(stringResource(R.string.channel_trusted_hint), style = Zapara.typography.caption, color = c.text2) }
        if (desk == null) item { Text(stringResource(if (state.failed) R.string.channel_trusted_load_failed
            else R.string.channel_trusted_loading), color = c.text2) }
        else if (enabledRoles.isEmpty()) item {
            ZCard(Modifier.fillMaxWidth(), tag = "Group.TrustedRoleEditor") {
                Text(stringResource(R.string.channel_trusted_empty), style = Zapara.typography.body, color = c.text1)
                OutlinedTextField(roleName, { roleName = it.take(32) },
                    label = { Text(stringResource(R.string.channel_trusted_role_name)) }, singleLine = true,
                    modifier = Modifier.fillMaxWidth().testTag("Group.TrustedRoleName"))
                ZButton(stringResource(R.string.channel_trusted_create_role),
                    { onEvent(GroupEvent.CreateTrustedRole(roleName.trim())) },
                    enabled = !state.channelBusy && roleName.trim().length in 2..32,
                    tag = "Group.TrustedRoleCreate")
            }
        } else {
            if (enabledRoles.size > 1) item {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    enabledRoles.forEach { role ->
                        ZChip(role.name, selected = role.roleId == selected?.roleId,
                            onClick = { selectedRoleId = role.roleId }, tag = "Group.TrustedRole.${role.roleId}")
                    }
                }
            }
            if (selected != null) {
                item { Text(selected.name, style = Zapara.typography.bodyStrong, color = c.text1) }
                items(state.people.filterNot { it.self }, key = { it.id }) { person ->
                    val granted = desk.grants.any { it.roleId == selected.roleId && it.userId == person.id }
                    ZCard(Modifier.fillMaxWidth(), tag = "Group.TrustedPerson.${person.id}") {
                        Text(person.name, style = Zapara.typography.bodyStrong, color = c.text1)
                        Text("@${person.handle}", style = Zapara.typography.caption, color = c.text2)
                        ZButton(stringResource(if (granted) R.string.channel_trusted_revoke else R.string.channel_trusted_grant),
                            { onEvent(if (granted) GroupEvent.RevokeTrusted(selected.roleId, person.id)
                                else GroupEvent.GrantTrusted(selected.roleId, person.id)) },
                            enabled = !state.channelBusy, ghost = granted,
                            tag = "Group.TrustedGrant.${person.id}")
                    }
                }
            }
        }
        if (desk != null && enabledRoles.isEmpty()) {
            items(desk.roles.filter { role -> desk.grants.none { it.roleId == role.roleId } &&
                desk.powers.none { it.roleId == role.roleId } }, key = { it.roleId }) { role ->
                ZCard(Modifier.fillMaxWidth(), tag = "Group.TrustedReuse.${role.roleId}") {
                    Text(role.name, style = Zapara.typography.bodyStrong, color = c.text1)
                    ZButton(stringResource(R.string.channel_trusted_reuse_role),
                        { onEvent(GroupEvent.EnableTrustedRole(role.roleId)) }, enabled = !state.channelBusy, ghost = true)
                }
            }
        }
    }
}

@Composable
private fun ChannelEditor(initial: GroupTopic?, busy: Boolean,
    onSave: (String, String, String, String, String, Boolean, String) -> Unit, onCancel: () -> Unit) {
    var title by remember(initial?.topicId) { mutableStateOf(initial?.title.orEmpty()) }
    var icon by remember(initial?.topicId) { mutableStateOf(initial?.icon ?: "💬") }
    var kind by remember(initial?.topicId) { mutableStateOf(initial?.kind ?: "chat") }
    var description by remember(initial?.topicId) { mutableStateOf(initial?.description.orEmpty()) }
    var accent by remember(initial?.topicId) { mutableStateOf(initial?.accent ?: "default") }
    var pinned by remember(initial?.topicId) { mutableStateOf(initial?.pinned ?: false) }
    var writePolicy by remember(initial?.topicId) { mutableStateOf(initial?.writePolicy ?: "all") }
    ZCard(Modifier.fillMaxWidth(), tag = "Group.ChannelEditor") {
        Text(stringResource(if (initial == null) R.string.channel_new else R.string.channel_change), style = Zapara.typography.bodyStrong)
        OutlinedTextField(title, { title = it.take(40) }, label = { Text(stringResource(R.string.channel_name)) }, singleLine = true,
            modifier = Modifier.fillMaxWidth().testTag("Group.ChannelTitle"))
        OutlinedTextField(icon, { icon = it.take(8) }, label = { Text(stringResource(R.string.channel_icon)) }, singleLine = true,
            modifier = Modifier.fillMaxWidth().testTag("Group.ChannelIcon"))
        OutlinedTextField(description, { description = it.take(240) }, label = { Text(stringResource(R.string.channel_description)) },
            modifier = Modifier.fillMaxWidth().testTag("Group.ChannelDescription"))
        if (initial == null) Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZChip(stringResource(R.string.channel_chat_type), selected = kind == "chat", onClick = { kind = "chat" }, tag = "Group.ChannelType.Chat")
            ZChip(stringResource(R.string.channel_ballot_type), selected = kind == "ballots", onClick = { kind = "ballots" }, tag = "Group.ChannelType.Ballots")
        } else Text(stringResource(if (kind == "ballots") R.string.channel_ballot_label else R.string.channel_chat_label), style = Zapara.typography.caption)
        Text(stringResource(R.string.channel_accent), style = Zapara.typography.caption)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf("default" to R.string.channel_accent_default, "blue" to R.string.channel_accent_blue,
                "green" to R.string.channel_accent_green, "purple" to R.string.channel_accent_purple,
                "orange" to R.string.channel_accent_orange, "red" to R.string.channel_accent_red).forEach { (value, label) ->
                ZChip(stringResource(label), selected = accent == value, onClick = { accent = value },
                    tag = "Group.ChannelAccent.$value")
            }
        }
        ZChip(stringResource(R.string.channel_pin), selected = pinned, onClick = { pinned = !pinned }, tag = "Group.ChannelPinned")
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            ZChip(stringResource(R.string.channel_writes_all), selected = writePolicy == "all",
                onClick = { writePolicy = "all" }, tag = "Group.ChannelWritesAll")
            ZChip(stringResource(R.string.channel_writes_managers), selected = writePolicy == "managers",
                onClick = { writePolicy = "managers" }, tag = "Group.ChannelWritesManagers")
        }
        Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.channel_save), {
                onSave(title.trim(), icon.trim(), kind, description.trim(), accent, pinned, writePolicy) },
                enabled = !busy && title.trim().length in 2..40, tag = "Group.ChannelSave")
            ZButton(stringResource(R.string.channel_cancel), onCancel, ghost = true)
        }
    }
}

@Composable
private fun BallotChannel(state: GroupUiState, onEvent: (GroupEvent) -> Unit, modifier: Modifier) {
    val c = Zapara.colors
    val board = state.board
    var query by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf("") }
    var status by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(BallotStatus.All.name) }
    var sort by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(BallotSort.Original.name) }
    var filtersOpen by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(false) }
    var composing by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(false) }
    var draftQuestion by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf("") }
    var draftOptions by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(arrayListOf("", "")) }
    var draftDays by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(3) }
    var seenCreateVersion by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(state.ballotCreateVersion) }
    LaunchedEffect(state.communityId, state.activeTopicId, state.ballotCreateVersion) {
        if (state.ballotCreateVersion != seenCreateVersion) {
            seenCreateVersion = state.ballotCreateVersion
            composing = false
            draftQuestion = ""
            draftOptions = arrayListOf("", "")
            draftDays = 3
        }
    }
    val statusFilter = BallotStatus.entries.firstOrNull { it.name == status } ?: BallotStatus.All
    val sortOrder = BallotSort.entries.firstOrNull { it.name == sort } ?: BallotSort.Original
    val visible = browseBallots(board?.ballots.orEmpty(), query, statusFilter, sortOrder)
    val filtered = query.isNotBlank() || statusFilter != BallotStatus.All || sortOrder != BallotSort.Original
    val hasDraft = draftQuestion.isNotBlank() || draftOptions.any { it.isNotBlank() }
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        Text(stringResource(R.string.channel_ballot_type), style = Zapara.typography.section, color = c.text1)
        if (state.chatLoading && board == null) {
            Text(stringResource(R.string.group_loading), style = Zapara.typography.caption, color = c.text2)
            SkeletonList()
        } else if (board == null) {
            ZCard(Modifier.fillMaxWidth(), tag = "Empty.BallotError") {
                Text(stringResource(R.string.channel_ballot_load_failed), color = c.bad)
                ZButton(stringResource(R.string.group_ballot_retry), { onEvent(GroupEvent.Refresh) }, ghost = true,
                    tag = "Group.BallotRetry")
            }
        } else {
            OutlinedTextField(query, { query = it }, label = { Text(stringResource(R.string.group_ballot_search)) },
                singleLine = true, modifier = Modifier.fillMaxWidth().testTag("Group.BallotSearch"))
            Text(stringResource(R.string.group_ballot_scope), style = Zapara.typography.caption, color = c.text2)
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Text(stringResource(R.string.group_ballot_results, visible.size, board.ballots.size),
                    style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
                ZChip(stringResource(R.string.group_ballot_filters), selected = filtered,
                    onClick = { filtersOpen = true }, tag = "Group.BallotFilters")
            }
            if (filtered) ZButton(stringResource(R.string.group_ballot_reset), {
                query = ""; status = BallotStatus.All.name; sort = BallotSort.Original.name
            }, ghost = true, tag = "Group.BallotReset")
            if (state.canPost || hasDraft) ZButton(stringResource(if (hasDraft) R.string.group_ballot_continue_draft else R.string.channel_ballot_new), {
                filtersOpen = false; composing = true
            }, enabled = !state.channelBusy, tag = "Group.BallotCreate")
            if (!state.canPost) Text(stringResource(R.string.channel_ballot_read_only), style = Zapara.typography.caption, color = c.text2)
            if (state.ballotRefreshFailed) ZCard(Modifier.fillMaxWidth(), tag = "Group.BallotRefreshError") {
                Text(stringResource(R.string.channel_ballot_refresh_failed), style = Zapara.typography.caption, color = c.warn)
                ZButton(stringResource(R.string.group_ballot_retry), { onEvent(GroupEvent.Refresh) }, ghost = true)
            }
            LazyColumn(Modifier.weight(1f).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (visible.isEmpty()) item {
                    ZCard(Modifier.fillMaxWidth(), tag = if (filtered) "Empty.BallotSearch" else "Empty.BallotBoard") {
                        Text(stringResource(if (filtered) R.string.group_ballot_no_results else R.string.channel_ballot_empty),
                            style = Zapara.typography.caption, color = c.text2)
                        if (filtered) ZButton(stringResource(R.string.group_ballot_reset), {
                            query = ""; status = BallotStatus.All.name; sort = BallotSort.Original.name
                        }, ghost = true)
                    }
                }
                items(visible, key = { it.ballotId }) { ballot ->
                    BallotCard(ballot, board.canClose, state.channelBusy, onEvent)
                }
            }
        }
    }
    if (filtersOpen && board != null) ZBottomSheet(onDismiss = { filtersOpen = false },
        tag = "Group.BallotFilterSheet", scrollable = true) {
        Text(stringResource(R.string.group_ballot_filters), style = Zapara.typography.section, color = c.text1)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf(BallotStatus.All to R.string.group_ballot_status_all,
                BallotStatus.Collecting to R.string.group_ballot_status_collecting,
                BallotStatus.Open to R.string.group_ballot_status_open,
                BallotStatus.Closed to R.string.group_ballot_status_closed).forEach { (value, label) ->
                ZChip(stringResource(label), selected = statusFilter == value,
                    onClick = { status = value.name }, tag = "Group.BallotStatus.${value.name}",
                    modifier = Modifier.semantics { selected = statusFilter == value })
            }
        }
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf(BallotSort.Original to R.string.group_ballot_sort_original,
                BallotSort.Nearest to R.string.group_ballot_sort_nearest,
                BallotSort.Farthest to R.string.group_ballot_sort_farthest).forEach { (value, label) ->
                ZChip(stringResource(label), selected = sortOrder == value,
                    onClick = { sort = value.name }, tag = "Group.BallotSort.${value.name}",
                    modifier = Modifier.semantics { selected = sortOrder == value })
            }
        }
        ZButton(stringResource(R.string.group_ballot_filters_done), { filtersOpen = false },
            modifier = Modifier.fillMaxWidth(), tag = "Group.BallotFiltersDone")
    }
    if (composing && board != null) ZBottomSheet(
        onDismiss = { if (!state.channelBusy) composing = false }, tag = "Group.BallotCreateSheet", scrollable = true) {
        BallotEditor(state.channelBusy, board.canOpen, state.canPost, state.ballotCreateFailed,
            question = draftQuestion, onQuestion = { draftQuestion = it },
            options = draftOptions, onOptions = { draftOptions = ArrayList(it) },
            days = draftDays, onDays = { draftDays = it },
            onSave = { question, options, days, headman ->
                onEvent(GroupEvent.CreateBallot(question, options, days, headman))
            }, onCancel = {
                if (!state.channelBusy) {
                    draftQuestion = ""; draftOptions = arrayListOf("", ""); draftDays = 3; composing = false
                }
            })
    }
}

@Composable
private fun BallotCard(ballot: Ballot, canClose: Boolean, busy: Boolean, onEvent: (GroupEvent) -> Unit) {
    val c = Zapara.colors
    val context = LocalContext.current
    var closing by remember(ballot.ballotId) { mutableStateOf(false) }
    var copyFeedback by remember(ballot.ballotId) { mutableStateOf<String?>(null) }
    val total = ballotTotalVotes(ballot)
    val deadline = remember(ballot.deadlineAt) {
        DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm").withZone(ZoneId.systemDefault()).format(ballot.deadlineAt)
    }
    val statusLabel = stringResource(when (ballot.status) {
        "collecting" -> R.string.channel_ballot_collecting
        "open" -> R.string.channel_ballot_open
        else -> R.string.channel_ballot_closed
    })
    val supportLine = if (ballot.status == "collecting")
        stringResource(R.string.channel_ballot_supporters, ballot.supporters, ballot.supportersNeeded) else null
    val summary = ballotSummary(ballot,
        stringResource(R.string.group_ballot_summary_status, statusLabel),
        stringResource(R.string.group_ballot_summary_deadline, deadline),
        stringResource(R.string.group_ballot_votes_label), stringResource(R.string.group_ballot_share_label), supportLine)
    val copiedText = stringResource(R.string.group_ballot_copied)
    val copyFailedText = stringResource(R.string.group_ballot_copy_failed)
    val clipboardLabel = stringResource(R.string.group_ballot_clip_label)
    LaunchedEffect(copyFeedback) {
        if (copyFeedback != null) {
            delay(2500)
            copyFeedback = null
        }
    }
    ZCard(Modifier.fillMaxWidth(), tag = "Group.Ballot.${ballot.ballotId}") {
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZChip(stringResource(when (ballot.origin) { "system" -> R.string.channel_ballot_origin_system;
                "headman" -> R.string.channel_ballot_origin_headman; else -> R.string.channel_ballot_origin_collective }))
            ZChip(statusLabel)
            if (ballot.effect.isNotBlank()) ZChip(stringResource(R.string.channel_ballot_effect))
        }
        Text(ballot.question, style = Zapara.typography.bodyStrong, color = c.text1)
        Text(stringResource(R.string.channel_ballot_deadline, deadline), style = Zapara.typography.caption, color = c.text2)
        when (ballotDeadlineNotice(ballot, Instant.now())) {
            DeadlineNotice.Soon -> Text(stringResource(R.string.group_ballot_deadline_soon),
                style = Zapara.typography.caption, color = c.warn)
            DeadlineNotice.AwaitingServer -> Text(stringResource(R.string.group_ballot_deadline_waiting),
                style = Zapara.typography.caption, color = c.warn)
            DeadlineNotice.None -> Unit
        }
        if (ballot.status == "collecting") {
            Text(supportLine.orEmpty(), style = Zapara.typography.caption, color = c.text2)
            if (ballot.supported) ZChip(stringResource(R.string.channel_ballot_supported))
            else ZButton(stringResource(R.string.channel_ballot_support), { onEvent(GroupEvent.SupportBallot(ballot.ballotId)) },
                enabled = !busy, tag = "Group.BallotSupport.${ballot.ballotId}")
        }
        ballot.options.forEach { option ->
            val percent = ballotPercent(option.votes, total)
            val resultText = stringResource(R.string.group_ballot_option_result, option.votes, percent)
            val optionLabel = if (option.chosen) stringResource(R.string.group_ballot_your_choice, option.label) else option.label
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                if (ballot.status == "open") ZButton(
                    optionLabel,
                    { onEvent(GroupEvent.VoteBallot(ballot.ballotId, option.optionId)) },
                    modifier = Modifier.fillMaxWidth(), enabled = !busy,
                    ghost = !option.chosen, tag = "Group.BallotVote.${ballot.ballotId}.${option.optionId}")
                else Text(optionLabel, style = Zapara.typography.body, color = c.text1)
                Text(resultText, style = Zapara.typography.caption, color = c.text2)
                val barDescription = stringResource(R.string.group_ballot_bar_description, option.label, resultText)
                LinearProgressIndicator(progress = { percent / 100f },
                    modifier = Modifier.fillMaxWidth().semantics { contentDescription = barDescription },
                    color = c.accent, trackColor = c.chip)
            }
        }
        val outcome = when (ballot.outcome) {
            "accepted" -> R.string.channel_ballot_outcome_accepted
            "rejected" -> R.string.channel_ballot_outcome_rejected
            "skipped" -> R.string.channel_ballot_outcome_skipped
            else -> null
        }
        if (outcome != null) Text(stringResource(outcome), style = Zapara.typography.caption, color = c.text2)
        ZButton(stringResource(R.string.group_ballot_copy), {
            copyFeedback = try {
                val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                clipboard.setPrimaryClip(ClipData.newPlainText(clipboardLabel, summary))
                copiedText
            } catch (_: Exception) { copyFailedText }
        }, ghost = true, tag = "Group.BallotCopy.${ballot.ballotId}")
        if (copyFeedback != null) Text(copyFeedback.orEmpty(), style = Zapara.typography.caption,
            color = c.text2, modifier = Modifier.testTag("Group.BallotCopyFeedback.${ballot.ballotId}")
                .semantics { liveRegion = LiveRegionMode.Polite })
        if (canClose && ballot.effect.isBlank() && ballot.status != "closed") ZButton(stringResource(R.string.channel_ballot_finish), { closing = true },
            enabled = !busy, ghost = true, tag = "Group.BallotClose.${ballot.ballotId}")
    }
    if (closing) AlertDialog(onDismissRequest = { closing = false }, title = { Text(stringResource(R.string.channel_ballot_finish_question)) },
        text = { Text(stringResource(R.string.channel_ballot_finish_warning)) },
        confirmButton = { ZButton(stringResource(R.string.channel_ballot_finish), { onEvent(GroupEvent.CloseBallot(ballot.ballotId)); closing = false }, enabled = !busy) },
        dismissButton = { ZButton(stringResource(R.string.channel_cancel), { closing = false }, ghost = true) })
}

@Composable
private fun BallotEditor(busy: Boolean, canOpen: Boolean, canSubmit: Boolean, failed: Boolean,
    question: String, onQuestion: (String) -> Unit, options: List<String>, onOptions: (List<String>) -> Unit,
    days: Int, onDays: (Int) -> Unit,
    onSave: (String, List<String>, Int, Boolean) -> Unit, onCancel: () -> Unit) {
    ZCard(Modifier.fillMaxWidth(), tag = "Group.BallotEditor") {
        Text(stringResource(R.string.channel_ballot_one_question), style = Zapara.typography.bodyStrong)
        if (failed) Text(stringResource(R.string.group_ballot_submit_failed), style = Zapara.typography.caption,
            color = Zapara.colors.bad, modifier = Modifier.testTag("Group.BallotSubmitError"))
        if (busy) Text(stringResource(R.string.group_ballot_submitting), style = Zapara.typography.caption,
            color = Zapara.colors.text2)
        if (!canSubmit) Text(stringResource(R.string.group_ballot_write_revoked), style = Zapara.typography.caption,
            color = Zapara.colors.warn)
        OutlinedTextField(question, { onQuestion(it.take(400)) }, label = { Text(stringResource(R.string.channel_ballot_question)) },
            modifier = Modifier.fillMaxWidth().testTag("Group.BallotQuestion"))
        options.forEachIndexed { index, option ->
            OutlinedTextField(option, { next -> onOptions(options.mapIndexed { i, value -> if (i == index) next.take(80) else value }) },
                label = { Text(stringResource(R.string.channel_ballot_option, index + 1)) }, singleLine = true,
                modifier = Modifier.fillMaxWidth().testTag("Group.BallotOption.$index"))
        }
        if (options.size < 6) ZButton(stringResource(R.string.channel_ballot_add_option), { onOptions(options + "") }, ghost = true)
        if (options.size > 2) ZButton(stringResource(R.string.channel_ballot_remove_option), { onOptions(options.dropLast(1)) }, ghost = true)
        Text(stringResource(R.string.channel_ballot_duration), style = Zapara.typography.caption)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf(1, 3, 7, 14).forEach { value ->
                ZChip(stringResource(R.string.channel_ballot_days, value), selected = days == value, onClick = { onDays(value) }, tag = "Group.BallotDays.$value")
            }
        }
        val valid = question.isNotBlank() && options.size in 2..6 && options.all { it.isNotBlank() } &&
            options.map { it.trim() }.distinct().size == options.size
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.channel_ballot_propose), { onSave(question.trim(), options.map { it.trim() }, days, false) },
                enabled = valid && canSubmit && !busy, tag = "Group.BallotPropose")
            if (canOpen) ZButton(stringResource(R.string.channel_ballot_announce), { onSave(question.trim(), options.map { it.trim() }, days, true) },
                enabled = valid && canSubmit && !busy, ghost = true, tag = "Group.BallotOpen")
            ZButton(stringResource(R.string.channel_cancel), onCancel, enabled = !busy, ghost = true)
        }
    }
}

@Composable
private fun People(state: GroupUiState, onEvent: (GroupEvent) -> Unit,
                   query: String, onQuery: (String) -> Unit, modifier: Modifier) {
    val c = Zapara.colors
    val visible = browsePeople(state.people, query)
    LazyColumn(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item {
            OutlinedTextField(query, onQuery, label = { Text(stringResource(R.string.channel_people_search)) },
                placeholder = { Text(stringResource(R.string.channel_people_search_hint)) }, singleLine = true,
                modifier = Modifier.fillMaxWidth().testTag("Group.PeopleSearch"))
        }
        item {
            ZCard(onClick = { onEvent(GroupEvent.GroupChat) }, tag = "Group.Room", modifier = Modifier.fillMaxWidth()) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    Text(
                        stringResource(R.string.group_chat),
                        style = Zapara.typography.bodyStrong,
                        color = c.text1,
                        modifier = Modifier.weight(1f)
                    )
                    UnreadBadge(state.groupUnread)
                }
            }
        }
        if (state.people.isNotEmpty()) {
            item { Text(stringResource(R.string.group_roster), style = Zapara.typography.bodyStrong, color = c.text2) }
        }
        if (query.isNotBlank() && visible.isEmpty()) item {
            ZCard(Modifier.fillMaxWidth(), tag = "Empty.GroupPeopleSearch") {
                Text(stringResource(R.string.channel_people_empty), style = Zapara.typography.body, color = c.text2)
                ZButton(stringResource(R.string.group_search_clear), { onQuery("") }, ghost = true)
            }
        }
        items(visible, key = { it.id }) { person ->
            ZCard(
                onClick = if (person.self) null else { { onEvent(GroupEvent.Direct(person.id)) } },
                tag = "Group.Person.${person.id}",
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        Text(person.name, style = Zapara.typography.bodyStrong, color = c.text1, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text("@${person.handle}", style = Zapara.typography.caption, color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                    RoleChip(person.role)
                }
            }
        }
        if (state.directs.isNotEmpty()) {
            item { Text(stringResource(R.string.group_directs), style = Zapara.typography.bodyStrong, color = c.text2) }
            items(state.directs, key = { it.id }) { chat ->
                ZCard(onClick = { onEvent(GroupEvent.OpenChat(chat.id, chat.title)) }, tag = "Group.Direct.${chat.id}", modifier = Modifier.fillMaxWidth()) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                            Text(chat.title, style = Zapara.typography.bodyStrong, color = c.text1, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            if (chat.preview.isNotEmpty()) {
                                Text(chat.preview, style = Zapara.typography.caption, color = c.text2, maxLines = 2, overflow = TextOverflow.Ellipsis)
                            }
                        }
                        UnreadBadge(chat.unread)
                    }
                }
            }
        }
    }
}

@Composable
private fun UnreadBadge(count: Int) {
    if (count <= 0) return
    val description = stringResource(R.string.group_unread_count, count)
    ZChip(if (count > 99) "99+" else count.toString(), selected = true,
        modifier = Modifier.semantics { contentDescription = description })
}

@Composable
private fun Messages(state: GroupUiState, onEvent: (GroupEvent) -> Unit, modifier: Modifier) {
    val c = Zapara.colors
    val listState = rememberLazyListState()
    val scope = rememberCoroutineScope()
    var query by rememberSaveable(state.activeConversationId, state.activeTopicId) { mutableStateOf("") }
    var author by rememberSaveable(state.activeConversationId, state.activeTopicId) { mutableStateOf(MessageAuthor.All.name) }
    var kind by rememberSaveable(state.activeConversationId, state.activeTopicId) { mutableStateOf(MessageKind.All.name) }
    var filtersOpen by rememberSaveable(state.activeConversationId, state.activeTopicId) { mutableStateOf(false) }
    val authorFilter = MessageAuthor.entries.firstOrNull { it.name == author } ?: MessageAuthor.All
    val kindFilter = MessageKind.entries.firstOrNull { it.name == kind } ?: MessageKind.All
    val visible = browseMessages(state.messages, query, authorFilter, kindFilter)
    val filtered = query.isNotBlank() || authorFilter != MessageAuthor.All || kindFilter != MessageKind.All
    var firstScrollDone by remember(state.activeConversationId, state.activeTopicId) { mutableStateOf(false) }
    val filterKey = Triple(query.trim(), authorFilter, kindFilter)
    var previousFilterKey by remember(state.activeConversationId, state.activeTopicId) { mutableStateOf(filterKey) }
    LaunchedEffect(state.activeConversationId, state.activeTopicId, state.messages.size, state.hasMore, filterKey) {
        if (filterKey != previousFilterKey) {
            previousFilterKey = filterKey
            firstScrollDone = true
            listState.scrollToItem(0)
        } else if (!firstScrollDone && state.messages.isNotEmpty()) {
            listState.scrollToItem(if (filtered) 0 else visible.lastIndex + if (state.hasMore) 1 else 0)
            firstScrollDone = true
        }
    }
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        OutlinedTextField(query, { query = it }, label = { Text(stringResource(R.string.group_message_search)) },
            singleLine = true, modifier = Modifier.fillMaxWidth().testTag("Group.MessageSearch"))
        Text(stringResource(R.string.group_message_scope), style = Zapara.typography.caption, color = c.text2)
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Text(stringResource(R.string.group_message_results, visible.size, state.messages.size),
                style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
            ZChip(stringResource(R.string.group_message_filters), selected = filtered,
                onClick = { filtersOpen = !filtersOpen }, tag = "Group.MessageFilters")
        }
        if (filtered) ZButton(stringResource(R.string.group_message_reset), {
            query = ""; author = MessageAuthor.All.name; kind = MessageKind.All.name
        }, ghost = true, tag = "Group.MessageReset")
        if (state.chatLoading && state.messages.isEmpty()) {
            Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                Text(stringResource(R.string.group_loading), style = Zapara.typography.caption, color = c.text2)
            }
        } else {
            val total = listState.layoutInfo.totalItemsCount
            val lastVisible = listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
            val showJump = visible.size > 4 && total > 0 && lastVisible < total - 2
            Box(Modifier.weight(1f).fillMaxWidth()) {
                LazyColumn(Modifier.fillMaxSize(), state = listState) {
                    if (state.hasMore && visible.isNotEmpty()) item {
                        ZButton(stringResource(if (state.olderLoading) R.string.group_older_loading else R.string.group_older),
                            { onEvent(GroupEvent.Older) }, enabled = !state.olderLoading, ghost = true, tag = "Group.Older")
                    }
                    if (visible.isEmpty()) item {
                        ZCard(Modifier.fillMaxWidth(), tag = if (filtered) "Empty.GroupMessageSearch" else "Empty.GroupMessages") {
                            Text(stringResource(if (filtered) R.string.group_message_no_results else R.string.group_no_messages),
                                style = Zapara.typography.caption, color = c.text2)
                            if (state.hasMore) ZButton(stringResource(if (state.olderLoading) R.string.group_older_loading else R.string.group_older),
                                { onEvent(GroupEvent.Older) }, enabled = !state.olderLoading, ghost = true, tag = "Group.Older")
                        }
                    }
                    itemsIndexed(visible, key = { _, message -> message.id }) { index, message ->
                        val grouped = !filtered && sameMessageCluster(visible.getOrNull(index - 1), message)
                        Column(Modifier.fillMaxWidth().padding(top = if (grouped) Zapara.space.xs else Zapara.space.s),
                            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                            if (message.day.isNotBlank() && (index == 0 || visible[index - 1].day != message.day))
                                Text(message.day, modifier = Modifier.align(Alignment.CenterHorizontally).testTag("Group.Day.${message.id}"),
                                    style = Zapara.typography.caption, color = c.text2)
                            MessageBubble(message, state.messages.firstOrNull { it.id == message.replyTo }?.body,
                                state.mediaLoadingId == message.id || message.id in state.mediaLoadingIds,
                                state.mediaFiles[message.id], message.id in state.mediaFailedIds, state.canPost,
                                showAuthor = !grouped, onEvent = onEvent)
                        }
                    }
                }
                if (showJump) ZButton(stringResource(R.string.channel_new_messages), {
                    val last = listState.layoutInfo.totalItemsCount - 1
                    if (last >= 0) scope.launch { listState.animateScrollToItem(last) }
                }, modifier = Modifier.align(Alignment.BottomEnd).padding(Zapara.space.s),
                    ghost = true, tag = "Group.JumpLatest")
            }
        }
    }
    if (filtersOpen) ZBottomSheet(onDismiss = { filtersOpen = false }, tag = "Group.MessageFilterSheet", scrollable = true) {
        Text(stringResource(R.string.group_message_filters), style = Zapara.typography.section, color = c.text1)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf(MessageAuthor.All to R.string.group_author_all, MessageAuthor.Mine to R.string.group_author_mine,
                MessageAuthor.Others to R.string.group_author_others).forEach { (value, label) ->
                ZChip(stringResource(label), selected = authorFilter == value,
                    onClick = { author = value.name }, tag = "Group.MessageAuthor.${value.name}",
                    modifier = Modifier.semantics { selected = authorFilter == value })
            }
        }
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf(MessageKind.All to R.string.group_kind_all, MessageKind.Text to R.string.group_kind_text,
                MessageKind.PhotoVideo to R.string.group_kind_photo_video,
                MessageKind.Documents to R.string.group_kind_documents,
                MessageKind.VoiceCircle to R.string.group_kind_voice_circle).forEach { (value, label) ->
                ZChip(stringResource(label), selected = kindFilter == value,
                    onClick = { kind = value.name }, tag = "Group.MessageKind.${value.name}",
                    modifier = Modifier.semantics { selected = kindFilter == value })
            }
        }
        ZButton(stringResource(R.string.group_message_filters_done), { filtersOpen = false },
            tag = "Group.MessageFiltersDone", modifier = Modifier.fillMaxWidth())
    }
}

private fun messageLabel(message: GroupMessageUi): String = when {
    message.deleted -> "Сообщение удалено"
    message.kind == "image" -> "Фото"
    message.kind == "video" || message.kind == "circle" -> "Видео"
    message.kind == "voice" -> "Голосовое"
    message.kind == "file" -> message.body.ifBlank { "Документ" }
    else -> message.body
}

private fun reactionEmoji(code: String): String = when (code) {
    "like" -> "👍"
    "heart" -> "❤️"
    "laugh" -> "😂"
    "wow" -> "😮"
    "sad" -> "😢"
    else -> code
}

@Composable
private fun MessageBubble(message: GroupMessageUi, replyPreview: String?, mediaLoading: Boolean,
    mediaFile: File?, mediaFailed: Boolean, canPost: Boolean, showAuthor: Boolean,
    onEvent: (GroupEvent) -> Unit) {
    val c = Zapara.colors
    val mine = message.mine
    val context = LocalContext.current
    var menu by remember(message.id) { mutableStateOf(false) }
    var deleting by remember(message.id) { mutableStateOf(false) }
    var reactionPicker by remember(message.id) { mutableStateOf(false) }
    var copyFeedback by remember(message.id) { mutableStateOf<String?>(null) }
    val actions = HoldDecision.actions(message.kind, mine, message.deleted, true)
        .filter { canPost || it !in setOf("reply", "edit") }
    val copyText = copyableMessageText(message)
    val canCopy = copyText != null
    val copiedText = stringResource(R.string.group_message_copied)
    val copyFailedText = stringResource(R.string.group_message_copy_failed)
    val clipboardLabel = stringResource(R.string.group_message_clip_label)
    LaunchedEffect(copyFeedback) {
        if (copyFeedback != null) {
            delay(2500)
            copyFeedback = null
        }
    }
    @Composable fun MessageActions() {
        Box {
            IconButton(onClick = { menu = true }, modifier = Modifier.size(Zapara.space.minTouch)
                .testTag("Group.MessageActions.${message.id}")) {
                Icon(painterResource(R.drawable.ic_menu),
                    stringResource(R.string.group_message_actions_detail, message.author, message.time), tint = c.text2)
            }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                if (copyText != null) DropdownMenuItem(text = { Text(stringResource(R.string.group_message_copy)) }, onClick = {
                    menu = false
                    copyFeedback = try {
                        val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                        clipboard.setPrimaryClip(ClipData.newPlainText(clipboardLabel, copyText))
                        copiedText
                    } catch (_: Exception) { copyFailedText }
                }, modifier = Modifier.testTag("Group.Copy.${message.id}"))
                actions.forEach { action ->
                    val label = when (action) {
                        "reply" -> R.string.group_message_reply
                        "reaction" -> R.string.group_message_react
                        "edit" -> R.string.group_message_edit
                        else -> R.string.group_message_delete
                    }
                    DropdownMenuItem(text = { Text(stringResource(label)) }, onClick = {
                        menu = false
                        HoldDecision.perform(action,
                            reply = { onEvent(GroupEvent.Hold(message.id, "reply")) },
                            reaction = { reactionPicker = true },
                            edit = { onEvent(GroupEvent.Hold(message.id, "edit")) },
                            delete = { deleting = true })
                    }, modifier = Modifier.testTag("Group.Action.${message.id}.$action"))
                }
            }
        }
    }
    Column(
        Modifier.fillMaxWidth().testTag("Group.Author.${message.id}"),
        horizontalAlignment = if (mine) Alignment.End else Alignment.Start,
        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)
    ) {
        if (!mine && showAuthor && message.author.isNotBlank()) {
            Text(message.author, style = Zapara.typography.caption, color = c.text2)
        }
        Row(verticalAlignment = Alignment.Bottom,
            horizontalArrangement = if (mine) Arrangement.End else Arrangement.Start) {
        if (mine && (actions.isNotEmpty() || canCopy)) MessageActions()
        Surface(
            shape = RoundedCornerShape(Zapara.radii.card),
            color = if (mine) c.chip else c.card,
            contentColor = c.text1,
            border = BorderStroke(Zapara.space.hairline, if (mine) c.lineStrong else c.line),
            modifier = Modifier.widthIn(max = 280.dp).combinedClickable(
                onClick = {
                    if (!mediaLoading && !message.deleted && message.kind in setOf("image", "video", "file")) {
                        onEvent(GroupEvent.OpenMedia(message.id))
                    }
                },
                onLongClick = { if (actions.isNotEmpty() || canCopy) menu = true }
            )
        ) {
            Column(
                Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)
            ) {
                if (message.replyTo != null) Text("↳ ${replyPreview?.take(80) ?: "Сообщение"}",
                    style = Zapara.typography.caption, color = c.text2,
                    maxLines = 2, overflow = TextOverflow.Ellipsis)
                if (!message.deleted && message.kind in setOf("image", "voice", "circle")) {
                    ChatMediaBubble(kind = message.kind, file = mediaFile, durationMs = null,
                        loading = mediaLoading, error = mediaFailed,
                        onLoad = { onEvent(GroupEvent.LoadMedia(message.id)) })
                } else {
                    Text(if (mediaLoading) stringResource(R.string.group_media_loading) else messageLabel(message), style = Zapara.typography.body)
                }
                Text(
                    message.time,
                    style = Zapara.typography.caption,
                    color = c.text2,
                    modifier = Modifier.align(Alignment.End)
                )
            }
        }
        if (!mine && (actions.isNotEmpty() || canCopy)) MessageActions()
        }
        if (!message.deleted && message.reactions.isNotEmpty()) FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            message.reactions.forEach { reaction ->
                ZChip("${reactionEmoji(reaction.emoji)} ${reaction.count}", selected = reaction.mine,
                    onClick = { onEvent(GroupEvent.React(message.id, reaction.emoji)) },
                    tag = "Group.Reaction.${message.id}.${reaction.emoji}")
            }
        }
        if (copyFeedback != null) Text(copyFeedback.orEmpty(), style = Zapara.typography.caption,
            color = c.text2, modifier = Modifier.testTag("Group.CopyFeedback.${message.id}")
                .semantics { liveRegion = LiveRegionMode.Polite })
        if (reactionPicker) FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf("like", "heart", "laugh", "wow", "sad").forEach { emoji ->
                ZChip(reactionEmoji(emoji), onClick = {
                    reactionPicker = false
                    onEvent(GroupEvent.React(message.id, emoji))
                }, tag = "Group.React.${message.id}.$emoji")
            }
        }
    }
    if (deleting) AlertDialog(onDismissRequest = { deleting = false },
        title = { Text(stringResource(R.string.group_message_delete_title)) },
        text = { Text(stringResource(R.string.group_message_delete_warning)) },
        confirmButton = { ZButton(stringResource(R.string.group_message_delete), {
            onEvent(GroupEvent.Hold(message.id, "delete")); deleting = false
        }, tag = "Group.DeleteConfirm.${message.id}") },
        dismissButton = { ZButton(stringResource(R.string.channel_cancel), { deleting = false }, ghost = true) })
}

@Composable
private fun Composer(state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val c = Zapara.colors
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var pendingPickConversation by remember { mutableStateOf<String?>(null) }
    var pendingPickTopic by remember { mutableStateOf<String?>(null) }
    fun pick(kind: String, uri: Uri?) {
        val origin = pendingPickConversation
        val topic = pendingPickTopic
        pendingPickConversation = null
        pendingPickTopic = null
        if (uri == null || origin == null) return
        scope.launch {
            val read = withContext(Dispatchers.IO) { readAttachment(context, uri) }
            onEvent(GroupEvent.Media(kind, read?.first ?: "", read?.second ?: ByteArray(0), origin, topic))
        }
    }
    val photo = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { pick("image", it) }
    val video = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { pick("video", it) }
    val document = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { pick("file", it) }
    var attachOpen by remember(state.chatTitle) { mutableStateOf(false) }
    key(state.activeConversationId, state.activeTopicId) {
    ChatMediaCaptureHost(enabled = !state.sending && state.editing == null,
        onRecorded = { kind, file, duration -> onEvent(GroupEvent.Recorded(kind, file, duration, state.activeConversationId, state.activeTopicId)) },
        onError = { onEvent(GroupEvent.MediaError) }, modifier = Modifier.fillMaxWidth()) { startVoice, startCircle ->
        Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            if (state.editing != null || state.replyTo != null) {
                val preview = state.messages.firstOrNull { it.id == state.replyTo }?.body?.take(60)
                Surface(shape = RoundedCornerShape(Zapara.radii.control), color = c.chip,
                    border = BorderStroke(Zapara.space.hairline, c.lineStrong),
                    modifier = Modifier.fillMaxWidth().testTag("Group.ComposeContext")) {
                    Row(Modifier.padding(Zapara.space.s), verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        Text(if (state.editing != null) stringResource(R.string.group_edit_context)
                            else stringResource(R.string.group_reply_context, preview ?: stringResource(R.string.group_message)),
                            style = Zapara.typography.caption, color = c.text1, modifier = Modifier.weight(1f))
                        ZButton(stringResource(R.string.channel_cancel), { onEvent(GroupEvent.CancelContext) }, ghost = true,
                            tag = "Group.CancelContext")
                    }
                }
            }
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                if (state.editing == null) Box {
                    IconButton(onClick = { attachOpen = true }, enabled = !state.sending,
                        modifier = Modifier.testTag("Group.Attach")) {
                        Icon(painterResource(R.drawable.ic_paperclip), stringResource(R.string.group_attach), tint = c.text1)
                    }
                    DropdownMenu(expanded = attachOpen, onDismissRequest = { attachOpen = false }) {
                        DropdownMenuItem(text = { Text(stringResource(R.string.chat_media_photo)) }, onClick = {
                            attachOpen = false; pendingPickConversation = state.activeConversationId; pendingPickTopic = state.activeTopicId; photo.launch(arrayOf("image/*")) },
                            modifier = Modifier.testTag("Group.Photo"))
                        DropdownMenuItem(text = { Text(stringResource(R.string.group_video)) }, onClick = {
                            attachOpen = false; pendingPickConversation = state.activeConversationId; pendingPickTopic = state.activeTopicId; video.launch(arrayOf("video/*")) },
                            modifier = Modifier.testTag("Group.Video"))
                        DropdownMenuItem(text = { Text(stringResource(R.string.group_document)) }, onClick = {
                            attachOpen = false; pendingPickConversation = state.activeConversationId; pendingPickTopic = state.activeTopicId; document.launch(arrayOf("*/*")) },
                            modifier = Modifier.testTag("Group.File"))
                    }
                }
                OutlinedTextField(value = state.draft, onValueChange = { onEvent(GroupEvent.Draft(it)) },
                    enabled = !state.sending, modifier = Modifier.weight(1f).testTag("Group.Draft"),
                    placeholder = { Text(stringResource(R.string.group_message), style = Zapara.typography.caption, color = c.text3) },
                    maxLines = 4, shape = RoundedCornerShape(Zapara.radii.control),
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
                        focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
                        focusedTextColor = c.text1, unfocusedTextColor = c.text1
                    ))
                if (state.draft.isNotBlank() || state.attachmentPending || state.editing != null) {
                    if (state.attachmentPending) ZButton(stringResource(R.string.group_retry), { onEvent(GroupEvent.Send) },
                        enabled = !state.sending, tag = "Group.Send")
                    else Surface(onClick = { onEvent(GroupEvent.Send) },
                        enabled = !state.sending && state.draft.isNotBlank(),
                        shape = RoundedCornerShape(Zapara.radii.icon), color = c.accent, contentColor = c.onAccent,
                        modifier = Modifier.size(Zapara.space.minTouch).testTag("Group.Send")) {
                        Box(contentAlignment = Alignment.Center) {
                            Icon(painterResource(R.drawable.ic_send), stringResource(R.string.group_send))
                        }
                    }
                } else {
                    IconButton(onClick = startCircle, enabled = !state.sending,
                        modifier = Modifier.testTag("Group.Circle")) {
                        Icon(painterResource(R.drawable.ic_video_circle), stringResource(R.string.group_record_circle), tint = c.text1)
                    }
                    IconButton(onClick = startVoice, enabled = !state.sending,
                        modifier = Modifier.testTag("Group.Voice")) {
                        Icon(painterResource(R.drawable.ic_mic), stringResource(R.string.group_record_voice), tint = c.text1)
                    }
                }
            }
        }
    }
    }
}

private fun readAttachment(context: android.content.Context, uri: Uri): Pair<String, ByteArray>? {
    val name = context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
        if (cursor.moveToFirst()) cursor.getString(0) else null
    }?.substringAfterLast('/')?.substringAfterLast('\\') ?: "Файл"
    val bytes = context.contentResolver.openInputStream(uri)?.use { input ->
        val out = ByteArrayOutputStream()
        val buf = ByteArray(8192)
        var total = 0
        while (true) {
            val n = input.read(buf)
            if (n < 0) break
            total += n
            if (total > GroupMedia.maxBytes) return null
            out.write(buf, 0, n)
        }
        out.toByteArray()
    } ?: return null
    if (bytes.isEmpty()) return null
    return name to bytes
}

@Composable
private fun RoleChip(value: String) {
    val staff = value == "headman" || value == "curator"
    ZChip(role(value), selected = staff)
}

@Composable
private fun role(value: String): String = stringResource(when (value) {
    "headman" -> R.string.group_role_headman
    "curator" -> R.string.group_role_curator
    else -> R.string.group_role_member
})
