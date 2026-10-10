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
    source: InboxSourceFilter = InboxSourceFilter.All, unreadOnly: Boolean = false,
    draftIds: Set<String> = emptySet(), draftsOnly: Boolean = false,
    unreadFirst: Boolean = false, subtitleOf: (InboxRow) -> String = { it.subtitle }): List<InboxRow> {
    val words = chatSearchWords(query)
    val matches = rows.filter { row ->
        val searchable = normalizeChatSearch("${row.title} ${subtitleOf(row)} ${row.lastBody.orEmpty()}")
        (source.source == null || row.source == source.source) && (!unreadOnly || row.unread > 0) &&
            (!draftsOnly || row.id in draftIds) && words.all(searchable::contains)
    }
    return if (unreadFirst) matches.sortedByDescending { it.unread > 0 } else matches
}

/** Подписи строк из ресурсов (#106): данные несут только название группы, слова — здесь. */
internal class InboxSubtitleCopy(val personal: String, val group: String, val personalIn: (String) -> String)

internal fun inboxSubtitleText(row: InboxRow, copy: InboxSubtitleCopy): String = when (row.source) {
    InboxSource.GroupDirect -> if (row.subtitle.isBlank()) copy.personal else copy.personalIn(row.subtitle)
    InboxSource.Group -> row.subtitle.ifBlank { copy.group }
    else -> row.subtitle.ifBlank { copy.personal }
}

internal fun unreadInboxConversations(rows: List<InboxRow>): Int = rows.count { it.unread > 0 }

internal fun copyablePersonalText(message: SocialMessage?): String? =
    message?.takeIf { !it.deleted && it.kind == "text" }?.body?.takeIf { it.isNotBlank() }

internal enum class PersonalQuoteTarget { Loaded, Deleted, Earlier }

internal fun personalQuoteTarget(messages: List<SocialMessage>, targetId: String): PersonalQuoteTarget =
    messages.firstOrNull { it.id == targetId }?.let {
        if (it.deleted) PersonalQuoteTarget.Deleted else PersonalQuoteTarget.Loaded
    } ?: PersonalQuoteTarget.Earlier

internal enum class PersonalHistoryKind { All, Text, PhotoVideo, Documents, VoiceCircle }
internal enum class PersonalHistoryAuthor { All, Mine, Others }

internal fun browseLoadedPersonalHistory(messages: List<SocialMessage>, query: String,
    kind: PersonalHistoryKind = PersonalHistoryKind.All,
    author: PersonalHistoryAuthor = PersonalHistoryAuthor.All, userId: String = ""): List<SocialMessage> {
    val words = chatSearchWords(query)
    if (words.isEmpty() && kind == PersonalHistoryKind.All && author == PersonalHistoryAuthor.All) return messages
    return messages.filter { message ->
        if (message.deleted) return@filter false
        if (author != PersonalHistoryAuthor.All && (message.senderId == userId) != (author == PersonalHistoryAuthor.Mine)) return@filter false
        val matchesKind = when (kind) {
            PersonalHistoryKind.All -> true
            PersonalHistoryKind.Text -> message.kind == "text"
            PersonalHistoryKind.PhotoVideo -> message.kind in setOf("image", "video")
            PersonalHistoryKind.Documents -> message.kind == "file"
            PersonalHistoryKind.VoiceCircle -> message.kind in setOf("voice", "circle")
        }
        if (!matchesKind) return@filter false
        val searchable = normalizeChatSearch(when (message.kind) {
            "text" -> message.body.orEmpty()
            "image", "file", "voice", "circle" -> message.fileName.orEmpty()
            else -> ""
        } + " " + message.senderName)
        words.all { word ->
            searchable.contains(word)
        }
    }
}

internal fun totalInboxUnread(rows: List<InboxRow>): Long = rows.sumOf { it.unread.coerceAtLeast(0).toLong() }

internal fun formatInboxTime(time: Instant, zone: ZoneId): String =
    DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm").withZone(zone).format(time)
