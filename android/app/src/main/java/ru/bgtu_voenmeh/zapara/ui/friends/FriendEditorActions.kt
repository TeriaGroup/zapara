package ru.bgtu_voenmeh.zapara.ui.friends

import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.db.FriendEntity

/** The editor, toggle and delete controls share the repository's atomic local+outbox writes. */
class FriendEditorActions(private val repo: ScheduleRepository) {
    fun save(id: Long?, group: String, members: String, color: String) {
        if (id == null) repo.insertFriend(FriendEntity(groupName = group, memberNames = members, colorHex = color))
        else repo.friends().firstOrNull { it.id == id }?.let {
            repo.updateFriend(it.copy(groupName = group, memberNames = members, colorHex = color))
        }
    }
    fun toggle(id: Long, enabled: Boolean) {
        repo.friends().firstOrNull { it.id == id }?.let { repo.updateFriend(it.copy(enabled = enabled)) }
    }
    fun delete(id: Long) = repo.deleteFriend(id)
}
