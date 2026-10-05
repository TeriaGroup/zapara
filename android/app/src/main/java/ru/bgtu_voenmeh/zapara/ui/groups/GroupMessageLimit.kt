package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.CommunityValidation

internal enum class GroupMessageProblem { Empty, TooLong, Invalid }

internal data class GroupMessagePreview(val body: String, val scalars: Int,
    val problem: GroupMessageProblem?) {
    val ready: Boolean get() = problem == null
}

internal fun groupMessagePreview(draft: String, context: String?, editing: Boolean): GroupMessagePreview {
    val plain = draft.replace("\r\n", "\n")
    val body = if (context != null && !editing) "$context\n\n$plain" else plain
    val normalized = body.replace("\r\n", "\n")
    val count = normalized.codePointCount(0, normalized.length)
    val problem = when {
        plain.isBlank() -> GroupMessageProblem.Empty
        count > 2000 -> GroupMessageProblem.TooLong
        runCatching { CommunityValidation.message(normalized) }.isFailure -> GroupMessageProblem.Invalid
        else -> null
    }
    return GroupMessagePreview(normalized, count, problem)
}
