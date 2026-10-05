package ru.bgtu_voenmeh.zapara.data.communities

data class GroupChatUpdates(val messages: List<ChatMessage>, val hasMore: Boolean)

/** Catch up in order. A bounded partial result keeps its real cursor for the next cycle. */
suspend fun loadGroupChatUpdates(after: String?, maxPages: Int = 20, load: suspend (String?) -> ChatPage): GroupChatUpdates {
    require(maxPages > 0)
    val messages = linkedMapOf<String, ChatMessage>()
    val cursors = mutableSetOf<String>()
    var cursor = after
    repeat(maxPages) {
        val page = load(cursor)
        page.messages.forEach { messages[it.messageId] = it }
        if (after == null || !page.hasMore) return GroupChatUpdates(messages.values.toList(), page.hasMore)
        val next = page.messages.lastOrNull()?.messageId
        if (next == null || next == cursor || !cursors.add(next))
            throw CommunityClientException(CommunityClientFailure.InvalidPayload)
        cursor = next
    }
    return GroupChatUpdates(messages.values.toList(), hasMore = true)
}
