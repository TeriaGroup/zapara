package ru.bgtu_voenmeh.zapara.ui.shell

import ru.bgtu_voenmeh.zapara.data.GroupInfo

internal fun searchGroups(groups: List<GroupInfo>, query: String): List<GroupInfo> {
    val words = query.trim().lowercase().replace('ё', 'е').split(Regex("\\s+")).filter(String::isNotBlank)
    return groups.filter { group ->
        val text = (group.name + " " + group.id).lowercase().replace('ё', 'е')
        words.all(text::contains)
    }
}
