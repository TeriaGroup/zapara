package ru.bgtu_voenmeh.zapara.ui.friends

import ru.bgtu_voenmeh.zapara.data.GroupInfo

data class FriendUi(
    val id: Long,
    val index: Int,
    val groupName: String,
    val members: String,
    val enabled: Boolean,
    val colorIndex: Int
)

data class FriendEditorUi(
    val id: Long?,
    val index: Int?,
    val groupName: String,
    val members: String,
    val colorIndex: Int,
    val pickerOpen: Boolean = false
)

data class FriendsUiState(
    val loaded: Boolean = false,
    val friends: List<FriendUi> = emptyList(),
    val canAdd: Boolean = true,
    val editor: FriendEditorUi? = null,
    val confirmDelete: Long? = null,
    val strictness: Int = 25,
    val alwaysShow: Boolean = false,
    val invert: Boolean = false,
    val groups: List<GroupInfo> = emptyList()
)

sealed interface FriendsEvent {
    data object Add : FriendsEvent
    data class Edit(val index: Int) : FriendsEvent
    data class EditorGroup(val name: String) : FriendsEvent
    data class EditorMembers(val text: String) : FriendsEvent
    data class EditorColor(val index: Int) : FriendsEvent
    data object OpenPicker : FriendsEvent
    data object ClosePicker : FriendsEvent
    data object EditorSave : FriendsEvent
    data object EditorCancel : FriendsEvent
    data class Toggle(val index: Int, val enabled: Boolean) : FriendsEvent
    data class AskDelete(val id: Long) : FriendsEvent
    data object ConfirmDelete : FriendsEvent
    data object CancelDelete : FriendsEvent
    data class Strictness(val value: Int) : FriendsEvent
    data class AlwaysShow(val value: Boolean) : FriendsEvent
    data class Invert(val value: Boolean) : FriendsEvent
}
