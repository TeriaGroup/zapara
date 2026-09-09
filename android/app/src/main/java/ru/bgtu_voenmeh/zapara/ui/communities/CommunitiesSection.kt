package ru.bgtu_voenmeh.zapara.ui.communities

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextDecoration
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear

@Composable
fun CommunitiesSection(state: CommunitiesUiState, onEvent: (CommunitiesEvent) -> Unit) {
    val selected = state.selected
    if (state.pane == CommunityPane.Detail && selected != null) {
        CommunityDetail(selected, onEvent)
        return
    }
    val c = Zapara.colors
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_community))
        when (state.pane) {
            CommunityPane.Guest -> EmptyState(
                R.drawable.ic_community,
                stringResource(R.string.community_need_account),
                tag = "Empty.NeedAccount"
            )
            CommunityPane.Empty -> EmptyState(
                R.drawable.ic_community,
                stringResource(R.string.community_empty),
                tag = "Empty.Communities"
            )
            CommunityPane.Forbidden -> EmptyState(
                R.drawable.ic_community,
                stringResource(R.string.community_forbidden),
                tag = "Empty.Forbidden"
            )
            CommunityPane.Catalog, CommunityPane.Detail -> {
                LazyColumn(
                    Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(Zapara.space.l),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
                ) {
                    itemsIndexed(state.communities, key = { _, it -> it.communityId }) { index, item ->
                        ZCard(
                            onClick = if (item.canOpen) {
                                { onEvent(CommunitiesEvent.Open(item.communityId)) }
                            } else null,
                            tag = "Community.Row.${item.communityId}",
                            modifier = Modifier.fillMaxWidth().appear(index)
                        ) {
                            Text(item.name, style = Zapara.typography.bodyStrong, color = c.text1)
                            if (item.description.isNotEmpty()) {
                                Text(item.description, style = Zapara.typography.caption, color = c.text2)
                            }
                            if (item.joinStatus == "pending") {
                                Text(
                                    stringResource(R.string.community_pending),
                                    style = Zapara.typography.caption,
                                    color = c.text2,
                                    modifier = Modifier.testTag("Community.Pending.${item.communityId}")
                                )
                            } else if (item.canJoin) {
                                ZButton(
                                    stringResource(R.string.community_join),
                                    { onEvent(CommunitiesEvent.Join(item.communityId)) },
                                    ghost = true,
                                    tag = "Community.Join.${item.communityId}"
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun CommunityDetail(selected: CommunityDetailUi, onEvent: (CommunitiesEvent) -> Unit) {
    val c = Zapara.colors
    BackHandler { onEvent(CommunitiesEvent.Back) }
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.s), verticalAlignment = Alignment.CenterVertically) {
            ZIconButton(R.drawable.ic_chevron_left, stringResource(R.string.community_title), { onEvent(CommunitiesEvent.Back) }, "Community.Back")
            Text(selected.name, style = Zapara.typography.title, color = c.text1, modifier = Modifier.weight(1f))
        }
        LazyColumn(
            Modifier.fillMaxSize(),
            contentPadding = PaddingValues(Zapara.space.l),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
        ) {
            item {
                ZCard(Modifier.fillMaxWidth().appear(0), tag = "Community.Members") {
                    Text(stringResource(R.string.community_members), style = Zapara.typography.section, color = c.text1)
                    selected.members.forEach { Text(it.userId, style = Zapara.typography.caption, color = c.text2) }
                }
            }
            item {
                ZCard(Modifier.fillMaxWidth().appear(1), tag = "Community.Staff") {
                    Text(stringResource(R.string.community_staff), style = Zapara.typography.section, color = c.text1)
                    selected.staff.forEach { Text(it.userId, style = Zapara.typography.caption, color = c.text2) }
                }
            }
            if (selected.canModerate) {
                items(selected.joinRequests, key = { it.requestId }) { request ->
                    ZCard(Modifier.fillMaxWidth(), tag = "Community.Request.${request.requestId}") {
                        Text(stringResource(R.string.community_pending), style = Zapara.typography.body, color = c.text1)
                        Text(request.userId, style = Zapara.typography.caption, color = c.text2)
                        if (request.canResolve) {
                            Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                                ZButton(
                                    stringResource(R.string.community_accept),
                                    { onEvent(CommunitiesEvent.AcceptJoin(selected.communityId, request.requestId)) },
                                    tag = "Community.Accept.${request.requestId}"
                                )
                                ZButton(
                                    stringResource(R.string.community_reject),
                                    { onEvent(CommunitiesEvent.RejectJoin(selected.communityId, request.requestId)) },
                                    ghost = true,
                                    tag = "Community.Reject.${request.requestId}"
                                )
                            }
                        }
                    }
                }
            }
            item {
                Text(stringResource(R.string.community_homework), style = Zapara.typography.section, color = c.text1)
            }
            items(selected.homework, key = { it.homeworkId }) { item ->
                ZCard(Modifier.fillMaxWidth(), tag = "Community.Homework.${item.homeworkId}") {
                    Text(
                        item.title,
                        style = Zapara.typography.bodyStrong,
                        color = if (item.rejectedDraft || item.completed) c.text2 else c.text1,
                        textDecoration = if (item.completed) TextDecoration.LineThrough else null
                    )
                    Text(item.body, style = Zapara.typography.body, color = if (item.completed) c.text2 else c.text1)
                    if (item.canToggle) {
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.End) {
                            ZSwitch(
                                item.completed,
                                { onEvent(CommunitiesEvent.ToggleCompletion(selected.communityId, item.homeworkId, it, item.completionRevision)) },
                                "Community.Done.${item.homeworkId}"
                            )
                        }
                    }
                }
            }
            item {
                Text(stringResource(R.string.community_announcements), style = Zapara.typography.section, color = c.text1)
            }
            items(selected.announcements, key = { it.announcementId }) { item ->
                ZCard(Modifier.fillMaxWidth(), tag = "Community.Announcement.${item.announcementId}") {
                    Text(item.title, style = Zapara.typography.bodyStrong, color = if (item.rejectedDraft) c.text2 else c.text1)
                    Text(item.body, style = Zapara.typography.body, color = c.text1)
                }
            }
            item {
                Text(stringResource(R.string.community_polls), style = Zapara.typography.section, color = c.text1)
            }
            items(selected.polls, key = { it.pollId }) { poll ->
                ZCard(Modifier.fillMaxWidth(), tag = "Community.Poll.${poll.pollId}") {
                    Text(poll.question, style = Zapara.typography.bodyStrong, color = if (poll.rejectedDraft) c.text2 else c.text1)
                    poll.options.forEach { option ->
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            Text(option.label, style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                            if (poll.canVote) {
                                ZButton(
                                    stringResource(R.string.community_vote),
                                    { onEvent(CommunitiesEvent.Vote(selected.communityId, poll.pollId, option.optionId)) },
                                    ghost = true,
                                    tag = "Community.Vote.${poll.pollId}.${option.optionId}"
                                )
                            }
                        }
                    }
                    val results = poll.results
                    if (results == null) {
                        ZButton(
                            stringResource(R.string.community_results),
                            { onEvent(CommunitiesEvent.ShowResults(selected.communityId, poll.pollId)) },
                            ghost = true,
                            tag = "Community.Results.${poll.pollId}"
                        )
                    } else {
                        Text(stringResource(R.string.community_results), style = Zapara.typography.caption, color = c.text2)
                        results.options.forEach { option ->
                            Text("${option.label} · ${option.votes}", style = Zapara.typography.body, color = c.text1)
                        }
                    }
                }
            }
        }
    }
}
