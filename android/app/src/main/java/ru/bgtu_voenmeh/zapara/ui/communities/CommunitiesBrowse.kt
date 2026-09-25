package ru.bgtu_voenmeh.zapara.ui.communities

fun browseCommunities(rows: List<CommunityListItemUi>, query: String): List<CommunityListItemUi> {
    val needle = query.trim()
    if (needle.isEmpty()) return rows
    return rows.filter { it.name.contains(needle, ignoreCase = true) || it.description.contains(needle, ignoreCase = true) }
}
