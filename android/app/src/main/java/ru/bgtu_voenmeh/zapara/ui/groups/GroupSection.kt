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
import androidx.compose.ui.platform.LocalDensity
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
import androidx.compose.foundation.layout.BoxWithConstraints
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
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.runtime.Composable
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.ui.Alignment
import ru.bgtu_voenmeh.zapara.ui.components.rememberUiText
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
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
import ru.bgtu_voenmeh.zapara.data.communities.GroupTemplates
import ru.bgtu_voenmeh.zapara.ui.chat.HoldDecision
import ru.bgtu_voenmeh.zapara.ui.chat.KeepLatestVisible
import ru.bgtu_voenmeh.zapara.ui.chat.ChatAvatar
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import ru.bgtu_voenmeh.zapara.ui.chat.AvatarEditor
import ru.bgtu_voenmeh.zapara.data.avatars.AvatarKind
import ru.bgtu_voenmeh.zapara.data.avatars.AvatarTarget
import ru.bgtu_voenmeh.zapara.ui.media.ChatMediaBubble
import ru.bgtu_voenmeh.zapara.ui.media.ChatMediaCaptureHost
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.chat.chatMessagePreview
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.shell.rememberKeyboardVisible
import ru.bgtu_voenmeh.zapara.ui.components.RevisionGuard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZDisclosureButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun GroupSection(state: GroupUiState, onEvent: (GroupEvent) -> Unit,
    onReturnToInbox: (() -> Unit)? = null, onOpenHomework: (Long) -> Unit = {}) {
    val uiText = rememberUiText()
    GroupObligationsSheet(state, onEvent)
    var groupSearch by rememberSaveable { mutableStateOf("") }
    var peopleSearch by rememberSaveable(state.title) { mutableStateOf("") }
    var channelSearch by rememberSaveable(state.title) { mutableStateOf("") }
    var channelKind by rememberSaveable(state.title) { mutableStateOf("all") }
    var unreadOnly by rememberSaveable(state.title) { mutableStateOf(false) }
    if (state.hasHome && state.spacePanel == null) BackHandler {
        when {
            state.showTrusted -> onEvent(GroupEvent.CloseTrusted)
            onReturnToInbox != null -> onReturnToInbox()
            state.showChannels -> onEvent(GroupEvent.Back)
            else -> onEvent(GroupEvent.Channels)
        }
    }
    Column(Modifier.fillMaxSize()) {
    if (!isChannelDetail(state)) ZTopBar(stringResource(R.string.group_title))
        when {
            state.accessRevoked -> EmptyState(R.drawable.ic_chat, uiText(R.string.space_day_55), actionText = stringResource(R.string.group_retry), onAction = { onEvent(GroupEvent.Refresh) })
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
                unreadOnly, { unreadOnly = it }, onReturnToInbox, onOpenHomework)
        }
    }
    GroupSpaceManagement(state, onEvent)
    if (state.showSubjectTasks) ZBottomSheet(onDismiss = { onEvent(GroupEvent.CloseSubjectTasks) }, tag = "Group.SubjectTasks", scrollable = true) {
        state.subjectDetail?.let { task ->
            Text(task.title,style=Zapara.typography.section); Text(task.body,style=Zapara.typography.body)
            Text(task.due?.toString() ?: uiText(R.string.space_day_61),style=Zapara.typography.caption)
            if (task.sharedId != null) Row(verticalAlignment=Alignment.CenterVertically) {
                androidx.compose.material3.Checkbox(task.done,{ onEvent(GroupEvent.SpaceAction(GroupSpaceAction.CompleteHomework(task.sharedId,it))) },enabled=!state.channelBusy && state.preview==null)
                Text(uiText(R.string.space_day_25),style=Zapara.typography.body)
            }
            ZButton(uiText(R.string.space_day_22), { onEvent(GroupEvent.CloseSubjectTasks); onEvent(GroupEvent.Context("${task.title} · ${task.due ?: ""} · ${task.body}")) }, enabled=state.canPost && state.preview==null,ghost=true)
        } ?: state.subjectHomework.forEach { task -> SubjectTaskRow(task, {
            if (task.localId != null) { onEvent(GroupEvent.CloseSubjectTasks); onOpenHomework(task.localId) } else onEvent(GroupEvent.SubjectDetail(task))
        }) }
    }
}

private fun isChannelDetail(state: GroupUiState): Boolean = state.hasHome &&
    !state.showChannels && !state.showPeople && !state.guest && !state.accessRevoked &&
    state.activeChannelKind in setOf("chat", "materials", "forms", "ballots", "homework", "schedule") &&
    state.channels.firstOrNull { it.topicId == state.activeTopicId }?.supported != false

