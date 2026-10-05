package ru.bgtu_voenmeh.zapara.ui.friends

import java.time.LocalDate

internal fun browseFriends(rows: List<FriendUi>, query: String, status: Int): List<FriendUi> {
    val words = query.trim().lowercase().replace('ё', 'е').split(Regex("\\s+")).filter(String::isNotBlank)
    return rows.filter { row ->
        (status == 0 || row.enabled == (status == 1)) && words.all {
            (row.groupName + " " + row.members).lowercase().replace('ё', 'е').contains(it)
        }
    }
}

internal fun browseEncounters(rows: List<FriendEncounter>, day: Int, today: LocalDate): List<FriendEncounter> =
    rows.filter { day == 0 || it.date == today.plusDays((day - 1).toLong()) }

internal fun friendEditorDirty(editor: FriendEditorUi, original: FriendUi?, initialColor: Int = 0): Boolean =
    if (editor.id == null) editor.groupName.isNotBlank() || editor.members.isNotBlank() || editor.colorIndex != initialColor
    else original == null || editor.groupName.trim() != original.groupName.trim() ||
        editor.members.trim() != original.members.trim() || editor.colorIndex != original.colorIndex
