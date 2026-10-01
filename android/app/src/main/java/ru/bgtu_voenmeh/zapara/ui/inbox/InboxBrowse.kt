package ru.bgtu_voenmeh.zapara.ui.inbox

import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.InboxSource
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import ru.bgtu_voenmeh.zapara.ui.chat.chatSearchWords
import ru.bgtu_voenmeh.zapara.ui.chat.normalizeChatSearch

internal enum class InboxSourceFilter(val source: InboxSource?) {
    All(null), Group(InboxSource.Group), GroupDirect(InboxSource.GroupDirect), Friend(InboxSource.Friend)
}

internal fun browseInbox(rows: List<InboxRow>, query: String,
    source: InboxSourceFilter = InboxSourceFilter.All, unreadOnly: Boolean = false): List<InboxRow> {
    val needle = normalizeChatSearch(query)
    return rows.filter { row ->
        (source.source == null || row.source == source.source) && (!unreadOnly || row.unread > 0) &&
            (needle.isEmpty() || normalizeChatSearch(row.title).contains(needle) ||
                normalizeChatSearch(row.lastBody.orEmpty()).contains(needle))
    }
}

internal fun unreadInboxConversations(rows: List<InboxRow>): Int = rows.count { it.unread > 0 }

internal fun copyablePersonalText(message: SocialMessage?): String? =
    message?.takeIf { !it.deleted && it.kind == "text" }?.body?.takeIf { it.isNotBlank() }

internal enum class PersonalQuoteTarget { Loaded, Deleted, Earlier }

internal fun personalQuoteTarget(messages: List<SocialMessage>, targetId: String): PersonalQuoteTarget =
    messages.firstOrNull { it.id == targetId }?.let {
        if (it.deleted) PersonalQuoteTarget.Deleted else PersonalQuoteTarget.Loaded
    } ?: PersonalQuoteTarget.Earlier

internal fun browseLoadedPersonalHistory(messages: List<SocialMessage>, query: String): List<SocialMessage> {
    val words = chatSearchWords(query)
    if (words.isEmpty()) return messages
    return messages.filter { message ->
        if (message.deleted) return@filter false
        val searchable = normalizeChatSearch(when (message.kind) {
            "text" -> message.body.orEmpty()
            "image", "file", "voice", "circle" -> message.fileName.orEmpty()
            else -> ""
        } + " " + message.senderName)
        searchable.isNotBlank() && words.all { word ->
            searchable.contains(word)
        }
    }
}

internal fun totalInboxUnread(rows: List<InboxRow>): Long = rows.sumOf { it.unread.coerceAtLeast(0).toLong() }

internal fun formatInboxTime(time: Instant, zone: ZoneId): String =
    DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm").withZone(zone).format(time)