@Composable
private fun CommunityList(state: GroupUiState, onEvent: (GroupEvent) -> Unit, query: String,
                          onQuery: (String) -> Unit, modifier: Modifier = Modifier.fillMaxSize()) {
    val uiText = rememberUiText()
    val visible = browseCommunities(state.communities, query)
    LazyColumn(
        modifier,
        contentPadding = PaddingValues(Zapara.space.l),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
    ) {
        item {
            ZTextField(query, onQuery, label = { Text(stringResource(R.string.group_search)) },
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
                    ChatAvatar(item.name, AvatarTarget(AvatarKind.Group, item.id), 40.dp)
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
                 unreadOnly: Boolean, onUnreadOnly: (Boolean) -> Unit,
                 onReturnToInbox: (() -> Unit)?, onOpenHomework: (Long) -> Unit) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    val channelState = rememberSaveableStateHolder()
    val conversation = isChannelDetail(state)
    val keyboardVisible = rememberKeyboardVisible()
    val browsingWithKeyboard = (state.showChannels || state.showPeople) && keyboardVisible
    var detailsOpen by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(false) }
    var groupInfoOpen by rememberSaveable(state.communityId) { mutableStateOf(false) }
    var searchOpen by rememberSaveable(state.activeConversationId, state.activeTopicId) { mutableStateOf(false) }
    var avatarOpen by rememberSaveable(state.communityId) { mutableStateOf(false) }
    var recentTopics by rememberSaveable(state.communityId) { mutableStateOf(listOf<String>()) }
    LaunchedEffect(state.communityId, state.activeTopicId, state.preview,
        state.channels.map { it.topicId }) {
        val id = state.activeTopicId
        if (state.preview == null && id != null && state.channels.any { it.topicId == id })
            recentTopics = (listOf(id) + recentTopics.filterNot { it == id }).take(3)
    }
    val groupAvatar = state.communityId?.let { AvatarTarget(AvatarKind.Group, it) }
    val peerAvatar = state.directs.firstOrNull { it.id == state.activeConversationId }?.peerUserId
        ?.let { AvatarTarget(AvatarKind.User, it) }
    if (avatarOpen && state.canManageChannels && state.preview == null && groupAvatar != null)
        ZBottomSheet(onDismiss = { avatarOpen = false }, tag = "Group.AvatarSheet", scrollable = true) {
            AvatarEditor(state.title, groupAvatar, enabled = !state.loading)
        }
    if (groupInfoOpen) ZBottomSheet(onDismiss = { groupInfoOpen = false },
        tag = "Group.InfoSheet", scrollable = true) {
        Text(state.title, style = Zapara.typography.section, color = c.text1)
        RoleChip(state.myRole)
        Text(stringResource(R.string.group_disclaimer), style = Zapara.typography.caption, color = c.text2)
        if (state.canManageChannels && state.preview == null && groupAvatar != null)
            ZButton(stringResource(R.string.avatar_group), { groupInfoOpen = false; avatarOpen = true },
                ghost = true, tag = "Group.EditAvatar")
    }
    Column(
        Modifier.fillMaxSize().padding(horizontal = Zapara.space.l,
            vertical = if (conversation) Zapara.space.s else Zapara.space.l),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
    ) {
        if (state.showChannels || state.showPeople) {
            if (!browsingWithKeyboard) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZIconButton(R.drawable.ic_chevron_left, stringResource(R.string.group_list),
                    { onReturnToInbox?.invoke() ?: onEvent(GroupEvent.Back) }, "Group.List")
                ChatAvatar(state.title, groupAvatar, 32.dp)
                Text(state.title, style = Zapara.typography.bodyStrong, color = c.text1,
                    modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                ZIconButton(R.drawable.ic_menu, stringResource(R.string.chat_polish_group_info),
                    { groupInfoOpen = true }, "Group.Info")
            }
            ZSegmented(listOf(stringResource(R.string.channel_list), stringResource(R.string.group_people),
                stringResource(R.string.channel_general)),
                selected = if (state.showPeople) 1 else 0,
                onSelect = { onEvent(when (it) {
                    1 -> GroupEvent.People
                    2 -> GroupEvent.OpenChannel(null)
                    else -> GroupEvent.Chat
                }) },
                tag = "Group.Panes", modifier = Modifier.fillMaxWidth())
            }
        } else {
            BoxWithConstraints(Modifier.fillMaxWidth()) {
                if (maxWidth < 320.dp || LocalDensity.current.fontScale >= 1.5f) {
                    Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            ZIconButton(R.drawable.ic_chevron_left, stringResource(R.string.group_back),
                                { onReturnToInbox?.invoke() ?: onEvent(GroupEvent.Channels) }, "Group.Back")
                            ChatAvatar(if (state.direct) state.chatTitle else state.title,
                                if (state.direct) peerAvatar else groupAvatar, 32.dp)
                            Spacer(Modifier.weight(1f))
                            if (conversation && state.activeChannelKind == "chat") ZIconButton(R.drawable.ic_search,
                                stringResource(R.string.group_message_search), { searchOpen = true }, "Group.MessageFilters")
                            if (conversation) ZIconButton(R.drawable.ic_menu, stringResource(R.string.chat_details),
                                { detailsOpen = true }, "Group.Details")
                        }
                        Text(state.title, style = Zapara.typography.caption, color = c.text2,
                            modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), softWrap = false)
                        Text(state.chatTitle, style = Zapara.typography.bodyStrong, color = c.text1,
                            modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), softWrap = false)
                    }
                } else Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZIconButton(R.drawable.ic_chevron_left, stringResource(R.string.group_back),
                        { onReturnToInbox?.invoke() ?: onEvent(GroupEvent.Channels) }, "Group.Back")
                    ChatAvatar(if (state.direct) state.chatTitle else state.title,
                        if (state.direct) peerAvatar else groupAvatar, 32.dp)
                    Column(Modifier.weight(1f)) {
                        Text(state.title, style = Zapara.typography.caption, color = c.text2,
                            modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), softWrap = false)
                        Text(state.chatTitle, style = Zapara.typography.bodyStrong, color = c.text1,
                            modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), softWrap = false)
                    }
                    if (conversation && state.activeChannelKind == "chat") ZIconButton(R.drawable.ic_search,
                        stringResource(R.string.group_message_search), { searchOpen = true }, "Group.MessageFilters")
                    if (conversation) ZIconButton(R.drawable.ic_menu, stringResource(R.string.chat_details),
                        { detailsOpen = true }, "Group.Details")
                }
            }
        }
        val details: @Composable () -> Unit = {
        val nextUnread = nextUnreadChannel(state.channels, state.activeTopicId,
            inChannel = !state.showChannels && !state.showPeople && !state.direct &&
                (state.activeConversationId != null || state.activeTopicId != null))
        if (!state.direct && nextUnread != null) ZButton(stringResource(R.string.group_next_unread),
            { nextUnread?.let { onEvent(GroupEvent.OpenChannel(it.topicId)) } },
            ghost = true, tag = "Group.NextUnread")
        if (state.failed) Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.group_failed), color = c.bad, modifier = Modifier.weight(1f).testTag("Group.Error"))
            if (!state.attachmentPending) ZButton(stringResource(R.string.group_refresh),
                { onEvent(GroupEvent.Refresh) }, ghost = true)
        }
        if (state.activeArchivedTopic != null) Text(uiText(R.string.review_archive_readonly),style=Zapara.typography.caption,color=c.text2)
        if (state.preview != null) ZCard(Modifier.fillMaxWidth(), tag = "Group.PreviewBanner") {
            Text(uiText(R.string.review_preview_subject, state.previewSubject.orEmpty()), style = Zapara.typography.section)
            Text(uiText(R.string.review_preview_readonly), style = Zapara.typography.caption)
            val topic = GroupActions.topic(state, state.activeTopicId)
            if (!state.showChannels && topic != null) {
                Text(uiText(R.string.review_preview_permissions), style = Zapara.typography.caption)
                FlowRow { topic.permissions.filterNot { it in ru.bgtu_voenmeh.zapara.data.communities.GroupAccessPresets.groupOnly }.forEach { power -> ZChip(stringResource(powerResource(power)), selected = true) } }
                if(topic.permissions.any { it in ru.bgtu_voenmeh.zapara.data.communities.GroupAccessPresets.groupOnly }) {
                    Text(uiText(R.string.review_global_permissions),style=Zapara.typography.caption)
                    FlowRow { topic.permissions.filter { it in ru.bgtu_voenmeh.zapara.data.communities.GroupAccessPresets.groupOnly }.forEach { power -> ZChip(stringResource(powerResource(power)),selected=true) } }
                }
            }
            ZButton(uiText(R.string.space_day_73), { onEvent(GroupEvent.SpaceAction(GroupSpaceAction.EndPreview)) }, ghost = true)
        }
        if (state.mediaError) Text(stringResource(R.string.group_media_failed), color = c.bad, modifier = Modifier.testTag("Group.MediaError"))
        if (!state.showPeople && !state.showChannels && !state.direct) {
            QuickChannels(state, onEvent)
            if (state.activeChannelKind == "chat") {
                val active = GroupActions.topic(state,state.activeTopicId)
                if (active?.template == "subject") SubjectContextCard(state,onEvent,onOpenHomework)
                else {
                    val context = groupChatContext(state.channels, state.contextLesson)
                    if (context.hasContent) GroupContextCard(state, context, onEvent)
                }
            }
        }
        }
        if (!conversation) {
            if (!browsingWithKeyboard) details()
        }
        else {
            if (state.failed || state.mediaError) Text(
                stringResource(if (state.mediaError) R.string.group_media_failed else R.string.group_failed),
                style = Zapara.typography.caption, color = c.bad, maxLines = 2,
                overflow = TextOverflow.Ellipsis, modifier = Modifier.testTag("Group.ErrorSummary"))
            if (state.preview != null) Text(uiText(R.string.review_preview_readonly),
                style = Zapara.typography.caption, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (state.activeArchivedTopic != null) Text(uiText(R.string.review_archive_readonly),
                style = Zapara.typography.caption, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (detailsOpen) ZBottomSheet(onDismiss = { detailsOpen = false },
                tag = "Group.DetailsSheet", scrollable = true) {
                Text(state.title, style = Zapara.typography.section)
                Text(state.chatTitle, style = Zapara.typography.bodyStrong)
                if (!state.direct) Text(stringResource(R.string.group_disclaimer),
                    style = Zapara.typography.caption, color = c.text2)
                if (!state.direct && state.canManageChannels && state.preview == null && groupAvatar != null)
                    ZButton(stringResource(R.string.avatar_group), { detailsOpen = false; avatarOpen = true },
                        ghost = true, tag = "Group.EditAvatar")
                details()
            }
        }
        state.obligationFocusId?.takeIf { !state.showChannels && !state.showPeople }?.let {
            ZButton(stringResource(R.string.ux300_group_obligations_show_channel), { onEvent(GroupEvent.ClearObligationFocus) }, ghost = true)
        }
        val focused = state.obligationFocusId?.let { id -> state.copy(forms = state.forms.filter { it.formId == id },
            homework = state.homework.filter { it.homeworkId == id }, board = state.board?.let { it.copy(ballots = it.ballots.filter { it.ballotId == id }) }) } ?: state
        channelState.SaveableStateProvider("${state.communityId}:${state.activeTopicId}:${state.activeChannelKind}:${state.showChannels}:${state.showPeople}:${state.obligationFocusId}") {
        if (state.showPeople) {
            People(state, onEvent, peopleSearch, onPeopleSearch, Modifier.weight(1f))
        } else if (state.showChannels) {
            ChannelList(state, onEvent, channelSearch, onChannelSearch, channelKind, onChannelKind,
                unreadOnly, onUnreadOnly, Modifier.weight(1f), recentTopics)
        } else if (state.activeChannelKind == "ballots") {
            BallotChannel(focused, onEvent, Modifier.weight(1f))
        } else if (state.activeChannelKind == "materials") {
            MaterialList(state, onEvent, Modifier.weight(1f))
            if (state.canPost && state.preview == null) Composer(state, onEvent)
        } else if (state.activeChannelKind in setOf("forms", "homework", "schedule") || state.channels.firstOrNull { it.topicId == state.activeTopicId }?.supported == false) {
            SpecializedChannel(focused, onEvent, Modifier.weight(1f))
        } else {
            Messages(state, onEvent, Modifier.weight(1f), searchOpen, { searchOpen = false },
                showSearchAction = !conversation, onSearchOpen = { searchOpen = true })
            if (state.canPost && state.preview == null) Composer(state, onEvent)
            else Text(stringResource(R.string.channel_read_only), style = Zapara.typography.caption, color = c.text2)
        }
        }
    }
}

@Composable
private fun MaterialList(state: GroupUiState, onEvent: (GroupEvent) -> Unit, modifier: Modifier) {
    val context = LocalContext.current
    val saveAttachment = rememberGroupAttachmentSave(state, onEvent)
    val rows = state.messages.filterNot { it.deleted }
    LazyColumn(modifier, contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        if (state.chatLoading && rows.isEmpty()) item { Text(stringResource(R.string.next_material_loading),
            style = Zapara.typography.caption, color = Zapara.colors.text2) }
        else if (state.failed && rows.isEmpty()) item { ZCard(Modifier.fillMaxWidth()) {
            Text(stringResource(R.string.next_material_failed), color = Zapara.colors.bad)
            ZButton(stringResource(R.string.next_retry), { onEvent(GroupEvent.Refresh) }, ghost = true)
        } }
        else if (rows.isEmpty()) item { Text(stringResource(R.string.next_material_empty),
            style = Zapara.typography.caption, color = Zapara.colors.text2) }
        if (state.failed && rows.isNotEmpty()) item { ZCard(Modifier.fillMaxWidth()) {
            Text(stringResource(R.string.next_material_stale), color = Zapara.colors.warn)
            ZButton(stringResource(R.string.next_retry), { onEvent(GroupEvent.Refresh) }, ghost = true)
        } }
        if (state.hasMore) item { ZButton(stringResource(R.string.group_older), { onEvent(GroupEvent.Older) }, enabled = !state.olderLoading, ghost = true) }
        items(rows, key = { it.id }) { material ->
            val link = Regex("https?://[^\\s]+").find(material.body)?.value
            var deleting by remember(material.id) { mutableStateOf(false) }
            ZCard(Modifier.fillMaxWidth(), onClick = {
                if (material.kind != "text") onEvent(GroupEvent.OpenMedia(material.id))
                else if (link != null) context.startActivity(android.content.Intent(android.content.Intent.ACTION_VIEW, Uri.parse(link)))
            }) {
                Text(messageLabel(material), style = Zapara.typography.bodyStrong)
                Text("${material.author} · ${material.time}", style = Zapara.typography.caption, color = Zapara.colors.text2)
                if ((material.mine || GroupActions.canModerate(state)) && state.preview == null && state.activeArchivedTopic == null) ZButton(stringResource(R.string.group_message_delete), { deleting = true }, ghost = true)
                if (material.kind in setOf("image", "voice", "circle")) ChatMediaBubble(material.kind, state.mediaFiles[material.id], null,
                    material.id in state.mediaLoadingIds, material.id in state.mediaFailedIds, { onEvent(GroupEvent.LoadMedia(material.id)) })
                if (!material.deleted && material.kind in setOf("image", "video", "file", "voice", "circle"))
                    ZButton(stringResource(R.string.ux60_chat_group_save), { saveAttachment(material) },
                        enabled = material.id !in state.mediaSavingIds, ghost = true,
                        tag = "Group.SaveMedia.${material.id}")
                when {
                    material.id in state.mediaSavingIds -> Text(stringResource(R.string.ux60_chat_group_saving), style = Zapara.typography.caption)
                    material.id in state.mediaSavedIds -> Text(stringResource(R.string.ux60_chat_group_saved), style = Zapara.typography.caption, color = Zapara.colors.ok)
                    material.id in state.mediaSaveFailedIds -> Text(stringResource(R.string.ux60_chat_group_save_failed), style = Zapara.typography.caption, color = Zapara.colors.bad)
                }
            }
            if (deleting) AlertDialog(onDismissRequest = { deleting = false },title = { Text(stringResource(R.string.group_message_delete)) },
                text = { Text(stringResource(R.string.group_message_delete_warning)) },
                confirmButton = { ZButton(stringResource(R.string.group_message_delete), { onEvent(GroupEvent.Hold(material.id,"delete")); deleting=false }) },
                dismissButton = { ZButton(stringResource(R.string.channel_cancel), { deleting=false },ghost=true) })
        }
    }
}

