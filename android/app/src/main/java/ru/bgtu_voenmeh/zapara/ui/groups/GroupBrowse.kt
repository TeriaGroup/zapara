package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic

private fun matches(query: String, vararg values: String): Boolean {
    val needle = query.trim()
    return needle.isEmpty() || values.any { it.contains(needle, ignoreCase = true) }
}

internal fun browseCommunities(rows: List<GroupCommunityUi>, query: String): List<GroupCommunityUi> =
    rows.filter { matches(query, it.name) }

internal fun browsePeople(rows: List<GroupPersonUi>, query: String): List<GroupPersonUi> =
    rows.filter { matches(query, it.name, it.handle) }

internal fun browseChannels(rows: List<GroupTopic>, query: String = "", kind: String = "all",
                             unreadOnly: Boolean = false, categoryOrder: Map<String, Int> = emptyMap()): List<GroupTopic> =
    rows.withIndex()
        .filter { (_, channel) ->
            (kind == "all" || channel.kind == kind) &&
                (!unreadOnly || channel.unread > 0) &&
                matches(query, channel.title, channel.description)
        }
        .sortedWith(compareBy<IndexedValue<GroupTopic>> { if (it.value.topicId == null && it.value.kind == "chat") 0 else 1 }
            .thenBy { categoryOrder[it.value.categoryId] ?: -1 }.thenBy { it.value.categoryId ?: "" }
            .thenBy { if (it.value.pinned) 0 else 1 }.thenBy { it.value.position }.thenByDescending { it.value.unread > 0 }.thenBy { it.index })
        .map { it.value }

internal fun categoryExpanded(id: String?, collapsed: Collection<String>, filtered: Boolean): Boolean = id == null || filtered || id !in collapsed
