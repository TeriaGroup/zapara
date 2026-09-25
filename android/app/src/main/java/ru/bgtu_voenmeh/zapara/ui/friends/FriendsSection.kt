package ru.bgtu_voenmeh.zapara.ui.friends

import androidx.compose.foundation.border
import androidx.compose.foundation.LocalIndication
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.runtime.remember
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberUpdatedState
import kotlinx.coroutines.delay
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.FriendDot
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.SkeletonList
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.shell.GroupPickerSheet
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear
import ru.bgtu_voenmeh.zapara.data.Intersection
import java.time.format.DateTimeFormatter
import java.util.Locale

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun FriendsSection(state: FriendsUiState, onEvent: (FriendsEvent) -> Unit) {
    val c = Zapara.colors
    val latestEvent = rememberUpdatedState(onEvent)
    LaunchedEffect(Unit) {
        while (true) {
            delay(60_000)
            latestEvent.value(FriendsEvent.Retry)
        }
    }
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_friends)) {
            if (state.canAdd) ZIconButton(R.drawable.ic_plus, stringResource(R.string.add), { onEvent(FriendsEvent.Add) }, "Friends.Add")
        }
        if (!state.loaded) Box(Modifier.fillMaxSize().padding(Zapara.space.l)) { SkeletonList() }
        else LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            if (state.friends.isNotEmpty() || !state.failed) item("overview") {
                FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    ZChip(stringResource(R.string.friends_detail_count, state.friends.size), tag = "Friends.Count")
                    ZChip(stringResource(R.string.friends_detail_active, state.friends.count { it.enabled }), tag = "Friends.Active")
                }
            }
            if (state.failed) item("load-failed") {
                ZCard(Modifier.fillMaxWidth(), tag = "Friends.LoadFailed") {
                    Text(stringResource(R.string.friends_detail_load_failed), style = Zapara.typography.bodyStrong, color = c.bad)
                    ZButton(stringResource(R.string.friends_detail_retry), { onEvent(FriendsEvent.Retry) }, ghost = true)
                }
            }
            if (!state.failed) item("forecast") {
                ZCard(Modifier.fillMaxWidth(), tag = "Friends.Forecast") {
                    Text(stringResource(R.string.friends_forecast_title), style = Zapara.typography.section, color = c.text1)
                    Text(stringResource(R.string.friends_forecast_hint), style = Zapara.typography.caption, color = c.text2)
                    ZButton(stringResource(if (state.refreshing) R.string.friends_forecast_refreshing else R.string.friends_forecast_refresh),
                        { onEvent(FriendsEvent.RefreshSchedules) }, ghost = true, enabled = !state.refreshing, tag = "Friends.RefreshSchedules")
                    if (state.refreshFailed) Text(stringResource(R.string.friends_forecast_refresh_failed),
                        style = Zapara.typography.caption, color = c.bad)
                    when {
                        !state.hasOwnSchedule -> Text(stringResource(R.string.friends_forecast_no_schedule), style = Zapara.typography.body, color = c.text2)
                        state.friends.none { it.enabled } -> Text(stringResource(R.string.friends_forecast_no_friends), style = Zapara.typography.body, color = c.text2)
                        state.encounters.isEmpty() -> Text(state.previewLine, style = Zapara.typography.body, color = c.text2, modifier = Modifier.testTag("Friends.Preview"))
                    }
                    val dateFormat = DateTimeFormatter.ofPattern("EEE d MMM", Locale.forLanguageTag("ru"))
                    state.encounters.forEachIndexed { index, encounter ->
                        Row(Modifier.fillMaxWidth().padding(vertical = Zapara.space.xs),
                            verticalAlignment = Alignment.Top, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                            FriendDot(FriendPalette.indexOf(encounter.colorHex), size = 10.dp)
                            Column(Modifier.weight(1f)) {
                                Text("${encounter.date.format(dateFormat)} · ${encounter.time} · ${encounter.subject}",
                                    style = Zapara.typography.bodyStrong, color = c.text1,
                                    modifier = Modifier.testTag("Friends.Encounter.$index"))
                                Text(listOf(encounter.groupName, encounter.members).filter { it.isNotBlank() }.joinToString(" · "),
                                    style = Zapara.typography.body, color = c.text1)
                                val place = Intersection.scoreToTextRu(encounter.score)
                                Text(listOf(place, encounter.friendRoom).filter { it.isNotBlank() }.joinToString(" · "),
                                    style = Zapara.typography.caption, color = c.text2)
                            }
                        }
                    }
                    if (state.missingGroups.isNotEmpty()) {
                        Text(stringResource(R.string.friends_forecast_missing, state.missingGroups.joinToString(", ")),
                            style = Zapara.typography.caption, color = c.text2)
                    }
                }
            }
            if (state.friends.isEmpty() && !state.failed) item("empty") {
                ZCard(Modifier.fillMaxWidth(), tag = "Empty.Friends") {
                    Text(stringResource(R.string.friends_detail_empty), style = Zapara.typography.bodyStrong, color = c.text1)
                    Text(stringResource(R.string.friends_detail_empty_hint), style = Zapara.typography.caption, color = c.text2)
                    if (state.canAdd) ZButton(stringResource(R.string.add), { onEvent(FriendsEvent.Add) }, tag = "Friends.AddFirst")
                }
            }
            itemsIndexed(state.friends, key = { _, it -> it.id }) { index, friend ->
                ZCard(onClick = { onEvent(FriendsEvent.Edit(friend.index)) }, tag = "Friends.Row.${friend.index}", modifier = Modifier.fillMaxWidth().appear(index)) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        FriendDot(friend.colorIndex, size = 10.dp)
                        Column(Modifier.weight(1f)) {
                            Text(friend.groupName, style = Zapara.typography.bodyStrong, color = c.text1)
                            Text(friend.members, style = Zapara.typography.caption, color = c.text2)
                        }
                        val enabledLabel = stringResource(R.string.friends_enabled_label, friend.groupName)
                        ZSwitch(friend.enabled, { onEvent(FriendsEvent.Toggle(friend.index, it)) }, "Friends.Enabled.${friend.index}",
                            Modifier.semantics { contentDescription = enabledLabel })
                    }
                }
            }
            item {
                ZCard(Modifier.fillMaxWidth()) {
                    Text(stringResource(R.string.friends_intersections), style = Zapara.typography.section, color = c.text1)
                    Text(stringResource(R.string.friends_forecast_source_hint), style = Zapara.typography.caption, color = c.text2)
                    Text(stringResource(R.string.friends_strictness), style = Zapara.typography.body, color = c.text1)
                    val strictnessLabel = stringResource(R.string.friends_strictness)
                    val active = Strictness.nearest(state.strictness)
                    val steps = listOf(
                        25 to R.string.strict_uni,
                        50 to R.string.strict_building,
                        75 to R.string.strict_floor,
                        100 to R.string.strict_room
                    )
                    FlowRow(Modifier.fillMaxWidth().testTag("Friends.Strictness").semantics { contentDescription = strictnessLabel }, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                        verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        steps.forEach { (value, label) ->
                            ZChip(
                                stringResource(label),
                                selected = value == active,
                                onClick = { onEvent(FriendsEvent.Strictness(value)) },
                                tag = "Friends.Strict.$value"
                            )
                        }
                    }
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        val label = stringResource(R.string.friends_always_show)
                        Text(label, style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f).clearAndSetSemantics {})
                        ZSwitch(state.alwaysShow, { onEvent(FriendsEvent.AlwaysShow(it)) }, "Friends.AlwaysShow", Modifier.semantics { contentDescription = label })
                    }
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        val label = stringResource(R.string.friends_invert)
                        Column(Modifier.weight(1f)) {
                            Text(label, style = Zapara.typography.body, color = c.text1, modifier = Modifier.clearAndSetSemantics {})
                            Text(stringResource(R.string.friends_invert_hint), style = Zapara.typography.caption, color = c.text2)
                        }
                        ZSwitch(state.invert, { onEvent(FriendsEvent.Invert(it)) }, "Friends.Invert", Modifier.semantics { contentDescription = label })
                    }
                }
            }
        }
    }
    state.editor?.let { editor ->
        FriendEditorSheet(editor, state, onEvent)
        if (editor.pickerOpen) {
            val available = state.groups.filter { group ->
                group.id != state.myGroupId && state.friends.none { friend ->
                    friend.id != editor.id && (friend.groupName.equals(group.name, ignoreCase = true) || friend.groupName == group.id)
                }
            }
            GroupPickerSheet(available, null, { id ->
                val name = state.groups.firstOrNull { it.id == id }?.name ?: id
                onEvent(FriendsEvent.EditorGroup(name))
            }, { onEvent(FriendsEvent.ClosePicker) })
        }
    }
    state.confirmDelete?.let {
        AlertDialog(
            onDismissRequest = { onEvent(FriendsEvent.CancelDelete) },
            title = { Text(stringResource(R.string.friends_delete_title), style = Zapara.typography.section) },
            confirmButton = { ZButton(stringResource(R.string.delete), { onEvent(FriendsEvent.ConfirmDelete) }) },
            dismissButton = { ZButton(stringResource(R.string.theme_cancel), { onEvent(FriendsEvent.CancelDelete) }, ghost = true) },
            containerColor = Zapara.colors.card
        )
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun FriendEditorSheet(editor: FriendEditorUi, state: FriendsUiState, onEvent: (FriendsEvent) -> Unit) {
    val c = Zapara.colors
    ZBottomSheet({ onEvent(FriendsEvent.EditorCancel) }, "Sheet.Friend") {
        Text(stringResource(R.string.friends_group), style = Zapara.typography.section, color = c.text1)
        ZButton(editor.groupName.ifBlank { stringResource(R.string.group_pick) }, { onEvent(FriendsEvent.OpenPicker) }, ghost = true)
        OutlinedTextField(
            value = editor.members, onValueChange = { onEvent(FriendsEvent.EditorMembers(it)) },
            modifier = Modifier.fillMaxWidth().testTag("Editor.Text"),
            placeholder = { Text(stringResource(R.string.friends_members), color = c.text3) },
            singleLine = true,
            shape = RoundedCornerShape(Zapara.radii.control),
            colors = OutlinedTextFieldDefaults.colors(
                focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
                focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
                focusedTextColor = c.text1, unfocusedTextColor = c.text1
            )
        )
        val colorNames = listOf(R.string.friend_color_orange, R.string.friend_color_green,
            R.string.friend_color_blue, R.string.friend_color_purple, R.string.friend_color_pink)
        FlowRow(Modifier.fillMaxWidth().selectableGroup(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            repeat(5) { i ->
                val selected = editor.colorIndex == i
                val label = stringResource(R.string.friend_color_label, stringResource(colorNames[i]))
                val selection = stringResource(if (selected) R.string.control_selected else R.string.control_not_selected)
                Box(Modifier.size(Zapara.space.minTouch)
                        .testTag("Editor.Color.$i")
                        .clip(CircleShape)
                        .selectable(selected, role = Role.RadioButton,
                            interactionSource = remember { MutableInteractionSource() },
                            indication = if (Zapara.motion.enabled) LocalIndication.current else null,
                            onClick = { onEvent(FriendsEvent.EditorColor(i)) })
                        .semantics { contentDescription = label; stateDescription = selection },
                    contentAlignment = Alignment.Center) {
                    FriendDot(i, size = Zapara.space.xl, modifier =
                        if (selected) Modifier.border(Zapara.space.hairline * 2, c.text1, CircleShape) else Modifier)
                }
            }
        }
        state.editorError?.let { Text(it, style = Zapara.typography.caption, color = c.bad) }
        FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.theme_cancel), { onEvent(FriendsEvent.EditorCancel) }, ghost = true, tag = "Editor.Cancel")
            if (editor.id != null) ZButton(stringResource(R.string.delete), { onEvent(FriendsEvent.AskDelete(editor.id)) }, ghost = true, tag = "Editor.Delete")
            ZButton(stringResource(R.string.theme_save), { onEvent(FriendsEvent.EditorSave) }, enabled = editor.groupName.isNotBlank(), tag = "Editor.Save")
        }
    }
}
