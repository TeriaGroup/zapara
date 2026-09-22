package ru.bgtu_voenmeh.zapara.ui.groups

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun GroupSection(state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    if (state.hasHome) BackHandler { onEvent(GroupEvent.Back) }
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
                    Text(
                        stringResource(R.string.group_load_failed),
                        color = Zapara.colors.bad,
                        style = Zapara.typography.caption,
                        modifier = Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s).testTag("Group.Error")
                    )
                }
                CommunityList(state, onEvent, Modifier.weight(1f))
            }
            else -> Home(state, onEvent)
        }
    }
}

@Composable
private fun CommunityList(state: GroupUiState, onEvent: (GroupEvent) -> Unit, modifier: Modifier = Modifier.fillMaxSize()) {
    LazyColumn(
        modifier,
        contentPadding = PaddingValues(Zapara.space.l),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
    ) {
        items(state.communities, key = { it.id }) { item ->
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
private fun Home(state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
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
                stringResource(R.string.group_people),
                { onEvent(GroupEvent.People) },
                modifier = Modifier.weight(1f),
                ghost = !state.showPeople,
                tag = "Group.People"
            )
            ZButton(
                state.chatTitle.ifEmpty { stringResource(R.string.group_chat) },
                { onEvent(GroupEvent.Chat) },
                modifier = Modifier.weight(1f),
                ghost = state.showPeople,
                tag = "Group.ChatTab"
            )
        }
        if (state.failed) Text(stringResource(R.string.group_failed), color = c.bad, modifier = Modifier.testTag("Group.Error"))
        if (state.direct && !state.showPeople) {
            ZButton(stringResource(R.string.group_back), { onEvent(GroupEvent.GroupChat) }, ghost = true, tag = "Group.Back")
        }
        if (state.showPeople) {
            People(state, onEvent, Modifier.weight(1f))
        } else {
            Messages(state, onEvent, Modifier.weight(1f))
            Composer(state, onEvent)
        }
    }
}

@Composable
private fun People(state: GroupUiState, onEvent: (GroupEvent) -> Unit, modifier: Modifier) {
    val c = Zapara.colors
    LazyColumn(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
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
        items(state.people, key = { it.id }) { person ->
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
    if (state.chatLoading && state.messages.isEmpty()) {
        Box(modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            Text(stringResource(R.string.group_loading), style = Zapara.typography.caption, color = c.text2)
        }
    } else if (state.messages.isEmpty() && !state.hasMore) {
        Box(modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            Text(stringResource(R.string.group_no_messages), style = Zapara.typography.caption, color = c.text2)
        }
    } else {
        LazyColumn(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
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
                MessageBubble(message)
            }
        }
    }
}

@Composable
private fun MessageBubble(message: GroupMessageUi) {
    val c = Zapara.colors
    val mine = message.mine
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
            modifier = Modifier.widthIn(max = 280.dp)
        ) {
            Column(
                Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)
            ) {
                Text(message.body, style = Zapara.typography.body)
                Text(
                    message.time,
                    style = Zapara.typography.caption,
                    color = if (mine) c.onAccent.copy(alpha = 0.72f) else c.text2,
                    modifier = Modifier.align(Alignment.End)
                )
            }
        }
    }
}

@Composable
private fun Composer(state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val c = Zapara.colors
    Row(
        Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)
    ) {
        OutlinedTextField(
            value = state.draft,
            onValueChange = { onEvent(GroupEvent.Draft(it)) },
            modifier = Modifier.weight(1f).testTag("Group.Draft"),
            placeholder = { Text(stringResource(R.string.group_message), style = Zapara.typography.caption, color = c.text3) },
            singleLine = true,
            shape = RoundedCornerShape(Zapara.radii.control),
            colors = OutlinedTextFieldDefaults.colors(
                focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
                focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
                focusedTextColor = c.text1, unfocusedTextColor = c.text1
            )
        )
        ZButton(stringResource(R.string.group_send), { onEvent(GroupEvent.Send) }, enabled = state.draft.isNotBlank(), tag = "Group.Send")
    }
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
