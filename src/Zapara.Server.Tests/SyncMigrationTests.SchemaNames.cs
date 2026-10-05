using Xunit;
using Zapara.Server.Sync;

namespace Zapara.Server.Tests;

public sealed partial class SyncMigrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Overlapping_schema_names_initialize_reread_and_reject_drift(bool accountsContainsSync)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(accountsContainsSync: accountsContainsSync);
        TestContext.Current.TestOutputHelper!.WriteLine($"OWNED accounts={db.Accounts.Schema} sync={db.Schema}");
        var accountsBefore = await State(db.Accounts, db.Accounts.Schema);
        Assert.Null(await Record.ExceptionAsync(() => db.Migrations.EnsureAsync(Ct)));
        await using (var connection = db.Accounts.DataSource.CreateConnection())
        {
            await connection.OpenAsync(Ct);
            await using var tx = await connection.BeginTransactionAsync(Ct);
            Assert.Equal(SyncSchemaShape.Expected,
                await SyncSchemaShape.FingerprintAsync(connection, tx, db.Configuration, Ct));
        }
        var initialized = await State(db.Accounts, db.Schema);
        await db.Migrations.EnsureAsync(Ct);
        Assert.Equal(initialized, await State(db.Accounts, db.Schema));
        Assert.Equal(accountsBefore, await State(db.Accounts, db.Accounts.Schema));
        await db.Accounts.Migrations.EnsureAsync(Ct);
        Assert.Equal(SyncMigrations.BaselineChecksum,
            await db.Accounts.ScalarAsync<string>($"SELECT checksum FROM {db.QuotedSchema}.schema_migrations"));

        await db.Accounts.ExecuteAsync($"ALTER TABLE {db.QuotedSchema}.sync_state DROP CONSTRAINT sync_state_sequence_check; ALTER TABLE {db.QuotedSchema}.sync_state ADD CONSTRAINT sync_state_sequence_check CHECK (sequence >= -1)");
        var altered = await State(db.Accounts, db.Schema);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(altered, await State(db.Accounts, db.Schema));
        Assert.Equal(accountsBefore, await State(db.Accounts, db.Accounts.Schema));
    }
}
