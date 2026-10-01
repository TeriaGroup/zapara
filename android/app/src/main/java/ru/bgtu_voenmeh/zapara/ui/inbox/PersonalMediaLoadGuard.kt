package ru.bgtu_voenmeh.zapara.ui.inbox

import ru.bgtu_voenmeh.zapara.data.social.SocialMessage

/** A media request may publish only while its exact message remains in the active chat history. */
internal fun personalMediaRequestIsCurrent(
    state: InboxUiState,
    conversationId: String,
    message: SocialMessage
): Boolean = state.active?.id == conversationId && state.messages.any {
    it.id == message.id && it.attachmentId == message.attachmentId && it.kind == message.kind && !it.deleted
}
