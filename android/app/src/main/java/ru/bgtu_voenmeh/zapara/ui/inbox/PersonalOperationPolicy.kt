package ru.bgtu_voenmeh.zapara.ui.inbox

import ru.bgtu_voenmeh.zapara.data.social.SocialMessage

/** Refreshes are version guarded; they must not consume a user's send/recording action. */
internal fun canStartPersonalOperation(composing: Boolean, refreshing: Boolean, sending: Boolean): Boolean =
    if (composing) !sending else !refreshing

/** A late refresh still contributes missing history, while newer local receipts win collisions. */
internal fun mergePersonalHistory(current: List<SocialMessage>, incoming: List<SocialMessage>, refreshIsCurrent: Boolean): List<SocialMessage> {
    val preferred = (if (refreshIsCurrent) current + incoming else incoming + current).associateBy { it.id }
    val deleted = (current + incoming).filter { it.deleted }.associateBy { it.id }
    val read = (current + incoming).filter { it.read }.mapTo(hashSetOf()) { it.id }
    return preferred.values.map { row -> (deleted[row.id] ?: row).let { if (it.id in read) it.copy(read = true) else it } }
        .sortedBy { it.createdAt }
}

internal fun personalReceipt(current: SocialMessage?, incoming: SocialMessage, editing: Boolean = false, reactionOnly: Boolean = false): SocialMessage = when {
    current == null -> incoming
    current.deleted -> current
    incoming.deleted -> incoming
    reactionOnly -> current.copy(reactions = incoming.reactions, read = current.read || incoming.read)
    editing -> incoming.copy(reactions = current.reactions, read = current.read || incoming.read)
    else -> incoming.copy(read = current.read || incoming.read)
}