@Composable
private fun QuickChannels(state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    val real = state.channels.filter { it.kind == "chat" || it.topicId != null }
    if (real.isEmpty()) return
    LazyRow(Modifier.fillMaxWidth().testTag("Group.QuickChannels"),
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            item(key = "all-channels") {
                ZButton(stringResource(R.string.chat_design_all_channels), { onEvent(GroupEvent.Channels) },
                    ghost = true, tag = "Group.AllChannelsQuick")
            }
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
}

@Composable
private fun SubjectContextCard(state: GroupUiState, onEvent: (GroupEvent)->Unit, onOpenHomework:(Long)->Unit) {
    val uiText=rememberUiText()
    val topic=GroupActions.topic(state,state.activeTopicId)
    ZCard(Modifier.fillMaxWidth(),tag="Group.SubjectContext") {
        Text(topic?.subject.orEmpty(),style=Zapara.typography.section)
        state.subjectLesson?.let { lesson -> Text("${lesson.date} · ${lesson.time} · ${lesson.subject} · ${lesson.room}",style=Zapara.typography.body) }
            ?: Text(uiText(R.string.review_subject_no_lesson),style=Zapara.typography.caption)
        state.subjectHomework.take(3).forEach { task -> SubjectTaskRow(task,{ if (task.localId!=null) onOpenHomework(task.localId) else onEvent(GroupEvent.SubjectDetail(task)) }) }
        if (state.subjectHomework.isNotEmpty()) ZButton(uiText(R.string.review_subject_all_tasks,state.subjectHomework.size),{ onEvent(GroupEvent.SubjectTasks) },ghost=true)
        else Text(uiText(R.string.review_subject_no_tasks),style=Zapara.typography.caption)
    }
}
@Composable
private fun SubjectTaskRow(task:GroupSubjectHomeworkUi,onOpen:()->Unit) {
    val uiText=rememberUiText()
    ZCard(Modifier.fillMaxWidth(),onClick=onOpen) {
        Text(task.body,style=Zapara.typography.body)
        Text("${task.due?.toString() ?: uiText(R.string.space_day_61)} · ${uiText(if(task.sharedId==null) R.string.space_day_local_source else R.string.space_day_group_source)}",style=Zapara.typography.caption)
        if(task.done) Text(uiText(R.string.space_day_25),style=Zapara.typography.caption)
    }
}

@Composable
private fun GroupContextCard(state: GroupUiState, context: GroupChatContext, onEvent: (GroupEvent) -> Unit) {
    val uiText = rememberUiText()
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
                        unreadOnly: Boolean, onUnreadOnly: (Boolean) -> Unit, modifier: Modifier,
                        recentTopics: List<String>) {
    val uiText = rememberUiText()
    if (state.showTrusted) {
        TrustedPanel(state, onEvent, modifier)
        return
    }
    state.creationDraft?.takeIf { state.preview == null }?.let { draft ->
        LazyColumn(modifier.fillMaxWidth(), contentPadding = PaddingValues(bottom = Zapara.space.l)) {
            item(key = "creation") {
                TopicCreationEditor(draft, state,
                    onChange = { onEvent(GroupEvent.CreationChanged(it)) },
                    onCreate = { onEvent(GroupEvent.SubmitCreation) },
                    onCancel = { onEvent(GroupEvent.CancelCreation) })
            }
        }
        return
    }
    val c = Zapara.colors
    val allBallotsTitle = stringResource(R.string.channel_all_ballots)
    val categories = state.space?.categories.orEmpty()
    val largeText = LocalDensity.current.fontScale >= 1.5f
    val visible = browseChannels(state.preview ?: state.channels, query, kind, unreadOnly,
        categories.associate { it.categoryId to it.position }, categories.associate { it.categoryId to it.title })
    val filtered = query.isNotBlank() || kind != "all" || unreadOnly
    val activeFilterCount = listOf(query.isNotBlank(), kind != "all", unreadOnly).count { it }
    val kindScroll = rememberScrollState()
    LaunchedEffect(state.communityId, kind, unreadOnly) {
        if (kind == "all" && !unreadOnly) kindScroll.scrollTo(0)
    }
    var collapsed by rememberSaveable(state.communityId) { mutableStateOf(arrayListOf<String>()) }
    val showAllBallots = !unreadOnly && kind in setOf("all", "ballots") &&
        (query.isBlank() || allBallotsTitle.contains(query.trim(), ignoreCase = true))
    var managing by remember(state.title, state.canManageChannels) { mutableStateOf(false) }
    var editing by remember(state.title, state.canManageChannels) { mutableStateOf<GroupTopic?>(null) }
    var deleting by remember(state.title, state.canManageChannels) { mutableStateOf<GroupTopic?>(null) }
    val initialSaveVersion = remember(state.title) { state.channelSaveVersion }
    LaunchedEffect(state.channelSaveVersion) { if (state.channelSaveVersion > initialSaveVersion) { editing = null } }
    var searchOpen by rememberSaveable(state.communityId) { mutableStateOf(false) }
    var toolsOpen by rememberSaveable(state.communityId) { mutableStateOf(false) }
    LazyColumn(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.channel_list_title), style = Zapara.typography.section,
                    color = c.text1, modifier = Modifier.weight(1f))
                IconButton(onClick = {
                    searchOpen = !searchOpen
                }, modifier = Modifier.size(Zapara.space.minTouch).testTag("Group.ChannelSearchToggle")) {
                    Icon(painterResource(R.drawable.ic_search), stringResource(R.string.channel_search), tint = c.text1)
                }
                if (state.space != null || state.canManageChannels) IconButton(
                    onClick = { toolsOpen = !toolsOpen },
                    modifier = Modifier.size(Zapara.space.minTouch).testTag("Group.ToolsToggle")) {
                    Icon(painterResource(R.drawable.ic_menu),
                        stringResource(R.string.chat_polish_group_tools), tint = c.text1)
                }
            }
        }
        if (searchOpen) item {
            ZTextField(query, onQuery, label = { Text(stringResource(R.string.channel_search)) },
                placeholder = { Text(stringResource(R.string.channel_search_hint)) }, singleLine = true,
                modifier = Modifier.fillMaxWidth().testTag("Group.ChannelSearch"))
        }
        if (searchOpen) item {
            Row(Modifier.fillMaxWidth().horizontalScroll(kindScroll).testTag("Group.ChannelKinds"),
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf("all" to R.string.channel_filter_all, "chat" to R.string.channel_filter_chats,
                "ballots" to R.string.channel_filter_ballots,
                "materials" to R.string.ux60_chat_kind_materials,
                "forms" to R.string.ux60_chat_kind_forms,
                "homework" to R.string.ux60_chat_kind_homework,
                "schedule" to R.string.ux60_chat_kind_schedule).forEach { (value, label) ->
                    ZChip(stringResource(label), selected = kind == value, onClick = { onKind(value) },
                        tag = "Group.ChannelFilter.$value")
                }
                ZChip(stringResource(R.string.channel_filter_unread), selected = unreadOnly,
                    onClick = { onUnreadOnly(!unreadOnly) }, tag = "Group.ChannelFilter.Unread")
            }
        }
        if (filtered) item {
            Text(stringResource(R.string.chat_polish_active_filters, activeFilterCount),
                style = Zapara.typography.caption, color = c.text2,
                modifier = Modifier.testTag("Group.ActiveChannelFilters"))
            if (query.isNotBlank()) Text(query, maxLines = 1, overflow = TextOverflow.Ellipsis,
                style = Zapara.typography.caption, color = c.text2)
            if (kind != "all") Text(stringResource(when (kind) {
                "chat" -> R.string.channel_filter_chats
                "ballots" -> R.string.channel_filter_ballots
                "materials" -> R.string.ux60_chat_kind_materials
                "forms" -> R.string.ux60_chat_kind_forms
                "homework" -> R.string.ux60_chat_kind_homework
                "schedule" -> R.string.ux60_chat_kind_schedule
                else -> R.string.channel_filter_all
            }), style = Zapara.typography.caption, color = c.text2)
            if (unreadOnly) Text(stringResource(R.string.channel_filter_unread),
                style = Zapara.typography.caption, color = c.text2)
            if (androidx.compose.ui.platform.LocalDensity.current.fontScale >= 1.5f) {
                Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    Text(stringResource(R.string.channel_results, visible.size, state.channels.size),
                        style = Zapara.typography.caption, color = c.text2, modifier = Modifier.fillMaxWidth())
                    ZButton(stringResource(R.string.channel_reset_filters), {
                        onQuery(""); onKind("all"); onUnreadOnly(false)
                    }, ghost = true, tag = "Group.ChannelReset")
                }
            } else {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Text(stringResource(R.string.channel_results, visible.size, state.channels.size),
                    style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
                if (filtered) ZButton(stringResource(R.string.channel_reset_filters), {
                    onQuery(""); onKind("all"); onUnreadOnly(false)
                }, ghost = true, tag = "Group.ChannelReset")
            }
            }
        }
        val availableRecents = recentTopics.mapNotNull { id ->
            (state.preview ?: state.channels).firstOrNull { it.topicId == id }
        }
        if (availableRecents.isNotEmpty() && !filtered) item {
            Text(stringResource(R.string.ux300_android_recent_channels),
                style = Zapara.typography.caption, color = c.text2)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                availableRecents.forEach { topic -> ZButton(topic.title,
                    { onEvent(GroupEvent.OpenChannel(topic.topicId)) }, ghost = true,
                    tag = "Group.RecentChannel.${topic.topicId}") }
            }
        }
        state.spaceError?.let { error -> item {
            Text(error, color = c.bad, style = Zapara.typography.body)
        } }
        if (state.space != null && toolsOpen) item {
            if (state.preview != null) {
                Text(uiText(R.string.space_day_36), style = Zapara.typography.section)
                ZButton(uiText(R.string.space_day_37), { onEvent(GroupEvent.SpaceAction(GroupSpaceAction.EndPreview)) }, ghost = true)
            } else FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                ZButton(stringResource(R.string.ux300_group_obligations), { onEvent(GroupEvent.Obligations) }, ghost = true, tag = "Group.Obligations")
                val mine = state.desk?.mine.orEmpty()
                if (state.desk?.headman == true || "roles" in mine || "grants" in mine) ZButton(uiText(R.string.space_day_38), { onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("roles"))) }, ghost = true)
                if (state.canManageChannels) ZButton(uiText(R.string.space_day_39), { onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("categories"))) }, ghost = true)
                ZButton(uiText(R.string.space_day_40), { onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("archive"))) }, ghost = true)
                if (state.desk?.headman == true || "access" in mine) {
                    ZButton(uiText(R.string.space_day_41), { onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("preview"))) }, ghost = true)
                }
                if(state.desk?.headman==true || mine.any { it in setOf("channels","access","roles") }) ZButton(uiText(R.string.space_day_42),{ onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("audit"))) },ghost=true)
            }
        }
        if (state.canManageChannels && state.preview == null && toolsOpen) item {
            ZButton(stringResource(if (managing) R.string.channel_close_manage else R.string.channel_manage), {
                managing = !managing
                if (!managing) { editing = null }
            }, ghost = true, tag = "Group.ChannelManage")
        }
        state.creationNotice?.let { notice -> item { Text(notice, style=Zapara.typography.body, color=c.text1) } }
        if (state.preview==null && state.canManageChannels && managing && toolsOpen) item {
            ZButton(stringResource(R.string.channel_create), { onEvent(GroupEvent.BeginCreation) },
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
        visible.groupBy { it.categoryId }.forEach { (categoryId, categoryTopics) ->
            if (categoryId != null) item(key = "category:$categoryId") {
                val name = state.space?.categories?.firstOrNull { it.categoryId == categoryId }?.title.orEmpty()
                val expanded = categoryExpanded(categoryId, collapsed, filtered)
                ZDisclosureButton("$name · ${categoryTopics.size}", expanded = expanded, onClick = {
                    collapsed = if (categoryId in collapsed) ArrayList(collapsed - categoryId) else ArrayList(collapsed + categoryId)
                })
            }
            if (categoryExpanded(categoryId, collapsed, filtered)) items(categoryTopics, key = { it.topicId ?: "general" }) { channel ->
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
                    Column {
                    Row(Modifier.fillMaxWidth().heightIn(min = if (largeText) 64.dp else 48.dp)
                        .padding(vertical = Zapara.space.s),
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
                                    modifier = Modifier.weight(1f, fill = false), maxLines = 2,
                                    overflow = TextOverflow.Ellipsis)
                                if (channel.pinned) Icon(painterResource(R.drawable.ic_pin),
                                    stringResource(R.string.channel_pinned), tint = c.text2, modifier = Modifier.size(14.dp))
                                if (accent != null) Surface(shape = CircleShape, color = accent,
                                    modifier = Modifier.size(6.dp)) {}
                            }
                            if (largeText) Text(preview, style = Zapara.typography.caption, color = c.text2,
                                maxLines = 3, overflow = TextOverflow.Ellipsis)
                            if (largeText && channel.writePolicy == "managers") Text(stringResource(R.string.channel_writes_managers),
                                style = Zapara.typography.caption, color = c.text2)
                            if (largeText && (channel.lastAt != null || channel.unread > 0)) {
                                FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                                    verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                                    channel.lastAt?.let { lastAt ->
                                        Text(DateTimeFormatter.ofPattern("dd.MM HH:mm").withZone(ZoneId.systemDefault()).format(lastAt),
                                            style = Zapara.typography.caption, color = c.text2)
                                    }
                                    UnreadBadge(channel.unread)
                                }
                            }
                        }
                        if (!largeText && (channel.lastAt != null || channel.unread > 0)) {
                            Column(horizontalAlignment = Alignment.End) {
                                channel.lastAt?.let { lastAt ->
                                    Text(DateTimeFormatter.ofPattern("dd.MM HH:mm").withZone(ZoneId.systemDefault()).format(lastAt),
                                        style = Zapara.typography.caption, color = c.text2)
                                }
                                UnreadBadge(channel.unread)
                            }
                        }
                        if (channel.topicId != null && state.preview == null && (GroupActions.canManageTopic(state,channel) || GroupActions.canAccess(state, channel) || GroupActions.canPin(state, channel))) {
                            var menuOpen by remember(channel.topicId) { mutableStateOf(false) }
                            Box {
                                IconButton(onClick = { menuOpen = true }, enabled = !state.channelBusy,
                                    modifier = Modifier.testTag("Group.ChannelActions.${channel.topicId}")) {
                                    Icon(painterResource(R.drawable.ic_menu), stringResource(R.string.channel_more), tint = c.text1)
                                }
                                DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
                                    if (GroupActions.canManageTopic(state,channel)) DropdownMenuItem(text = { Text(stringResource(R.string.channel_edit)) }, onClick = {
                                        menuOpen = false; editing = channel
                                    }, modifier = Modifier.testTag("Group.ChannelEdit.${channel.topicId}"))
                                    if (GroupActions.canPin(state, channel)) DropdownMenuItem(text = { Text(uiText(if (channel.pinned) R.string.review_unpin_channel else R.string.channel_pin)) }, onClick = { menuOpen = false; onEvent(GroupEvent.PinChannel(channel, !channel.pinned)) })
                                    if (state.space != null) {
                                        if (GroupActions.canManageTopic(state,channel)) DropdownMenuItem(text = { Text(uiText(R.string.space_day_43)) }, onClick = { menuOpen = false; onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Archive(channel, true))) })
                                        if (GroupActions.canAccess(state, channel)) DropdownMenuItem(text = { Text(uiText(R.string.space_day_44)) }, onClick = { menuOpen = false; onEvent(GroupEvent.SpaceAction(GroupSpaceAction.LoadAccess(channel.topicId!!))) })
                                    }
                                    if (GroupActions.canManageTopic(state,channel) && channel.canDelete) DropdownMenuItem(text = { Text(stringResource(R.string.channel_delete)) }, onClick = {
                                        menuOpen = false; deleting = channel
                                    }, modifier = Modifier.testTag("Group.ChannelDelete.${channel.topicId}"))
                                }
                            }
                        }
                    }
                    if (!largeText) {
                        Text(preview, style = Zapara.typography.caption, color = c.text2,
                            maxLines = 2, overflow = TextOverflow.Ellipsis)
                        if (channel.writePolicy == "managers") Text(stringResource(R.string.channel_writes_managers),
                            style = Zapara.typography.caption, color = c.text2)
                    }
                    }
                }
                HorizontalDivider(color = c.line, thickness = Zapara.space.hairline)
            }
            if (GroupActions.canManageTopic(state,channel) && editing?.topicId == channel.topicId) {
                ChannelEditor(editing, state,
                    onSave = { title, icon, newKind, description, accent, pinned, policy, template, category, position, subject, baseRevision ->
                        channel.topicId?.let { onEvent(GroupEvent.RenameChannel(it, title, icon, newKind, description, accent, pinned, policy, if (state.space != null) template else null, category, position, subject, if (state.space != null) baseRevision else null)) }
                        },
                    onCancel = { editing = null })
            }
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
        if (state.myRole == "headman" && managing && state.space == null) item {
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
    val uiText = rememberUiText()
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
        item { Text(stringResource(R.string.group_disclaimer), style = Zapara.typography.caption, color = c.text2) }
        item { Text(stringResource(R.string.channel_trusted_hint), style = Zapara.typography.caption, color = c.text2) }
        if (desk == null) item { Text(stringResource(if (state.failed) R.string.channel_trusted_load_failed
            else R.string.channel_trusted_loading), color = c.text2) }
        else if (enabledRoles.isEmpty()) item {
            ZCard(Modifier.fillMaxWidth(), tag = "Group.TrustedRoleEditor") {
                Text(stringResource(R.string.channel_trusted_empty), style = Zapara.typography.body, color = c.text1)
                ZTextField(roleName, { roleName = it.take(32) },
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
private fun ChannelEditor(initial: GroupTopic?, state: GroupUiState,
    onSave: (String, String, String, String, String, Boolean, String, String, String?, Int, String?, Long?) -> Unit, onCancel: () -> Unit) {
    val uiText = rememberUiText()
    val busy = state.channelBusy
    var revisions by rememberSaveable(initial?.topicId, stateSaver = RevisionGuard.Saver) { mutableStateOf(RevisionGuard(initial?.revision ?: 0)) }
    val latest = state.channels.firstOrNull { it.topicId == initial?.topicId }
    LaunchedEffect(latest?.revision) { latest?.let { revisions = revisions.observed(it.revision) } }
    var template by remember(initial?.topicId) { mutableStateOf(initial?.template ?: "chat") }
    var category by remember(initial?.topicId) { mutableStateOf(initial?.categoryId) }
    var position by remember(initial?.topicId) { mutableStateOf((initial?.position ?: 0).toString()) }
    var subject by remember(initial?.topicId) { mutableStateOf(initial?.subject) }
    var title by remember(initial?.topicId) { mutableStateOf(initial?.title.orEmpty()) }
    var icon by remember(initial?.topicId) { mutableStateOf(initial?.icon ?: "💬") }
    var kind by remember(initial?.topicId) { mutableStateOf(initial?.kind ?: "chat") }
    var description by remember(initial?.topicId) { mutableStateOf(initial?.description.orEmpty()) }
    var accent by remember(initial?.topicId) { mutableStateOf(initial?.accent ?: "default") }
    var pinned by remember(initial?.topicId) { mutableStateOf(initial?.pinned ?: false) }
    var writePolicy by remember(initial?.topicId) { mutableStateOf(initial?.writePolicy ?: "all") }
    ZCard(Modifier.fillMaxWidth(), tag = "Group.ChannelEditor") {
        if (initial != null && revisions.conflict && latest != null) {
            Text(uiText(R.string.review_channel_conflict), style = Zapara.typography.body, color = Zapara.colors.warn)
            Text(uiText(R.string.review_current_channel, latest.title, latest.description, latest.writePolicy, latest.position, latest.revision), style = Zapara.typography.caption)
            ZButton(uiText(R.string.space_day_reload_changes), {
                title = latest.title; icon = latest.icon; description = latest.description; accent = latest.accent; pinned = latest.pinned
                writePolicy = latest.writePolicy; template = latest.template; category = latest.categoryId; position = latest.position.toString(); subject = latest.subject
                revisions = revisions.reload()
            }, enabled = !busy, ghost = true)
            ZButton(uiText(R.string.review_keep_draft), { revisions = revisions.reload() },enabled=!busy,ghost=true)
        }
        Text(stringResource(if (initial == null) R.string.channel_new else R.string.channel_change), style = Zapara.typography.bodyStrong)
        ZTextField(title, { title = it.take(40) }, label = { Text(stringResource(R.string.channel_name)) }, singleLine = true,
            modifier = Modifier.fillMaxWidth().testTag("Group.ChannelTitle"))
        ZTextField(icon, { icon = it.take(8) }, label = { Text(stringResource(R.string.channel_icon)) }, singleLine = true,
            modifier = Modifier.fillMaxWidth().testTag("Group.ChannelIcon"))
        ZTextField(description, { description = it.take(240) }, label = { Text(stringResource(R.string.channel_description)) },
            modifier = Modifier.fillMaxWidth().testTag("Group.ChannelDescription"))
        if (initial == null) FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            GroupTemplates.titles.filterKeys { value -> if (state.space == null) value in setOf("chat", "polls") else value in state.space.capabilities.templates }.forEach { (value, label) ->
                ZChip(label, selected = template == value, onClick = { template = value; kind = GroupTemplates.kind(value) }, tag = "Group.Template.$value")
            }
        } else Text(GroupTemplates.titles[template] ?: uiText(R.string.space_day_45), style = Zapara.typography.caption)
        if (template == "subject") {
            Text(uiText(R.string.space_day_46), style = Zapara.typography.caption)
            FlowRow { state.subjects.forEach { value -> ZChip(value, selected = subject == value, onClick = { subject = value }) } }
        }
        FlowRow {
            ZChip(uiText(R.string.space_day_47), selected = category == null, onClick = { category = null })
            state.space?.categories.orEmpty().sortedBy { it.position }.forEach { value -> ZChip(value.title, selected = category == value.categoryId, onClick = { category = value.categoryId }) }
        }
        ZTextField(position, { position = it }, label = { Text(uiText(R.string.space_day_48)) }, modifier = Modifier.fillMaxWidth())
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
                onSave(title.trim(), icon.trim(), kind, description.trim(), accent, pinned, writePolicy, template, category, position.toInt(), subject, revisions.base) },
                enabled = !busy && title.trim().length in 2..40 && position.toIntOrNull() != null && (template != "subject" || subject != null), tag = "Group.ChannelSave")
            ZButton(stringResource(R.string.channel_cancel), onCancel, ghost = true)
        }
    }
}

