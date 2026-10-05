package ru.bgtu_voenmeh.zapara.ui.inbox

import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import ru.bgtu_voenmeh.zapara.data.social.SocialFailure

internal fun personalMediaMissingOnServer(error: Exception): Boolean =
    error is SocialFailure && error.status == 404

internal fun personalMediaFailureState(state: InboxUiState, conversationId: String,
    message: SocialMessage, error: Exception): InboxUiState {
    if (!personalMediaRequestIsCurrent(state, conversationId, message)) return state
    val attachment = message.attachmentId ?: return state
    return state.copy(mediaLoading = state.mediaLoading - attachment,
        mediaErrors = state.mediaErrors + attachment,
        mediaNotFound = if (personalMediaMissingOnServer(error)) state.mediaNotFound + attachment
            else state.mediaNotFound - attachment)
}

/** A media request may publish only while its exact message remains in the active chat history. */
internal fun personalMediaRequestIsCurrent(
    state: InboxUiState,
    conversationId: String,
    message: SocialMessage
): Boolean = state.active?.id == conversationId && state.messages.any {
    it.id == message.id && it.attachmentId == message.attachmentId && it.kind == message.kind && !it.deleted
}
