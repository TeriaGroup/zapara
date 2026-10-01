package ru.bgtu_voenmeh.zapara.ui.groups

data class GroupMediaSaveTarget(
    val ownerId: String,
    val communityId: String,
    val conversationId: String,
    val topicId: String?,
    val messageId: String
)

internal fun canSaveGroupAttachment(
    ownerId: String?, communityId: String?, conversationId: String?, topicId: String?,
    target: GroupMediaSaveTarget, message: GroupMessageUi?
): Boolean = ownerId == target.ownerId && communityId == target.communityId && conversationId == target.conversationId &&
    topicId == target.topicId && message != null && message.id == target.messageId && !message.deleted &&
    message.kind in setOf("image", "video", "file", "voice", "circle")

internal fun groupAttachmentSuggestedName(name: String): String = name.trim()
    .substringAfterLast('/')
    .substringAfterLast('\\')
    .map { if (it.isLetterOrDigit() || it in "._- ()[]") it else '_' }
    .joinToString("")
    .trim(' ', '.')
    .take(120)
    .trimEnd(' ', '.')
    .ifBlank { "attachment" }
