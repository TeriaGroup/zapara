package ru.bgtu_voenmeh.zapara.ui.media

internal enum class ChatMediaLoadState { NeedsTap, Loading, Retry, NotFound, Ready }

internal fun chatMediaLoadState(fileAvailable: Boolean, loading: Boolean, error: Boolean,
    notFound: Boolean = false): ChatMediaLoadState = when {
    fileAvailable -> ChatMediaLoadState.Ready
    notFound -> ChatMediaLoadState.NotFound
    error -> ChatMediaLoadState.Retry
    loading -> ChatMediaLoadState.Loading
    else -> ChatMediaLoadState.NeedsTap
}
