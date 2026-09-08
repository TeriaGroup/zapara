using Xunit;

namespace Zapara.Server.Tests;

public sealed class OAuthMigrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Catalog_receipt_for_additive_migration()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine);
        await db.Migrations.EnsureAsync(TestContext.Current.CancellationToken, targetVersion: 1);
        await db.ExecuteAsync(Zapara.Server.Accounts.AccountsMigrations.MigrationSql(2).Replace("__SCHEMA__", db.QuotedSchema));
        output.WriteLine("OAUTH CATALOG " + await db.FingerprintAsync());
        Assert.Equal(Zapara.Server.Accounts.AccountsSchemaShape.ExternalFingerprint, await db.FingerprintAsync());
    }

    [Fact]
    public async Task Latest_accounts_migration_installs_external_login_tables()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, true);
        Assert.Equal(4, await db.ScalarAsync<int>($"SELECT max(version) FROM {db.QuotedSchema}.schema_migrations"));
        Assert.Equal(3L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}' AND tablename IN ('external_identities','oauth_transactions','reauth_proofs')"));
    }
}
