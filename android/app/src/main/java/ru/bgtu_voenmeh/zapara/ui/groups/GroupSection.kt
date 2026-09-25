@file:OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class, androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package ru.bgtu_voenmeh.zapara.ui.groups

import android.net.Uri
import android.provider.OpenableColumns
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.platform.LocalContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.File
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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
        ZButton(stringResource(R.string.group_list), { onEvent(GroupEvent.Back) }, ghost = true, tag = "Group.List")
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Text(
                state.title,
                style = Zapara.typography.section,
                color = c.text1,
                modifier = Modifier.weight(1f),
                maxLines = 2,
                overflow = TextOverflow.Ellipsis
            )
            RoleChip(state.myRole)
        }
        Text(stringResource(R.string.group_disclaimer), style = Zapara.typography.caption, color = c.text2)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(
                stringResource(R.string.channel_list),
                { onEvent(GroupEvent.Chat) },
                modifier = Modifier.weight(1f),
                ghost = !state.showChannels,
                tag = "Group.Channels"
            )
            ZButton(
                stringResource(R.string.group_people),
                { onEvent(GroupEvent.People) },
                modifier = Modifier.weight(1f),
                ghost = !state.showPeople,
                tag = "Group.People"
            )
        }
        if (state.failed) Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.group_failed), color = c.bad, modifier = Modifier.weight(1f).testTag("Group.Error"))
            if (!state.attachmentPending) ZButton(stringResource(R.string.group_refresh),
                { onEvent(GroupEvent.Refresh) }, ghost = true)
        }
        if (state.mediaError) Text(stringResource(R.string.group_media_failed), color = c.bad, modifier = Modifier.testTag("Group.MediaError"))
        if (!state.showPeople && !state.showChannels) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                ZButton(stringResource(R.string.group_back), { onEvent(GroupEvent.Channels) }, ghost = true, tag = "Group.Back")
                Text(state.chatTitle, style = Zapara.typography.bodyStrong, color = c.text1,
                    modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
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
    LazyColumn(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item { Text(stringResource(R.string.channel_list_title), style = Zapara.typography.section, color = c.text1) }
        item {
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
        item {
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
            ZCard(onClick = { onEvent(GroupEvent.OpenChannel(channel.topicId)) },
                tag = "Group.Channel.${channel.topicId ?: "general"}", modifier = Modifier.fillMaxWidth()) {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    val accent = when (channel.accent) {
                        "blue" -> c.info
                        "green" -> c.ok
                        "purple" -> c.friends[3]
                        "orange" -> c.warn
                        "red" -> c.bad
                        else -> c.lineStrong
                    }
                    Surface(shape = RoundedCornerShape(Zapara.radii.icon), color = c.chip,
                        border = BorderStroke(2.dp, accent), modifier = Modifier.size(Zapara.space.minTouch)) {
                        Box(contentAlignment = Alignment.Center) { Text(channel.icon, style = Zapara.typography.section) }
                    }
                    Column(Modifier.weight(1f)) {
                        Text(channel.title, style = Zapara.typography.bodyStrong, color = c.text1)
                        if (channel.description.isNotBlank()) Text(channel.description, style = Zapara.typography.caption,
                            color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(if (channel.kind == "ballots") stringResource(R.string.channel_ballot_count, channel.activeBallots)
                            else channel.lastBody ?: stringResource(R.string.channel_no_messages), style = Zapara.typography.caption,
                            color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                    if (channel.unread > 0) ZChip(channel.unread.toString(), selected = true)
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
                if (channel.pinned || channel.writePolicy == "managers") FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    if (channel.pinned) ZChip(stringResource(R.string.channel_pinned))
                    if (channel.writePolicy == "managers") ZChip(stringResource(R.string.channel_writes_managers))
                }
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
            ZCard(onClick = { onEvent(GroupEvent.GlobalBallots(allBallotsTitle)) },
                tag = "Group.AllBallots", modifier = Modifier.fillMaxWidth()) {
                Text(stringResource(R.string.channel_all_ballots), style = Zapara.typography.bodyStrong, color = c.text1)
                Text(stringResource(R.string.channel_all_ballots_hint), style = Zapara.typography.caption, color = c.text2)
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
    if (state.chatLoading && board == null) {
        Box(modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            Text(stringResource(R.string.group_loading), color = c.text2)
        }
        return
    }
    var composing by remember(state.activeTopicId) { mutableStateOf(false) }
    LazyColumn(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item { Text(stringResource(R.string.channel_ballot_type), style = Zapara.typography.section, color = c.text1) }
        if (state.ballotRefreshFailed) item {
            Text(stringResource(R.string.channel_ballot_refresh_failed), style = Zapara.typography.caption, color = c.warn)
        }
        if (board == null) item { Text(stringResource(R.string.channel_ballot_load_failed), color = c.text2) }
        else {
            items(board.ballots, key = { it.ballotId }) { ballot ->
                BallotCard(ballot, board.canClose, state.channelBusy, onEvent)
            }
            if (board.ballots.isEmpty()) item { Text(stringResource(R.string.channel_ballot_empty), color = c.text2) }
            item {
                if (!state.canPost) Text(stringResource(R.string.channel_ballot_read_only), style = Zapara.typography.caption, color = c.text2)
                else if (composing) BallotEditor(state.channelBusy, board.canOpen,
                    onSave = { question, options, days, headman ->
                        onEvent(GroupEvent.CreateBallot(question, options, days, headman)); composing = false
                    }, onCancel = { composing = false })
                else ZButton(stringResource(R.string.channel_ballot_new), { composing = true }, enabled = !state.channelBusy,
                    tag = "Group.BallotCreate")
            }
        }
    }
}

@Composable
private fun BallotCard(ballot: Ballot, canClose: Boolean, busy: Boolean, onEvent: (GroupEvent) -> Unit) {
    val c = Zapara.colors
    var closing by remember(ballot.ballotId) { mutableStateOf(false) }
    val total = ballot.options.sumOf { it.votes }
    val deadline = remember(ballot.deadlineAt) {
        DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm").withZone(ZoneId.systemDefault()).format(ballot.deadlineAt)
    }
    ZCard(Modifier.fillMaxWidth(), tag = "Group.Ballot.${ballot.ballotId}") {
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZChip(stringResource(when (ballot.origin) { "system" -> R.string.channel_ballot_origin_system;
                "headman" -> R.string.channel_ballot_origin_headman; else -> R.string.channel_ballot_origin_collective }))
            ZChip(stringResource(when (ballot.status) { "collecting" -> R.string.channel_ballot_collecting; "open" -> R.string.channel_ballot_open; else -> R.string.channel_ballot_closed }))
            if (ballot.effect.isNotBlank()) ZChip(stringResource(R.string.channel_ballot_effect))
        }
        Text(ballot.question, style = Zapara.typography.bodyStrong, color = c.text1)
        Text(stringResource(R.string.channel_ballot_deadline, deadline), style = Zapara.typography.caption, color = c.text2)
        if (ballot.status == "collecting") {
            Text(stringResource(R.string.channel_ballot_supporters, ballot.supporters, ballot.supportersNeeded), style = Zapara.typography.caption, color = c.text2)
            if (ballot.supported) ZChip(stringResource(R.string.channel_ballot_supported))
            else ZButton(stringResource(R.string.channel_ballot_support), { onEvent(GroupEvent.SupportBallot(ballot.ballotId)) },
                enabled = !busy, tag = "Group.BallotSupport.${ballot.ballotId}")
        }
        ballot.options.forEach { option ->
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (ballot.status == "open") ZButton(
                    (if (option.chosen) "✓ " else "") + option.label,
                    { onEvent(GroupEvent.VoteBallot(ballot.ballotId, option.optionId)) },
                    modifier = Modifier.weight(1f), enabled = !busy,
                    ghost = !option.chosen, tag = "Group.BallotVote.${ballot.ballotId}.${option.optionId}")
                else Text(option.label, style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                if (ballot.status != "collecting") Text("${option.votes} / $total", style = Zapara.typography.caption, color = c.text2)
            }
        }
        val outcome = when (ballot.outcome) {
            "accepted" -> R.string.channel_ballot_outcome_accepted
            "rejected" -> R.string.channel_ballot_outcome_rejected
            "skipped" -> R.string.channel_ballot_outcome_skipped
            else -> null
        }
        if (outcome != null) Text(stringResource(outcome), style = Zapara.typography.caption, color = c.text2)
        if (canClose && ballot.effect.isBlank() && ballot.status != "closed") ZButton(stringResource(R.string.channel_ballot_finish), { closing = true },
            enabled = !busy, ghost = true, tag = "Group.BallotClose.${ballot.ballotId}")
    }
    if (closing) AlertDialog(onDismissRequest = { closing = false }, title = { Text(stringResource(R.string.channel_ballot_finish_question)) },
        text = { Text(stringResource(R.string.channel_ballot_finish_warning)) },
        confirmButton = { ZButton(stringResource(R.string.channel_ballot_finish), { onEvent(GroupEvent.CloseBallot(ballot.ballotId)); closing = false }, enabled = !busy) },
        dismissButton = { ZButton(stringResource(R.string.channel_cancel), { closing = false }, ghost = true) })
}

@Composable
private fun BallotEditor(busy: Boolean, canOpen: Boolean,
    onSave: (String, List<String>, Int, Boolean) -> Unit, onCancel: () -> Unit) {
    var question by remember { mutableStateOf("") }
    var options by remember { mutableStateOf(listOf("", "")) }
    var days by remember { mutableStateOf(3) }
    ZCard(Modifier.fillMaxWidth(), tag = "Group.BallotEditor") {
        Text(stringResource(R.string.channel_ballot_one_question), style = Zapara.typography.bodyStrong)
        OutlinedTextField(question, { question = it.take(400) }, label = { Text(stringResource(R.string.channel_ballot_question)) },
            modifier = Modifier.fillMaxWidth().testTag("Group.BallotQuestion"))
        options.forEachIndexed { index, option ->
            OutlinedTextField(option, { next -> options = options.mapIndexed { i, value -> if (i == index) next.take(80) else value } },
                label = { Text(stringResource(R.string.channel_ballot_option, index + 1)) }, singleLine = true,
                modifier = Modifier.fillMaxWidth().testTag("Group.BallotOption.$index"))
        }
        if (options.size < 6) ZButton(stringResource(R.string.channel_ballot_add_option), { options = options + "" }, ghost = true)
        if (options.size > 2) ZButton(stringResource(R.string.channel_ballot_remove_option), { options = options.dropLast(1) }, ghost = true)
        Text(stringResource(R.string.channel_ballot_duration), style = Zapara.typography.caption)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf(1, 3, 7, 14).forEach { value ->
                ZChip(stringResource(R.string.channel_ballot_days, value), selected = days == value, onClick = { days = value }, tag = "Group.BallotDays.$value")
            }
        }
        val valid = question.isNotBlank() && options.size in 2..6 && options.all { it.isNotBlank() } &&
            options.map { it.trim() }.distinct().size == options.size
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.channel_ballot_propose), { onSave(question.trim(), options.map { it.trim() }, days, false) },
                enabled = valid && !busy, tag = "Group.BallotPropose")
            if (canOpen) ZButton(stringResource(R.string.channel_ballot_announce), { onSave(question.trim(), options.map { it.trim() }, days, true) },
                enabled = valid && !busy, ghost = true, tag = "Group.BallotOpen")
            ZButton(stringResource(R.string.channel_cancel), onCancel, ghost = true)
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
                    if (state.groupUnread > 0) ZChip(state.groupUnread.toString(), selected = true)
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
                        if (chat.unread > 0) ZChip(chat.unread.toString(), selected = true)
                    }
                }
            }
        }
    }
}

