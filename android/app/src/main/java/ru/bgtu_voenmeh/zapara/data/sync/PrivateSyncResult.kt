package ru.bgtu_voenmeh.zapara.data.sync

import java.time.Duration

enum class PrivateSyncState {
    Success, Conflict, ResetRequired, ManifestExpired, NeedsReauthentication,
    RateLimited, Unavailable, InvalidRequest, InvalidResponse, Cancelled, TimedOut
}

class PrivateSyncResult<T>(
    val state: PrivateSyncState,
    val value: T? = null,
    val mutationOutcome: SyncMutationResult? = null,
    val retryAfter: Duration? = null
) {
    val diagnostic: String
        get() = when (state) {
            PrivateSyncState.Success -> "Запрос синхронизации выполнен."
            PrivateSyncState.Conflict -> "Состояние записи не совпало."
            PrivateSyncState.ResetRequired -> "Требуется повторная синхронизация."
            PrivateSyncState.ManifestExpired -> "Снимок синхронизации устарел."
            PrivateSyncState.NeedsReauthentication -> "Требуется повторный вход."
            PrivateSyncState.RateLimited -> "Слишком много запросов."
            PrivateSyncState.TimedOut -> "Превышено время ожидания."
            PrivateSyncState.Cancelled -> "Операция отменена."
            PrivateSyncState.InvalidRequest -> "Некорректный запрос синхронизации."
            PrivateSyncState.InvalidResponse -> "Некорректный ответ синхронизации."
            PrivateSyncState.Unavailable -> "Синхронизация временно недоступна."
        }

    override fun toString(): String = "PrivateSyncResult { [REDACTED] }"
}
