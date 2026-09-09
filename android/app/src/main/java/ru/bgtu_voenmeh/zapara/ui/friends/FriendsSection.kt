package ru.bgtu_voenmeh.zapara.ui.friends

import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
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
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.FriendDot
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import ru.bgtu_voenmeh.zapara.ui.shell.GroupPickerSheet
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.appear
import kotlin.math.roundToInt

@Composable
fun FriendsSection(state: FriendsUiState, onEvent: (FriendsEvent) -> Unit) {
    val c = Zapara.colors
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_friends)) {
            if (state.canAdd) ZIconButton(R.drawable.ic_plus, stringResource(R.string.add), { onEvent(FriendsEvent.Add) }, "Friends.Add")
        }
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            itemsIndexed(state.friends, key = { _, it -> it.id }) { index, friend ->
                ZCard(onClick = { onEvent(FriendsEvent.Edit(friend.index)) }, tag = "Friends.Row.${friend.index}", modifier = Modifier.fillMaxWidth().appear(index)) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        FriendDot(friend.colorIndex, size = 10.dp)
                        Column(Modifier.weight(1f)) {
                            Text(friend.groupName, style = Zapara.typography.bodyStrong, color = c.text1)
                            Text(friend.members, style = Zapara.typography.caption, color = c.text2)
                        }
                        ZSwitch(friend.enabled, { onEvent(FriendsEvent.Toggle(friend.index, it)) }, "Friends.Enabled.${friend.index}")
                    }
                }
            }
            item { Text(stringResource(R.string.friends_max), style = Zapara.typography.caption, color = c.text2) }
            item {
                ZCard(Modifier.fillMaxWidth()) {
                    Text(stringResource(R.string.friends_intersections), style = Zapara.typography.section, color = c.text1)
                    Text(stringResource(R.string.friends_strictness), style = Zapara.typography.body, color = c.text1)
                    Slider(
                        value = state.strictness.toFloat(),
                        onValueChange = { onEvent(FriendsEvent.Strictness(it.roundToInt())) },
                        valueRange = 25f..100f,
                        steps = 3,
                        modifier = Modifier.testTag("Friends.Strictness"),
                        colors = SliderDefaults.colors(thumbColor = c.accent, activeTrackColor = c.accent, inactiveTrackColor = c.lineStrong)
                    )
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        Text(stringResource(R.string.strict_uni), style = Zapara.typography.caption, color = c.text2)
                        Text(stringResource(R.string.strict_building), style = Zapara.typography.caption, color = c.text2)
                        Text(stringResource(R.string.strict_floor), style = Zapara.typography.caption, color = c.text2)
                        Text(stringResource(R.string.strict_room), style = Zapara.typography.caption, color = c.text2)
                    }
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.friends_always_show), style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                        ZSwitch(state.alwaysShow, { onEvent(FriendsEvent.AlwaysShow(it)) }, "Friends.AlwaysShow")
                    }
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(stringResource(R.string.friends_invert), style = Zapara.typography.body, color = c.text1)
                            Text(stringResource(R.string.friends_invert_hint), style = Zapara.typography.caption, color = c.text2)
                        }
                        ZSwitch(state.invert, { onEvent(FriendsEvent.Invert(it)) }, "Friends.Invert")
                    }
                }
            }
        }
    }
    state.editor?.let { editor ->
        FriendEditorSheet(editor, state, onEvent)
        if (editor.pickerOpen) {
            GroupPickerSheet(state.groups, null, { id ->
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
        Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            repeat(5) { i ->
                val selected = editor.colorIndex == i
                FriendDot(
                    i, size = 24.dp,
                    modifier = Modifier
                        .testTag("Editor.Color.$i")
                        .clip(CircleShape)
                        .then(if (selected) Modifier.border(2.dp, c.text1, CircleShape) else Modifier)
                        .clickable { onEvent(FriendsEvent.EditorColor(i)) }
                )
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.theme_cancel), { onEvent(FriendsEvent.EditorCancel) }, ghost = true, tag = "Editor.Cancel")
            Spacer(Modifier.weight(1f))
            if (editor.id != null) ZButton(stringResource(R.string.delete), { onEvent(FriendsEvent.AskDelete(editor.id)) }, ghost = true, tag = "Editor.Delete")
            ZButton(stringResource(R.string.theme_save), { onEvent(FriendsEvent.EditorSave) }, enabled = editor.groupName.isNotBlank(), tag = "Editor.Save")
        }
    }
}
