using Zapara.Contracts.Sync;

namespace Zapara.Server.Sync;

/// <summary>Domain-only union. Map the selected frozen DTO after the Accounts transaction commits.</summary>
public sealed class SyncReadResult<T> where T : class
{
    private SyncReadResult(T? value, SyncError? error) => (Value, Error) = (value, error);
    public T? Value { get; }
    public SyncError? Error { get; }
    internal static SyncReadResult<T> Success(T value) => new(value, null);
    internal static SyncReadResult<T> Failure(int status, string code) => new(null, new(status, code));
    public override string ToString() => "SyncReadResult { [REDACTED] }";
}
