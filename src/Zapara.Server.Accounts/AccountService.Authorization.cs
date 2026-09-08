using Npgsql;

namespace Zapara.Server.Accounts;

public sealed partial class AccountService
{
    // Internal, trusted module boundary for later same-database writes. The callback must use this
    // connection/transaction, never commit or retain them, and must not perform external effects.
    // No caller-supplied actor or stale authentication result is accepted as authorization.
    internal Task<T> ExecuteMutationAsync<T>(string accessToken,
        Func<AccountMutationContext, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        var hash = AccountTokens.Hash(accessToken, "za_");
        return DatabaseAsync(async db =>
        {
            await using var tx = await db.BeginAsync();
            var (user, family) = await db.AuthorizeAsync(hash);
            var result = await operation(new(db, user, family, tx), ct);
            await db.CommitAsync(tx);
            return result;
        }, ct);
    }
}

internal sealed class AccountMutationContext(AccountRepository repository, AccountRow user, FamilyRow family, NpgsqlTransaction transaction)
{
    internal AccountRepository Repository => repository;
    internal AccountRow User => user;
    internal FamilyRow Family => family;
    internal NpgsqlConnection Connection => repository.Connection;
    internal NpgsqlTransaction Transaction => transaction;
}