@Composable
private fun BallotChannel(state: GroupUiState, onEvent: (GroupEvent) -> Unit, modifier: Modifier) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    val largeText = LocalDensity.current.fontScale >= 1.5f
    val board = state.board
    var query by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf("") }
    var status by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(BallotStatus.All.name) }
    var sort by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(BallotSort.Original.name) }
    var filtersOpen by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(false) }
    var pendingDiscardKey by remember(state.ballotDraftKey) { mutableStateOf<BallotDraftKey?>(null) }
    val draftKey = state.ballotDraftKey
    val draft = state.ballotDraft
    val statusFilter = BallotStatus.entries.firstOrNull { it.name == status } ?: BallotStatus.All
    val sortOrder = BallotSort.entries.firstOrNull { it.name == sort } ?: BallotSort.Original
    val visible = browseBallots(board?.ballots.orEmpty(), query, statusFilter, sortOrder)
    val filtered = query.isNotBlank() || statusFilter != BallotStatus.All || sortOrder != BallotSort.Original
    val hasDraft = draft.hasContent
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
            ZTextField(query, { query = it }, label = { Text(stringResource(R.string.group_ballot_search)) },
                singleLine = true, modifier = Modifier.fillMaxWidth().testTag("Group.BallotSearch"))
            Text(stringResource(R.string.group_ballot_scope), style = Zapara.typography.caption, color = c.text2)
            if (largeText) {
                Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    Text(stringResource(R.string.group_ballot_results, visible.size, board.ballots.size),
                        style = Zapara.typography.caption, color = c.text2, modifier = Modifier.fillMaxWidth())
                    ZChip(stringResource(R.string.group_ballot_filters), selected = filtered,
                        onClick = { filtersOpen = true }, tag = "Group.BallotFilters")
                }
            } else {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    Text(stringResource(R.string.group_ballot_results, visible.size, board.ballots.size),
                        style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
                    ZChip(stringResource(R.string.group_ballot_filters), selected = filtered,
                        onClick = { filtersOpen = true }, tag = "Group.BallotFilters")
                }
            }
            if (filtered) ZButton(stringResource(R.string.group_ballot_reset), {
                query = ""; status = BallotStatus.All.name; sort = BallotSort.Original.name
            }, ghost = true, tag = "Group.BallotReset")
            if (state.canPost || hasDraft) ZButton(stringResource(if (hasDraft) R.string.group_ballot_continue_draft else R.string.channel_ballot_new), {
                filtersOpen = false
                draftKey?.let { onEvent(GroupEvent.BallotDraftEdit(it, composing = true)) }
            }, enabled = !state.channelBusy && draftKey != null, tag = "Group.BallotCreate")
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
                    val permissions = GroupActions.topic(state,ballot.topicId)?.permissions.orEmpty()
                    BallotCard(ballot, state.preview == null && board.canClose && (permissions.isEmpty() || "close" in permissions),
                        state.channelBusy || ballot.ballotId in state.ballotPendingIds, onEvent,
                        state.preview == null && (permissions.isEmpty() || "vote" in permissions))
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
    if (draft.composing && board != null && draftKey != null) ZBottomSheet(
        onDismiss = { if (!state.channelBusy) onEvent(GroupEvent.BallotDraftEdit(draftKey, composing = false)) }, tag = "Group.BallotCreateSheet", scrollable = true) {
        BallotEditor(state.channelBusy, board.canOpen, state.canPost, state.ballotCreateFailed,
            question = draft.question, onQuestion = { onEvent(GroupEvent.BallotDraftEdit(draftKey, question = it)) },
            options = draft.options, onOptions = { onEvent(GroupEvent.BallotDraftEdit(draftKey, options = it)) },
            days = draft.days, onDays = { onEvent(GroupEvent.BallotDraftEdit(draftKey, days = it)) },
            onSave = { question, options, days, headman ->
                onEvent(GroupEvent.CreateBallot(question, options, days, headman, draft.revision, draftKey))
            }, onCancel = {
                if (!state.channelBusy) {
                    if (draft.hasContent) pendingDiscardKey = draftKey
                    else onEvent(GroupEvent.BallotDraftEdit(draftKey, clear = true))
                }
            })
    }
    pendingDiscardKey?.let { target ->
        AlertDialog(onDismissRequest = { pendingDiscardKey = null },
            title = { Text(stringResource(R.string.ux60_chat_ballot_discard_title)) },
            text = { Text(stringResource(R.string.ux60_chat_ballot_discard_body)) },
            confirmButton = {
                Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZButton(stringResource(R.string.ux60_chat_ballot_discard_confirm), {
                        if (!state.channelBusy && target == state.ballotDraftKey)
                            onEvent(GroupEvent.BallotDraftEdit(target, clear = true))
                        pendingDiscardKey = null
                    }, modifier = Modifier.fillMaxWidth(), enabled = !state.channelBusy,
                        tag = "Group.BallotDiscardConfirm")
                    ZButton(stringResource(R.string.channel_cancel), { pendingDiscardKey = null },
                        modifier = Modifier.fillMaxWidth(), ghost = true, tag = "Group.BallotDiscardCancel")
                }
            })
    }
}

