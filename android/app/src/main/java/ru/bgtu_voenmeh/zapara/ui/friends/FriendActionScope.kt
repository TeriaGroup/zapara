package ru.bgtu_voenmeh.zapara.ui.friends

data class FriendActionScope(val id: Long, val groupId: String, val profileName: String) {
    fun current(groupId: String, profileName: String): Boolean =
        this.groupId == groupId && this.profileName == profileName

    fun find(rows: List<FriendUi>): FriendUi? = rows.firstOrNull { it.id == id }
}
