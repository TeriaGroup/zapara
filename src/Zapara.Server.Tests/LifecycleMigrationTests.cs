using Npgsql;
using Xunit;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

public sealed class LifecycleMigrationTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requested_v4_installs_export_jobs_deletion_jobs_and_manifests()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, 4);
        Assert.Equal(4, await db.ScalarAsync<int>($"SELECT max(version) FROM {db.QuotedSchema}.schema_migrations"));
        Assert.Equal(3L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}' AND tablename IN ('export_jobs','deletion_jobs','deletion_manifests')"));
        Assert.Equal(AccountsMigrations.LifecycleChecksum, await db.ScalarAsync<string>($"SELECT checksum FROM {db.QuotedSchema}.schema_migrations WHERE version=4"));
        Assert.Equal(AccountsSchemaShape.LifecycleFingerprint, await db.FingerprintAsync());
        output.WriteLine($"SQL lifecycle checksum={AccountsMigrations.LifecycleChecksum} shape={AccountsSchemaShape.LifecycleFingerprint}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Supported_schemas_upgrade_to_v4_and_preserve_users_recovery_and_sessions(int from)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, from);
        var s = db.QuotedSchema;
        var user = Guid.NewGuid();
        var family = Guid.NewGuid();
        var hash = new string('a', 64);
        await db.ExecuteAsync($"INSERT INTO {s}.users VALUES ('{user}','life.user','life.user',NULL,now(),'active',1)");
        await db.ExecuteAsync($"INSERT INTO {s}.password_credentials(user_id,password_hash,changed_at) VALUES ('{user}','hash-placeholder',now())");
        await db.ExecuteAsync($"INSERT INTO {s}.session_families(family_id,user_id,device_id,device_name,platform,created_at,authenticated_at,last_seen_at,expires_at) VALUES ('{family}','{user}','{Guid.NewGuid()}','test','windows',now(),now(),now(),now()+interval '30 days')");
        await db.ExecuteAsync($"INSERT INTO {s}.access_tokens VALUES (decode('{hash}','hex'),'{family}',now(),now()+interval '1 minute')");
        if (from >= 3)
            await db.ExecuteAsync($"INSERT INTO {s}.recovery_addresses VALUES ('{user}','life@example.invalid',now())");
        var users = await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.users");
        var sessions = await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.session_families");
        await db.Migrations.EnsureAsync(Ct, 4);
        Assert.Equal(4, await db.ScalarAsync<int>($"SELECT max(version) FROM {s}.schema_migrations"));
        Assert.Equal(users, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.users"));
        Assert.Equal(sessions, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.session_families"));
        if (from >= 3)
            Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.recovery_addresses"));
        Assert.Equal(AccountsSchemaShape.LifecycleFingerprint, await db.FingerprintAsync());
        await db.Migrations.EnsureAsync(Ct, 4);
        Assert.Equal(AccountsSchemaShape.LifecycleFingerprint, await db.FingerprintAsync());
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("future")]
    [InlineData("partial")]
    public async Task V4_checksum_future_and_partial_are_rejected_without_repair(string scenario)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, 4);
        var s = db.QuotedSchema;
        var sql = scenario switch
        {
            "checksum" => $"UPDATE {s}.schema_migrations SET checksum='changed' WHERE version=4",
            "future" => $"INSERT INTO {s}.schema_migrations VALUES (5,'future',CURRENT_TIMESTAMP)",
            "partial" => $"DROP TABLE {s}.deletion_manifests",
            _ => throw new InvalidOperationException()
        };
        await db.ExecuteAsync(sql);
        var before = await db.FingerprintAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct, 4));
        Assert.Equal(before, await db.FingerprintAsync());
        if (scenario == "checksum")
            Assert.Equal("changed", await db.ScalarAsync<string>($"SELECT checksum FROM {s}.schema_migrations WHERE version=4"));
        if (scenario == "future")
            Assert.Equal(5, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.schema_migrations"));
        if (scenario == "partial")
            Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}' AND tablename='deletion_manifests'"));
    }

    [Fact]
    public async Task Tombstone_rejects_insert_of_a_deleted_identity()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, 4);
        var s = db.QuotedSchema;
        var user = Guid.NewGuid();
        await db.ExecuteAsync($"INSERT INTO {s}.deletion_manifests VALUES ('{user}','gone.user',now())");
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.ExecuteAsync($"INSERT INTO {s}.users VALUES ('{user}','gone.user','gone.user',NULL,now(),'active',1)"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.users"));
    }

    [Fact]
    public async Task Default_ensure_installs_v4()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine);
        await db.Migrations.EnsureAsync(Ct);
        Assert.Equal(4, await db.ScalarAsync<int>($"SELECT max(version) FROM {db.QuotedSchema}.schema_migrations"));
        Assert.Equal(AccountsSchemaShape.LifecycleFingerprint, await db.FingerprintAsync());
        Assert.Equal(3L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}' AND tablename IN ('export_jobs','deletion_jobs','deletion_manifests')"));
    }
}