@Composable
private fun rememberGroupAttachmentSave(
    state: GroupUiState, onEvent: (GroupEvent) -> Unit
): (GroupMessageUi) -> Unit {
    var pendingTarget by remember(state.ownerId, state.communityId, state.activeConversationId, state.activeTopicId) {
        mutableStateOf<GroupMediaSaveTarget?>(null)
    }
    val currentOwner by rememberUpdatedState(state.ownerId)
    val currentCommunity by rememberUpdatedState(state.communityId)
    val currentConversation by rememberUpdatedState(state.activeConversationId)
    val currentTopic by rememberUpdatedState(state.activeTopicId)
    val send by rememberUpdatedState(onEvent)
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/octet-stream")) { uri ->
        val target = pendingTarget
        pendingTarget = null
        if (uri != null && target != null && target.ownerId == currentOwner &&
            target.communityId == currentCommunity &&
            target.conversationId == currentConversation && target.topicId == currentTopic)
            send(GroupEvent.SaveMedia(target, uri.toString()))
    }
    return { message ->
        val owner = state.ownerId
        val community = state.communityId
        val conversation = state.activeConversationId
        if (owner != null && community != null && conversation != null && !message.deleted && message.kind in setOf("image", "video", "file", "voice", "circle")) {
            pendingTarget = GroupMediaSaveTarget(owner, community, conversation, state.activeTopicId, message.id)
            launcher.launch(groupAttachmentSuggestedName(message.body))
        }
    }
}

