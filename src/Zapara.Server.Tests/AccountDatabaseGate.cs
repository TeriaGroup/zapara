using Npgsql;
using Xunit;

namespace Zapara.Server.Tests;

internal sealed class AccountDatabaseGate : IAsyncDisposable
{
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private AccountDatabaseGate(NpgsqlConnection connection, NpgsqlTransaction transaction)
        => (this.connection, this.transaction) = (connection, transaction);

    internal static async Task<AccountDatabaseGate> LockUser(AccountsPostgresFixture db, Guid userId)
    {
        var connection = db.DataSource.CreateConnection();
        try
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var result = new AccountDatabaseGate(connection, transaction);
            await using var command = new NpgsqlCommand($"SELECT 1 FROM {db.QuotedSchema}.users WHERE user_id=@id FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("id", userId);
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            return result;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    internal static async Task WaitFor(AccountsPostgresFixture db, int count, string waitType = "Lock")
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (await db.ScalarAsync<long>($"SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND query LIKE '%{db.Schema}%' AND wait_event_type='{waitType}'") < count)
            await Task.Delay(20, timeout.Token);
    }
    internal Task Commit() => transaction.CommitAsync(TestContext.Current.CancellationToken);
    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
