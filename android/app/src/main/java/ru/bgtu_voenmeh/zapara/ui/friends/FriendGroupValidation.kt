package ru.bgtu_voenmeh.zapara.ui.friends

import ru.bgtu_voenmeh.zapara.data.GroupInfo

internal fun validFriendGroupName(input: String, catalog: List<GroupInfo>, manualOffline: Boolean,
    existingName: String? = null): String? {
    val clean = input.trim()
    if (clean.length < 2) return null
    catalog.firstOrNull { it.id == clean || it.name.equals(clean, ignoreCase = true) }?.let { return it.name }
    if (existingName?.equals(clean, ignoreCase = true) == true) return existingName
    return clean.takeIf { catalog.isEmpty() && manualOffline }
}