@Composable
private fun BallotCard(ballot: Ballot, canClose: Boolean, busy: Boolean, onEvent: (GroupEvent) -> Unit, canVote: Boolean = true) {
    val uiText = rememberUiText()
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
                enabled = !busy && canVote, tag = "Group.BallotSupport.${ballot.ballotId}")
        }
        ballot.options.forEach { option ->
            val percent = ballotPercent(option.votes, total)
            val resultText = stringResource(R.string.group_ballot_option_result, option.votes, percent)
            val optionLabel = if (option.chosen) stringResource(R.string.group_ballot_your_choice, option.label) else option.label
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                if (ballot.status == "open") ZButton(
                    optionLabel,
                    { onEvent(GroupEvent.VoteBallot(ballot.ballotId, option.optionId)) },
                    modifier = Modifier.fillMaxWidth(), enabled = !busy && canVote,
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
    val uiText = rememberUiText()
    var deletingOption by remember { mutableStateOf<Pair<Int, String>?>(null) }
    ZCard(Modifier.fillMaxWidth(), tag = "Group.BallotEditor") {
        Text(stringResource(R.string.channel_ballot_one_question), style = Zapara.typography.bodyStrong)
        if (failed) Text(stringResource(R.string.group_ballot_submit_failed), style = Zapara.typography.caption,
            color = Zapara.colors.bad, modifier = Modifier.testTag("Group.BallotSubmitError"))
        if (busy) Text(stringResource(R.string.group_ballot_submitting), style = Zapara.typography.caption,
            color = Zapara.colors.text2)
        if (!canSubmit) Text(stringResource(R.string.group_ballot_write_revoked), style = Zapara.typography.caption,
            color = Zapara.colors.warn)
        ZTextField(question, { onQuestion(it.take(400)) }, label = { Text(stringResource(R.string.channel_ballot_question)) },
            enabled = !busy,
            supportingText = { Text(stringResource(R.string.ux100_chat_question_length, question.length)) },
            modifier = Modifier.fillMaxWidth().testTag("Group.BallotQuestion"))
        options.forEachIndexed { index, option ->
            ZTextField(option, { next -> onOptions(options.mapIndexed { i, value -> if (i == index) next.take(80) else value }) },
                label = { Text(stringResource(R.string.channel_ballot_option, index + 1)) }, singleLine = true,
                enabled = !busy,
                modifier = Modifier.fillMaxWidth().testTag("Group.BallotOption.$index"))
            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                ZButton(stringResource(R.string.ux100_chat_option_up), { onOptions(moveBallotOption(options, index, -1)) }, enabled = !busy && index > 0, ghost = true)
                ZButton(stringResource(R.string.ux100_chat_option_down), { onOptions(moveBallotOption(options, index, 1)) }, enabled = !busy && index < options.lastIndex, ghost = true)
                if (options.size > 2) ZButton(stringResource(R.string.ux100_chat_option_remove), {
                    if (option.isBlank()) onOptions(options.filterIndexed { i, _ -> i != index }) else deletingOption = index to option
                }, enabled = !busy, ghost = true)
            }
        }
        if (options.size < 6) ZButton(stringResource(R.string.channel_ballot_add_option), { onOptions(options + "") }, enabled = !busy, ghost = true)
        Text(stringResource(R.string.channel_ballot_duration), style = Zapara.typography.caption)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf(1, 3, 7, 14).forEach { value ->
                ZChip(stringResource(R.string.channel_ballot_days, value), selected = days == value, onClick = { if (!busy) onDays(value) }, tag = "Group.BallotDays.$value")
            }
        }
        Text(stringResource(R.string.ux100_chat_ballot_deadline,
            java.time.LocalDateTime.now().plusDays(days.toLong()).format(DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm"))), style = Zapara.typography.caption)
        val problem = ballotEditorProblem(question, options)
        if (problem != null) Text(stringResource(when (problem) {
            BallotEditorProblem.Question -> R.string.ux100_chat_ballot_question
            BallotEditorProblem.EmptyOption -> R.string.ux100_chat_ballot_empty
            BallotEditorProblem.DuplicateOption -> R.string.ux100_chat_ballot_duplicate
        }), style = Zapara.typography.caption, color = Zapara.colors.warn)
        val valid = problem == null
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.channel_ballot_propose), { onSave(question.trim(), options.map { it.trim() }, days, false) },
                enabled = valid && canSubmit && !busy, tag = "Group.BallotPropose")
            if (canOpen) ZButton(stringResource(R.string.channel_ballot_announce), { onSave(question.trim(), options.map { it.trim() }, days, true) },
                enabled = valid && canSubmit && !busy, ghost = true, tag = "Group.BallotOpen")
            ZButton(stringResource(R.string.channel_cancel), onCancel, enabled = !busy, ghost = true)
        }
    }
    deletingOption?.let { (index, value) ->
        AlertDialog(onDismissRequest = { deletingOption = null },
            title = { Text(stringResource(R.string.ux100_chat_option_remove_title)) },
            text = { Text(value) },
            confirmButton = { Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.ux100_chat_option_remove), {
                if (options.size > 2 && options.getOrNull(index) == value) onOptions(options.filterIndexed { i, _ -> i != index })
                deletingOption = null
                }, modifier = Modifier.fillMaxWidth(), enabled = !busy)
                ZButton(stringResource(R.string.channel_cancel), { deletingOption = null }, modifier = Modifier.fillMaxWidth(), ghost = true)
            } })
    }
}

