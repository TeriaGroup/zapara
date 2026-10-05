using Zapara.Contracts.Sync;

namespace Vograph.Core.Services.Sync;

public enum PrivateSyncState
{
    Success, Conflict, ResetRequired, ManifestExpired, NeedsReauthentication,
    RateLimited, Unavailable, InvalidRequest, InvalidResponse, Cancelled, TimedOut
}

/// <summary>No raw response, credentials or diagnostic exceptions escape this boundary.</summary>
public sealed class PrivateSyncResult<T> where T : class
{
    public PrivateSyncState State { get; }
    public T? Value { get; }
    public SyncMutationResult? MutationOutcome { get; }
    public TimeSpan? RetryAfter { get; }
    public string Diagnostic => State switch
    {
        PrivateSyncState.Success => "Запрос синхронизации выполнен.",
        PrivateSyncState.Conflict => "Состояние записи не совпало.",
        PrivateSyncState.ResetRequired => "Требуется повторная синхронизация.",
        PrivateSyncState.ManifestExpired => "Снимок синхронизации устарел.",
        PrivateSyncState.NeedsReauthentication => "Требуется повторный вход.",
        PrivateSyncState.RateLimited => "Слишком много запросов.",
        PrivateSyncState.TimedOut => "Превышено время ожидания.",
        PrivateSyncState.Cancelled => "Операция отменена.",
        PrivateSyncState.InvalidRequest => "Некорректный запрос синхронизации.",
        PrivateSyncState.InvalidResponse => "Некорректный ответ синхронизации.",
        _ => "Синхронизация временно недоступна."
    };
    internal PrivateSyncResult(PrivateSyncState state, T? value = null,
        SyncMutationResult? mutationOutcome = null, TimeSpan? retryAfter = null)
        => (State, Value, MutationOutcome, RetryAfter) = (state, value, mutationOutcome, retryAfter);
    public override string ToString() => "PrivateSyncResult { [REDACTED] }";
}
