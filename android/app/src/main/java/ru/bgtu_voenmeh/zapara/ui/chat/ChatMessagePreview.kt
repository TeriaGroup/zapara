package ru.bgtu_voenmeh.zapara.ui.chat

import androidx.compose.runtime.Composable
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R

/** Avoid showing opaque attachment URLs in a reply or destructive-action preview. */
@Composable
internal fun chatMessagePreview(kind: String, body: String?, fileName: String? = null, deleted: Boolean = false): String =
    if (deleted) stringResource(R.string.face_message_deleted)
    else if (kind == "text") body.orEmpty().trim().ifBlank { stringResource(R.string.face_message) }
    else fileName?.takeIf { it.isNotBlank() } ?: stringResource(when (kind) {
        "image" -> R.string.chat_media_photo
        "voice" -> R.string.ux30_chat_preview_voice
        "circle" -> R.string.ux30_chat_preview_circle
        "video" -> R.string.group_video
        else -> R.string.face_attachment
    })