@Composable
private fun People(state: GroupUiState, onEvent: (GroupEvent) -> Unit,
                   query: String, onQuery: (String) -> Unit, modifier: Modifier) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    var leadersOnly by rememberSaveable(state.communityId) { mutableStateOf(false) }
    val visible = browsePeople(state.people, query).filter { !leadersOnly || it.role in setOf("headman", "curator") }
    val visibleDirects = state.directs.filter { query.isBlank() || (it.title + " " + it.preview).contains(query.trim(), ignoreCase = true) }
    LazyColumn(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item {
            ZTextField(query, onQuery, label = { Text(stringResource(R.string.ux100_chat_people_chat_search)) },
                placeholder = { Text(stringResource(R.string.ux100_chat_people_chat_search_hint)) }, singleLine = true,
                modifier = Modifier.fillMaxWidth().testTag("Group.PeopleSearch"))
        }
        item {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                ZChip(stringResource(R.string.ux100_chat_people_all), selected = !leadersOnly, onClick = { leadersOnly = false })
                ZChip(stringResource(R.string.ux100_chat_people_leaders), selected = leadersOnly, onClick = { leadersOnly = true })
            }
            Text(stringResource(R.string.ux100_chat_people_chat_count, visible.size, state.people.size,
                if (leadersOnly) 0 else visibleDirects.size, state.directs.size), style = Zapara.typography.caption)
        }
        item {
            ZCard(onClick = { onEvent(GroupEvent.GroupChat) }, tag = "Group.Room", modifier = Modifier.fillMaxWidth()) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ChatAvatar(state.title, state.communityId?.let { AvatarTarget(AvatarKind.Group, it) }, 40.dp)
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
        if ((query.isNotBlank() || leadersOnly) && visible.isEmpty()) item {
            ZCard(Modifier.fillMaxWidth(), tag = "Empty.GroupPeopleSearch") {
                Text(stringResource(R.string.channel_people_empty), style = Zapara.typography.body, color = c.text2)
                ZButton(stringResource(R.string.group_search_clear), { onQuery(""); leadersOnly = false }, ghost = true)
            }
        }
        items(visible, key = { it.id }) { person ->
            ZCard(
                onClick = if (person.self) null else { { onEvent(GroupEvent.Direct(person.id)) } },
                tag = "Group.Person.${person.id}",
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ChatAvatar(person.name, AvatarTarget(AvatarKind.User, person.id), 40.dp)
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        Text(person.name, style = Zapara.typography.bodyStrong, color = c.text1, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text("@${person.handle}", style = Zapara.typography.caption, color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        if (person.self) Text(stringResource(R.string.ux100_chat_people_self), style = Zapara.typography.caption, color = c.text2)
                    }
                    RoleChip(person.role)
                }
            }
        }
        if (visibleDirects.isNotEmpty() && !leadersOnly) {
            item { Text(stringResource(R.string.group_directs), style = Zapara.typography.bodyStrong, color = c.text2) }
            items(visibleDirects, key = { it.id }) { chat ->
                ZCard(onClick = { onEvent(GroupEvent.OpenChat(chat.id, chat.title)) }, tag = "Group.Direct.${chat.id}", modifier = Modifier.fillMaxWidth()) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        ChatAvatar(chat.title, chat.peerUserId?.let { AvatarTarget(AvatarKind.User, it) }, 40.dp)
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
    val uiText = rememberUiText()
    if (count <= 0) return
    val description = stringResource(R.string.group_unread_count, count)
    ZChip(if (count > 99) "99+" else count.toString(), selected = true,
        modifier = Modifier.semantics { contentDescription = description })
}

@Composable
private fun Messages(state: GroupUiState, onEvent: (GroupEvent) -> Unit, modifier: Modifier,
    searchOpen: Boolean, onSearchDismiss: () -> Unit, showSearchAction: Boolean, onSearchOpen: () -> Unit) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    val calendarContext = LocalContext.current
    val calendarTheme = if (c.isDark) R.style.Zapara_DatePicker_Dark else R.style.Zapara_DatePicker_Light
    val listState = rememberLazyListState()
    val scope = rememberCoroutineScope()
    var query by rememberSaveable(state.activeConversationId, state.activeTopicId) { mutableStateOf("") }
    var author by rememberSaveable(state.activeConversationId, state.activeTopicId) { mutableStateOf(MessageAuthor.All.name) }
    var senderId by rememberSaveable(state.activeConversationId, state.activeTopicId) { mutableStateOf("") }
    var kind by rememberSaveable(state.activeConversationId, state.activeTopicId) { mutableStateOf(MessageKind.All.name) }
    var selectedDate by rememberSaveable(state.activeConversationId, state.activeTopicId) { mutableStateOf("") }
    var pendingQuote by remember(state.activeConversationId, state.activeTopicId) { mutableStateOf<String?>(null) }
    var highlightedQuote by remember(state.activeConversationId, state.activeTopicId) { mutableStateOf<String?>(null) }
    var quoteNotice by remember(state.activeConversationId, state.activeTopicId) { mutableStateOf<QuoteTarget?>(null) }
    val saveAttachment = rememberGroupAttachmentSave(state, onEvent)
    val authorFilter = MessageAuthor.entries.firstOrNull { it.name == author } ?: MessageAuthor.All
    val kindFilter = MessageKind.entries.firstOrNull { it.name == kind } ?: MessageKind.All
    val selectedSenderId = senderId.takeIf(String::isNotBlank)
    val visible = browseMessages(state.messages, query, authorFilter, kindFilter, selectedSenderId)
        .filter { selectedDate.isBlank() || it.createdAt?.atZone(ZoneId.systemDefault())
            ?.toLocalDate()?.toString() == selectedDate }
    val filtered = query.isNotBlank() || authorFilter != MessageAuthor.All ||
        kindFilter != MessageKind.All || selectedSenderId != null || selectedDate.isNotBlank()
    KeepLatestVisible(listState, "${state.activeConversationId}:${state.activeTopicId}",
        enabled = !filtered && pendingQuote == null && highlightedQuote == null)
    var firstScrollDone by remember(state.activeConversationId, state.activeTopicId) { mutableStateOf(false) }
    val filterKey = listOf(query.trim(), authorFilter.name, selectedSenderId.orEmpty(),
        kindFilter.name, selectedDate)
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
    LaunchedEffect(pendingQuote, visible.map { it.id }, state.hasMore) {
        val target = pendingQuote ?: return@LaunchedEffect
        val index = visible.indexOfFirst { it.id == target }
        if (index >= 0) {
            listState.animateScrollToItem(index + if (state.hasMore) 1 else 0)
            highlightedQuote = target
            pendingQuote = null
            quoteNotice = null
            delay(3000)
            if (highlightedQuote == target) highlightedQuote = null
        }
    }
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        if (showSearchAction) ZButton(stringResource(R.string.group_message_search), onSearchOpen,
            ghost = true, tag = "Group.MessageFilters")
        if (filtered) FlowRow(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs),
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Text(stringResource(R.string.ux30_chat_result_count, visible.size, state.messages.size),
                style = Zapara.typography.caption, color = c.text2, modifier = Modifier.testTag("Group.SearchActive"))
            if (selectedDate.isNotBlank()) Text(selectedDate, style = Zapara.typography.caption,
                color = c.text2)
            ZButton(stringResource(R.string.group_message_reset), {
                query = ""; author = MessageAuthor.All.name; senderId = "";
                kind = MessageKind.All.name; selectedDate = ""
            }, ghost = true, tag = "Group.InlineMessageReset")
        }
        if (quoteNotice != null) Text(stringResource(when {
            quoteNotice == QuoteTarget.Deleted -> R.string.next_quote_deleted
            state.hasMore -> R.string.next_quote_earlier
            else -> R.string.next_quote_missing
        }),
            style = Zapara.typography.caption, color = c.warn)
        if (quoteNotice == QuoteTarget.Earlier && state.hasMore)
            ZButton(stringResource(R.string.ux300_android_find_quoted_message),
                { onEvent(GroupEvent.Older) }, enabled = !state.olderLoading,
                ghost = true, tag = "Group.LoadQuotedMessage")
        if (state.chatLoading && state.messages.isEmpty()) {
            Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                Text(stringResource(R.string.group_loading), style = Zapara.typography.caption, color = c.text2)
            }
        } else {
            val showJump by remember(listState, visible.size) { derivedStateOf {
                val total = listState.layoutInfo.totalItemsCount
                val lastVisible = listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
                visible.size > 4 && total > 0 && lastVisible < total - 2
            } }
            Box(Modifier.weight(1f).fillMaxWidth()) {
                LazyColumn(Modifier.fillMaxSize().testTag("Group.Messages"), state = listState) {
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
                        Column(Modifier.fillMaxWidth().testTag("Group.Message.${message.id}").padding(top = if (grouped) Zapara.space.xs else Zapara.space.s),
                            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                            if (message.day.isNotBlank() && (index == 0 || visible[index - 1].day != message.day))
                                Text(message.day, modifier = Modifier.align(Alignment.CenterHorizontally).testTag("Group.Day.${message.id}"),
                                    style = Zapara.typography.caption, color = c.text2)
                            if (highlightedQuote == message.id) Text(stringResource(R.string.next_quote_found),
                                style = Zapara.typography.caption, color = c.accent)
                            MessageBubble(message, state.messages.firstOrNull { it.id == message.replyTo }?.body,
                                state.mediaLoadingId == message.id || message.id in state.mediaLoadingIds,
                                state.mediaFiles[message.id], message.id in state.mediaFailedIds, state.canPost,
                                mediaSaving = message.id in state.mediaSavingIds,
                                mediaSaved = message.id in state.mediaSavedIds,
                                mediaSaveFailed = message.id in state.mediaSaveFailedIds,
                                showAuthor = !grouped, onEvent = onEvent, canModerate = GroupActions.canModerate(state), mutationEnabled = state.preview == null && state.activeArchivedTopic == null,
                                onSaveMedia = saveAttachment,
                                onQuoteClick = { id ->
                                    when (val found = quoteTarget(state.messages, id)) {
                                        QuoteTarget.Loaded -> {
                                            quoteNotice = null
                                            query = ""; author = MessageAuthor.All.name; senderId = ""; kind = MessageKind.All.name
                                            selectedDate = ""
                                            pendingQuote = id
                                        }
                                        QuoteTarget.Earlier -> {
                                            quoteNotice = found
                                            query = ""; author = MessageAuthor.All.name
                                            senderId = ""; kind = MessageKind.All.name; selectedDate = ""
                                            pendingQuote = id
                                            if (state.hasMore) onEvent(GroupEvent.Older)
                                        }
                                        else -> quoteNotice = found
                                    }
                                })
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
    if (searchOpen) ZBottomSheet(onDismiss = onSearchDismiss, tag = "Group.MessageFilterSheet",
        scrollable = true, footer = {
            ZButton(stringResource(R.string.group_message_filters_done), onSearchDismiss,
                tag = "Group.MessageFiltersDone", modifier = Modifier.fillMaxWidth())
        }) {
        ZTextField(query, { query = it }, label = { Text(stringResource(R.string.ux30_chat_group_search)) },
            singleLine = true, modifier = Modifier.fillMaxWidth().testTag("Group.MessageSearch"))
        Text(stringResource(R.string.group_message_scope), style = Zapara.typography.caption, color = c.text2)
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Text(stringResource(R.string.group_message_results, visible.size, state.messages.size),
                style = Zapara.typography.caption, color = c.text2, modifier = Modifier.weight(1f))
        }
        if (filtered) ZButton(stringResource(R.string.group_message_reset), {
            query = ""; author = MessageAuthor.All.name; senderId = "";
            kind = MessageKind.All.name; selectedDate = ""
        }, ghost = true, tag = "Group.MessageReset")
        ZButton(stringResource(R.string.ux300_android_chat_pick_date), {
            val date = runCatching { java.time.LocalDate.parse(selectedDate) }
                .getOrDefault(java.time.LocalDate.now())
            android.app.DatePickerDialog(calendarContext, calendarTheme, { _, year, month, day ->
                selectedDate = java.time.LocalDate.of(year, month + 1, day).toString()
            }, date.year, date.monthValue - 1, date.dayOfMonth).show()
        }, ghost = true, tag = "Group.MessageDate")
        if (selectedDate.isNotBlank()) ZButton(stringResource(R.string.ux300_android_chat_any_date),
            { selectedDate = "" }, ghost = true, tag = "Group.MessageAnyDate")
        Text(stringResource(R.string.group_message_filters), style = Zapara.typography.section, color = c.text1)
        val authors = (state.people.map { it.id to it.name } +
            state.messages.filter { it.senderId.isNotBlank() }.map { it.senderId to it.author })
            .filter { it.first.isNotBlank() }.distinctBy { it.first }.sortedBy { it.second }
        val selectedAuthor = authors.firstOrNull { it.first == selectedSenderId }
        var authorMenu by remember(state.activeConversationId, state.activeTopicId) { mutableStateOf(false) }
        Box {
            ZButton(selectedAuthor?.let { "${it.second} · ${it.first.takeLast(6)}" }
                ?: stringResource(R.string.ux60_chat_author_all), { authorMenu = true },
                ghost = selectedAuthor == null, tag = "Group.MessageSender")
            DropdownMenu(expanded = authorMenu, onDismissRequest = { authorMenu = false }) {
                DropdownMenuItem(text = { Text(stringResource(R.string.ux60_chat_author_all)) }, onClick = {
                    senderId = ""; author = MessageAuthor.All.name; authorMenu = false
                }, modifier = Modifier.testTag("Group.MessageSender.All"))
                authors.forEach { person ->
                    DropdownMenuItem(text = { Text("${person.second} · ${person.first.takeLast(6)}") }, onClick = {
                        senderId = person.first; author = MessageAuthor.All.name; authorMenu = false
                    }, modifier = Modifier.testTag("Group.MessageSender.${person.first}"))
                }
            }
        }
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf(MessageAuthor.All to R.string.group_author_all, MessageAuthor.Mine to R.string.group_author_mine,
                MessageAuthor.Others to R.string.group_author_others).forEach { (value, label) ->
                ZChip(stringResource(label), selected = authorFilter == value,
                    onClick = { author = value.name; senderId = "" }, tag = "Group.MessageAuthor.${value.name}",
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
    }
}

@Composable
private fun messageLabel(message: GroupMessageUi): String = when {
    message.deleted -> stringResource(R.string.face_message_deleted)
    message.kind == "image" -> stringResource(R.string.face_photo)
    message.kind == "video" || message.kind == "circle" -> stringResource(R.string.face_video)
    message.kind == "voice" -> stringResource(R.string.face_voice)
    message.kind == "file" -> message.body.ifBlank { stringResource(R.string.face_document) }
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
    mediaSaving: Boolean, mediaSaved: Boolean, mediaSaveFailed: Boolean,
    onEvent: (GroupEvent) -> Unit, canModerate: Boolean = false, mutationEnabled: Boolean = true,
    onSaveMedia: (GroupMessageUi) -> Unit = {},
    onQuoteClick: (String) -> Unit = {}) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    val mine = message.mine
    val context = LocalContext.current
    var menu by remember(message.id) { mutableStateOf(false) }
    var deleting by remember(message.id) { mutableStateOf(false) }
    var reactionPicker by remember(message.id) { mutableStateOf(false) }
    var copyFeedback by remember(message.id) { mutableStateOf<String?>(null) }
    val actions = if (!mutationEnabled) emptyList() else HoldDecision.actions(message.kind, mine, message.deleted, true, canModerate)
        .filter { canPost || it !in setOf("reply", "edit") }
    val copyText = copyableMessageText(message)
    val canCopy = copyText != null
    val canSave = !message.deleted && message.kind in setOf("image", "video", "file", "voice", "circle")
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
        val uiText = rememberUiText()
        Box {
            IconButton(onClick = { menu = true }, modifier = Modifier.size(Zapara.space.minTouch)
                .testTag("Group.MessageActions.${message.id}")) {
                Icon(painterResource(R.drawable.ic_ellipsis),
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
                if (canSave) DropdownMenuItem(text = { Text(stringResource(R.string.ux60_chat_group_save)) }, onClick = {
                    menu = false
                    onSaveMedia(message)
                }, modifier = Modifier.testTag("Group.SaveMedia.${message.id}"))
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
        if (!mine) {
            if (showAuthor) ChatAvatar(message.author, message.senderId.takeIf { it.isNotBlank() }?.let { AvatarTarget(AvatarKind.User, it) }, 28.dp)
            else Spacer(Modifier.width(28.dp))
            Spacer(Modifier.width(8.dp))
        }
        if (mine && (actions.isNotEmpty() || canCopy || canSave)) MessageActions()
        Surface(
            shape = RoundedCornerShape(Zapara.radii.card),
            color = if (mine) c.chip else c.card,
            contentColor = c.text1,
            border = BorderStroke(Zapara.space.hairline, if (mine) c.lineStrong else c.line),
            modifier = Modifier.weight(1f, fill = false).widthIn(max = 280.dp).combinedClickable(
                onClick = {
                    if (!mediaLoading && !message.deleted && message.kind in setOf("image", "video", "file")) {
                        onEvent(GroupEvent.OpenMedia(message.id))
                    }
                },
                onLongClick = { if (actions.isNotEmpty() || canCopy || canSave) menu = true }
            )
        ) {
            Column(
                Modifier.padding(horizontal = 12.dp, vertical = Zapara.space.s),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)
            ) {
                if (message.replyTo != null) ZButton("↳ ${replyPreview?.take(80) ?: stringResource(R.string.face_message)}",
                    { onQuoteClick(message.replyTo) }, ghost = true,
                    tag = "Group.Quote.${message.id}")
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
        if (!mine && (actions.isNotEmpty() || canCopy || canSave)) MessageActions()
        }
        when {
            mediaSaving -> Text(stringResource(R.string.ux60_chat_group_saving),
                style = Zapara.typography.caption, color = c.text2, modifier = Modifier.testTag("Group.MediaSaving.${message.id}"))
            mediaSaved -> Text(stringResource(R.string.ux60_chat_group_saved),
                style = Zapara.typography.caption, color = c.ok, modifier = Modifier.testTag("Group.MediaSaved.${message.id}"))
            mediaSaveFailed -> Text(stringResource(R.string.ux60_chat_group_save_failed),
                style = Zapara.typography.caption, color = c.bad, modifier = Modifier.testTag("Group.MediaSaveFailed.${message.id}"))
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
        text = { Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Text("${message.author} · ${message.time}", style = Zapara.typography.bodyStrong)
            Text(chatMessagePreview(message.kind, message.body, fileName = message.body.takeIf { message.kind != "text" }, deleted = message.deleted),
                maxLines = 4, overflow = TextOverflow.Ellipsis)
            Text(stringResource(R.string.group_message_delete_warning))
        } },
        confirmButton = { ZButton(stringResource(R.string.group_message_delete), {
            onEvent(GroupEvent.Hold(message.id, "delete")); deleting = false
        }, tag = "Group.DeleteConfirm.${message.id}") },
        dismissButton = { ZButton(stringResource(R.string.channel_cancel), { deleting = false }, ghost = true) })
}

@Composable
private fun Composer(state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val uiText = rememberUiText()
    val c = Zapara.colors
    val composerFocus = remember(state.activeConversationId, state.activeTopicId) { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    LaunchedEffect(state.activeConversationId, state.activeTopicId, state.editing, state.replyTo) {
        if (state.editing != null || state.replyTo != null) { if (runCatching { composerFocus.requestFocus() }.isSuccess) keyboard?.show() }
    }
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val permissions = state.channels.firstOrNull { it.topicId == state.activeTopicId }?.permissions.orEmpty()
    val canMedia = state.direct || permissions.isEmpty() || "media" in permissions
    val messagePreview = groupMessagePreview(state.draft, state.composeContext, state.editing != null)
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
    ChatMediaCaptureHost(enabled = !state.sending && state.editing == null && canMedia,
        onRecorded = { kind, file, duration -> onEvent(GroupEvent.Recorded(kind, file, duration, state.activeConversationId, state.activeTopicId)) },
        onError = { onEvent(GroupEvent.MediaError) }, modifier = Modifier.fillMaxWidth()) { startVoice, startCircle ->
        Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            state.composeContext?.let { contextText -> Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(contextText, style = Zapara.typography.caption, modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                ZButton(uiText(R.string.space_day_49), { onEvent(GroupEvent.Context(null)) }, ghost = true, quiet = true)
            } }
            if (state.editing != null || state.replyTo != null) {
                val target = state.messages.firstOrNull { it.id == (state.editing ?: state.replyTo) }
                Surface(shape = RoundedCornerShape(Zapara.radii.control), color = c.chip,
                    border = BorderStroke(Zapara.space.hairline, c.lineStrong),
                    modifier = Modifier.fillMaxWidth().testTag("Group.ComposeContext")) {
                    Row(Modifier.padding(Zapara.space.s), verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        Column(Modifier.weight(1f)) {
                            Text(if (state.editing != null) stringResource(R.string.group_edit_context)
                                else stringResource(R.string.ux30_chat_reply_author, target?.author ?: stringResource(R.string.group_message)),
                                style = Zapara.typography.caption, color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(target?.let { message -> chatMessagePreview(message.kind, message.body,
                                fileName = message.body.takeIf { message.kind != "text" }, deleted = message.deleted) }
                                ?: stringResource(R.string.group_message), color = c.text1,
                                maxLines = 2, overflow = TextOverflow.Ellipsis)
                        }
                        ZButton(stringResource(R.string.channel_cancel), { onEvent(GroupEvent.CancelContext) }, ghost = true,
                            tag = "Group.CancelContext")
                    }
                }
            }
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.Bottom,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (state.editing == null) Box {
                    ZIconButton(R.drawable.ic_paperclip, stringResource(R.string.group_attach),
                        { attachOpen = true }, "Group.Attach", enabled = !state.sending)
                    DropdownMenu(expanded = attachOpen, onDismissRequest = { attachOpen = false }) {
                        DropdownMenuItem(enabled = canMedia, text = { Text(uiText(R.string.space_day_50)) }, onClick = { attachOpen = false; startVoice() })
                        DropdownMenuItem(enabled = canMedia, text = { Text(uiText(R.string.space_day_51)) }, onClick = { attachOpen = false; startCircle() }, modifier = Modifier.testTag("Group.Circle"))
                        DropdownMenuItem(text = { Text(uiText(R.string.space_day_52)) }, onClick = { attachOpen = false; onEvent(GroupEvent.GlobalBallots(uiText(R.string.space_day_53))) })
                        state.contextLesson?.let { lesson -> DropdownMenuItem(text = { Text(uiText(R.string.space_day_54, (lesson.subject).toString())) }, onClick = { attachOpen = false; onEvent(GroupEvent.Context("${lesson.subject} · ${lesson.date} · ${lesson.time} · ${lesson.room}")) }) }

                        DropdownMenuItem(enabled = canMedia, text = { Text(stringResource(R.string.chat_media_photo)) }, onClick = {
                            attachOpen = false; pendingPickConversation = state.activeConversationId; pendingPickTopic = state.activeTopicId; photo.launch(arrayOf("image/*")) },
                            modifier = Modifier.testTag("Group.Photo"))
                        DropdownMenuItem(enabled = canMedia, text = { Text(stringResource(R.string.group_video)) }, onClick = {
                            attachOpen = false; pendingPickConversation = state.activeConversationId; pendingPickTopic = state.activeTopicId; video.launch(arrayOf("video/*")) },
                            modifier = Modifier.testTag("Group.Video"))
                        DropdownMenuItem(enabled = canMedia, text = { Text(stringResource(R.string.group_document)) }, onClick = {
                            attachOpen = false; pendingPickConversation = state.activeConversationId; pendingPickTopic = state.activeTopicId; document.launch(arrayOf("*/*")) },
                            modifier = Modifier.testTag("Group.File"))
                    }
                }
                ZTextField(value = state.draft, onValueChange = { onEvent(GroupEvent.Draft(it)) },
                    enabled = !state.sending, modifier = Modifier.weight(1f).focusRequester(composerFocus).testTag("Group.Draft"),
                    placeholder = { Text(stringResource(R.string.group_message)) }, minLines = 2, maxLines = 4,
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Default),
                    isError = messagePreview.problem != null && messagePreview.problem != GroupMessageProblem.Empty,
                    supportingText = { Text(when (messagePreview.problem) {
                        GroupMessageProblem.TooLong -> stringResource(R.string.uxnext_group_message_too_long,
                            messagePreview.scalars)
                        GroupMessageProblem.Invalid -> stringResource(R.string.uxnext_group_message_invalid)
                        else -> stringResource(R.string.uxnext_group_message_count, messagePreview.scalars)
                    }) })
                if (!state.attachmentPending) {
                    if (state.draft.isNotBlank() || state.editing != null) {
                        ZIconButton(R.drawable.ic_send, stringResource(R.string.group_send),
                            { keyboard?.hide(); onEvent(GroupEvent.Send) }, "Group.Send",
                            enabled = !state.sending && messagePreview.ready, primary = true)
                    } else {
                        ZIconButton(R.drawable.ic_mic, stringResource(R.string.group_record_voice),
                            startVoice, "Group.Voice", enabled = !state.sending && canMedia)
                    }
                }
            }
            if (state.attachmentPending) ZButton(stringResource(R.string.group_retry),
                { onEvent(GroupEvent.Send) }, modifier = Modifier.fillMaxWidth(),
                enabled = !state.sending, tag = "Group.Send")

        }
    }
    }
}

private fun readAttachment(context: android.content.Context, uri: Uri): Pair<String, ByteArray>? {
    val name = context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
        if (cursor.moveToFirst()) cursor.getString(0) else null
    }?.substringAfterLast('/')?.substringAfterLast('\\') ?: context.getString(R.string.face_file)
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
    val uiText = rememberUiText()
    val staff = value == "headman" || value == "curator"
    ZChip(role(value), selected = staff)
}

@Composable
private fun role(value: String): String = stringResource(when (value) {
    "headman" -> R.string.group_role_headman
    "curator" -> R.string.group_role_curator
    else -> R.string.group_role_member
})
