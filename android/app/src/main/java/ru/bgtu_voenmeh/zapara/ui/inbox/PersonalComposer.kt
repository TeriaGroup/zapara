package ru.bgtu_voenmeh.zapara.ui.inbox

import ru.bgtu_voenmeh.zapara.data.communities.CommunityValidation
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage

internal const val PERSONAL_MESSAGE_LIMIT = 2000

data class PersonalComposer(
    val text: String = "",
    val reply: SocialMessage? = null,
    val editing: SocialMessage? = null,
    private val ordinary: PersonalComposer? = null,
    val revision: Long = 0,
    val error: String? = null
) {
    val sendText: String get() = text.replace("\r\n", "\n").trim()
    val messageLength: Int get() = sendText.let { it.codePointCount(0, it.length) }
    val canSend: Boolean get() = runCatching { CommunityValidation.message(sendText) }.isSuccess

    fun type(value: String) = if (text == value) this else copy(text = value, revision = revision + 1, error = null)
    fun replyTo(message: SocialMessage): PersonalComposer =
        (ordinary ?: this).copy(reply = message, editing = null, ordinary = null, revision = revision + 1, error = null)
    fun edit(message: SocialMessage) = copy(text = message.body.orEmpty(), reply = null, editing = message,
        ordinary = ordinary ?: this, revision = revision + 1, error = null)
    fun cancel(): PersonalComposer = (ordinary ?: copy(reply = null)).copy(
        editing = null, ordinary = null, revision = revision + 1, error = null)
    fun acknowledge(sent: PersonalComposer): PersonalComposer = if (revision != sent.revision) this
        else if (editing != null) cancel() else copy(text = "", reply = null, revision = revision + 1, error = null)
    fun acknowledgeAttachment(sent: PersonalComposer): PersonalComposer =
        if (revision == sent.revision) copy(reply = null, revision = revision + 1, error = null) else this
}

/** Owned by one profile's InboxViewModel; never persisted or shared across accounts. */
internal class PersonalComposerSessions {
    private val drafts = mutableMapOf<String, PersonalComposer>()
    fun save(conversationId: String, composer: PersonalComposer) { drafts[conversationId] = composer }
    fun restore(conversationId: String) = drafts[conversationId] ?: PersonalComposer()
    fun clear() = drafts.clear()
}

/** A read started before an acknowledged mutation must never overwrite that mutation. */
internal class PersonalHistoryVersions {
    private val versions = java.util.concurrent.ConcurrentHashMap<String, Long>()
    fun current(conversationId: String): Long = versions[conversationId] ?: 0L
    fun acknowledge(conversationId: String) { versions.merge(conversationId, 1L) { previous, next -> previous + next } }
    fun isCurrent(conversationId: String, started: Long) = current(conversationId) == started
}
