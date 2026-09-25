package ru.bgtu_voenmeh.zapara.ui.groups

import java.time.Duration
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic

internal enum class MessageAuthor { All, Mine, Others }
internal enum class MessageKind { All, Text, PhotoVideo, Documents, VoiceCircle }

internal fun copyableMessageText(message: GroupMessageUi): String? =
    message.body.takeIf { !message.deleted && message.kind == "text" && it.isNotBlank() }

internal fun sameMessageCluster(before: GroupMessageUi?, after: GroupMessageUi): Boolean {
    if (before == null || before.deleted || after.deleted || before.senderId.isBlank() ||
        before.senderId != after.senderId || before.day.isBlank() || before.day != after.day) return false
    val firstTime = before.createdAt ?: return false
    val nextTime = after.createdAt ?: return false
    val gap = Duration.between(firstTime, nextTime)
    return !gap.isNegative && gap <= Duration.ofMinutes(5)
}

internal fun browseMessages(
    rows: List<GroupMessageUi>, query: String,
    author: MessageAuthor = MessageAuthor.All,
    kind: MessageKind = MessageKind.All
): List<GroupMessageUi> {
    val needle = query.trim()
    val filtered = needle.isNotEmpty() || author != MessageAuthor.All || kind != MessageKind.All
    return rows.filter { message ->
        if (message.deleted) return@filter !filtered
        (needle.isEmpty() || message.body.contains(needle, ignoreCase = true)) &&
            (author == MessageAuthor.All || message.mine == (author == MessageAuthor.Mine)) &&
            when (kind) {
                MessageKind.All -> true
                MessageKind.Text -> message.kind == "text"
                MessageKind.PhotoVideo -> message.kind == "image" || message.kind == "video"
                MessageKind.Documents -> message.kind == "file"
                MessageKind.VoiceCircle -> message.kind == "voice" || message.kind == "circle"
            }
    }
}

internal fun nextUnreadChannel(
    channels: List<GroupTopic>, activeTopicId: String?, inChannel: Boolean
): GroupTopic? {
    val current = if (inChannel) channels.indexOfFirst { it.topicId == activeTopicId } else -1
    val start = if (current >= 0) current + 1 else 0
    return (channels.drop(start) + channels.take(start)).firstOrNull { channel ->
        channel.kind in setOf("chat", "ballots") && channel.unread > 0 &&
            (!inChannel || channel.topicId != activeTopicId)
    }
}
