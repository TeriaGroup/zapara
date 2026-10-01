package ru.bgtu_voenmeh.zapara.ui.media

internal enum class ChatMediaLoadState { NeedsTap, Loading, Retry, Ready }

internal fun chatMediaLoadState(fileAvailable: Boolean, loading: Boolean, error: Boolean): ChatMediaLoadState = when {
    fileAvailable -> ChatMediaLoadState.Ready
    error -> ChatMediaLoadState.Retry
    loading -> ChatMediaLoadState.Loading
    else -> ChatMediaLoadState.NeedsTap
}