@Composable
private fun Messages(state: GroupUiState, onEvent: (GroupEvent) -> Unit, modifier: Modifier) {
    val c = Zapara.colors
    val listState = rememberLazyListState()
    val scope = rememberCoroutineScope()
    var firstScrollDone by remember(state.activeConversationId, state.activeTopicId) { mutableStateOf(false) }
    LaunchedEffect(state.activeConversationId, state.activeTopicId, state.messages.size, state.hasMore) {
        if (!firstScrollDone && state.messages.isNotEmpty()) {
            listState.scrollToItem(state.messages.lastIndex + if (state.hasMore) 1 else 0)
            firstScrollDone = true
        }
    }
    if (state.chatLoading && state.messages.isEmpty()) {
        Box(modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            Text(stringResource(R.string.group_loading), style = Zapara.typography.caption, color = c.text2)
        }
    } else if (state.messages.isEmpty() && !state.hasMore) {
        Box(modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            Text(stringResource(R.string.group_no_messages), style = Zapara.typography.caption, color = c.text2)
        }
    } else {
        val total = listState.layoutInfo.totalItemsCount
        val lastVisible = listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
        val showJump = state.messages.size > 4 && total > 0 && lastVisible < total - 2
        Box(modifier.fillMaxWidth()) {
        LazyColumn(Modifier.fillMaxSize(), state = listState,
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            if (state.hasMore) {
                item {
                    ZButton(stringResource(R.string.group_older), { onEvent(GroupEvent.Older) }, ghost = true, tag = "Group.Older")
                }
            }
            if (state.messages.isEmpty()) {
                item {
                    Text(stringResource(R.string.group_no_messages), style = Zapara.typography.caption, color = c.text2)
                }
            }
            items(state.messages, key = { it.id }) { message ->
                MessageBubble(message, state.messages.firstOrNull { it.id == message.replyTo }?.body,
                    state.mediaLoadingId == message.id || message.id in state.mediaLoadingIds,
                    state.mediaFiles[message.id], message.id in state.mediaFailedIds, state.canPost, onEvent)
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
    mediaFile: File?, mediaFailed: Boolean, canPost: Boolean, onEvent: (GroupEvent) -> Unit) {
    val c = Zapara.colors
    val mine = message.mine
    var menu by remember(message.id) { mutableStateOf(false) }
    var reactionPicker by remember(message.id) { mutableStateOf(false) }
    val actions = HoldDecision.actions(message.kind, mine, message.deleted, menu)
        .filter { canPost || it !in setOf("reply", "edit") }
    Column(
        Modifier.fillMaxWidth().testTag("Group.Author.${message.id}"),
        horizontalAlignment = if (mine) Alignment.End else Alignment.Start,
        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)
    ) {
        if (!mine && message.author.isNotBlank()) {
            Text(message.author, style = Zapara.typography.caption, color = c.text2)
        }
        Surface(
            shape = RoundedCornerShape(Zapara.radii.card),
            color = if (mine) c.accent else c.card,
            contentColor = if (mine) c.onAccent else c.text1,
            border = if (mine) null else BorderStroke(Zapara.space.hairline, c.line),
            modifier = Modifier.widthIn(max = 280.dp).combinedClickable(
                onClick = {
                    if (!mediaLoading && !message.deleted && message.kind in setOf("image", "video", "file")) {
                        onEvent(GroupEvent.OpenMedia(message.id))
                    }
                },
                onLongClick = { menu = true }
            )
        ) {
            Column(
                Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)
            ) {
                if (message.replyTo != null) Text("↳ ${replyPreview?.take(80) ?: "Сообщение"}",
                    style = Zapara.typography.caption, color = if (mine) c.onAccent else c.text2,
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
                    color = if (mine) c.onAccent.copy(alpha = 0.72f) else c.text2,
                    modifier = Modifier.align(Alignment.End)
                )
            }
        }
        if (!message.deleted && message.reactions.isNotEmpty()) FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            message.reactions.forEach { reaction ->
                ZChip("${reactionEmoji(reaction.emoji)} ${reaction.count}", selected = reaction.mine,
                    onClick = { onEvent(GroupEvent.React(message.id, reaction.emoji)) },
                    tag = "Group.Reaction.${message.id}.${reaction.emoji}")
            }
        }
        if (menu) Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            actions.forEach { action ->
                val label = when (action) {
                    "reply" -> "Ответить"
                    "reaction" -> "Реакция"
                    "edit" -> "Изменить"
                    else -> "Удалить"
                }
                ZButton(label, {
                    HoldDecision.perform(action,
                        reply = { onEvent(GroupEvent.Hold(message.id, "reply")) },
                        reaction = { reactionPicker = true },
                        edit = { onEvent(GroupEvent.Hold(message.id, "edit")) },
                        delete = { onEvent(GroupEvent.Hold(message.id, "delete")) }
                    )
                    menu = false
                }, ghost = true)
            }
        }
        if (reactionPicker) FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf("like", "heart", "laugh", "wow", "sad").forEach { emoji ->
                ZChip(reactionEmoji(emoji), onClick = {
                    reactionPicker = false
                    onEvent(GroupEvent.React(message.id, emoji))
                }, tag = "Group.React.${message.id}.$emoji")
            }
        }
    }
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
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Text(if (state.editing != null) "Редактирование" else "Ответ · ${preview ?: "Сообщение"}",
                        style = Zapara.typography.caption, color = c.text2, maxLines = 2, overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f))
                    ZButton(stringResource(R.string.channel_cancel), { onEvent(GroupEvent.CancelContext) }, ghost = true,
                        tag = "Group.CancelContext")
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
