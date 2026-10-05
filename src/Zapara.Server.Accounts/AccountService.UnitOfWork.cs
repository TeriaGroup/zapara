using Npgsql;

namespace Zapara.Server.Accounts;

/// <summary>Trusted server modules only. Never expose the callback as a client SQL endpoint.</summary>
public interface IAccountUnitOfWork
{
    Task<T> ExecuteAsync<T>(string accessToken,
        Func<TrustedAccountContext, CancellationToken, Task<T>> operation, CancellationToken ct = default);
}

public sealed partial class AccountService : IAccountUnitOfWork
{
    public Task<T> ExecuteAsync<T>(string accessToken,
        Func<TrustedAccountContext, CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteMutationAsync(accessToken, async (locked, cancellation) =>
        {
            var context = new TrustedAccountContext(locked.User.User.UserId, locked.Family.Id,
                clock.GetUtcNow().ToUniversalTime(), locked.Connection, locked.Transaction);
            try { return await operation(context, cancellation); }
            finally { context.Invalidate(); }
        }, ct);
    }
}

/// <summary>
/// Callback-scoped capability. Use only this transaction; do not commit, retain handles,
/// open independent write transactions or perform network I/O. This is not a SQL sandbox.
/// </summary>
public sealed class TrustedAccountContext
{
    private readonly Guid userId;
    private readonly Guid familyId;
    private readonly DateTimeOffset utcNow;
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private bool active = true;
    internal TrustedAccountContext(Guid userId, Guid familyId, DateTimeOffset utcNow,
        NpgsqlConnection connection, NpgsqlTransaction transaction)
        => (this.userId, this.familyId, this.utcNow, this.connection, this.transaction) =
            (userId, familyId, utcNow, connection, transaction);
    public Guid UserId { get { Check(); return userId; } }
    public Guid FamilyId { get { Check(); return familyId; } }
    public DateTimeOffset UtcNow { get { Check(); return utcNow; } }
    public NpgsqlConnection Connection { get { Check(); return connection; } }
    public NpgsqlTransaction Transaction { get { Check(); return transaction; } }
    internal void Invalidate() => active = false;
    private void Check() => ObjectDisposedException.ThrowIf(!active, this);
}
