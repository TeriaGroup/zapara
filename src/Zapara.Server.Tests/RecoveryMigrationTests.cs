using Npgsql;
using Xunit;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

public sealed class RecoveryMigrationTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Default_target_installs_v4_and_keeps_recovery_tables()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        Assert.Equal(4, await db.ScalarAsync<int>($"SELECT max(version) FROM {db.QuotedSchema}.schema_migrations"));
        Assert.Equal(3L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}' AND tablename IN ('recovery_addresses','recovery_email_tokens','password_reset_tokens')"));
        Assert.Equal(AccountsMigrations.RecoveryChecksum, await db.ScalarAsync<string>($"SELECT checksum FROM {db.QuotedSchema}.schema_migrations WHERE version=3"));
        Assert.Equal(AccountsSchemaShape.LifecycleFingerprint, await db.FingerprintAsync());
        output.WriteLine($"SQL recovery checksum={AccountsMigrations.RecoveryChecksum} shape={AccountsSchemaShape.LifecycleFingerprint}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Supported_schemas_upgrade_to_v3_and_preserve_users_sessions_and_oauth(int from)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, from);
        var s = db.QuotedSchema;
        var user = Guid.NewGuid();
        var family = Guid.NewGuid();
        var hash = new string('a', 64);
        await db.ExecuteAsync($"INSERT INTO {s}.users VALUES ('{user}','rec.user','rec.user',NULL,now(),'active',1)");
        await db.ExecuteAsync($"INSERT INTO {s}.password_credentials(user_id,password_hash,changed_at) VALUES ('{user}','hash-placeholder',now())");
        await db.ExecuteAsync($"INSERT INTO {s}.session_families(family_id,user_id,device_id,device_name,platform,created_at,authenticated_at,last_seen_at,expires_at) VALUES ('{family}','{user}','{Guid.NewGuid()}','test','windows',now(),now(),now(),now()+interval '30 days')");
        await db.ExecuteAsync($"INSERT INTO {s}.access_tokens VALUES (decode('{hash}','hex'),'{family}',now(),now()+interval '1 minute')");
        if (from >= 2)
            await db.ExecuteAsync($"INSERT INTO {s}.external_identities VALUES ('{user}','yandex','subject-1',now())");
        var users = await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.users");
        var sessions = await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.session_families");
        await db.Migrations.EnsureAsync(Ct, 3);
        Assert.Equal(3, await db.ScalarAsync<int>($"SELECT max(version) FROM {s}.schema_migrations"));
        Assert.Equal(users, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.users"));
        Assert.Equal(sessions, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.session_families"));
        if (from >= 2)
            Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.external_identities"));
        Assert.Equal(AccountsSchemaShape.RecoveryFingerprint, await db.FingerprintAsync());
        await db.Migrations.EnsureAsync(Ct, 3);
        Assert.Equal(AccountsSchemaShape.RecoveryFingerprint, await db.FingerprintAsync());
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("future")]
    [InlineData("partial")]
    public async Task V3_checksum_future_and_partial_are_rejected_without_repair(string scenario)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, 3);
        var s = db.QuotedSchema;
        var sql = scenario switch
        {
            "checksum" => $"UPDATE {s}.schema_migrations SET checksum='changed' WHERE version=3",
            "future" => $"INSERT INTO {s}.schema_migrations VALUES (4,'future',CURRENT_TIMESTAMP)",
            "partial" => $"DROP TABLE {s}.recovery_addresses",
            _ => throw new InvalidOperationException()
        };
        await db.ExecuteAsync(sql);
        var before = await db.FingerprintAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(before, await db.FingerprintAsync());
        if (scenario == "checksum")
            Assert.Equal("changed", await db.ScalarAsync<string>($"SELECT checksum FROM {s}.schema_migrations WHERE version=3"));
        if (scenario == "future")
            Assert.Equal(4, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.schema_migrations"));
        if (scenario == "partial")
            Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_tables WHERE schemaname='{db.Schema}' AND tablename='recovery_addresses'"));
    }

    [Fact]
    public async Task Recovery_email_is_not_a_login_identity_and_tokens_are_hashed()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, 3);
        var s = db.QuotedSchema;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await db.ExecuteAsync($"INSERT INTO {s}.users VALUES ('{first}','rec.one','rec.one',NULL,now(),'active',1)");
        await db.ExecuteAsync($"INSERT INTO {s}.users VALUES ('{second}','rec.two','rec.two',NULL,now(),'active',1)");
        await db.ExecuteAsync($"INSERT INTO {s}.recovery_addresses VALUES ('{first}','shared@example.invalid',now())");
        await db.ExecuteAsync($"INSERT INTO {s}.recovery_addresses VALUES ('{second}','shared@example.invalid',now())");
        Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.recovery_addresses WHERE email='shared@example.invalid'"));
        var hash = new string('a', 64);
        var other = new string('b', 64);
        await db.ExecuteAsync($"INSERT INTO {s}.recovery_email_tokens VALUES (decode('{hash}','hex'),'{first}','pending@example.invalid',now()+interval '30 minutes',NULL)");
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() =>
            db.ExecuteAsync($"INSERT INTO {s}.recovery_email_tokens VALUES (decode('aa','hex'),'{first}','pending@example.invalid',now()+interval '30 minutes',NULL)"))).SqlState);
        await db.ExecuteAsync($"INSERT INTO {s}.password_reset_tokens VALUES (decode('{hash}','hex'),'{first}',now()+interval '15 minutes',NULL)");
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (await Assert.ThrowsAsync<PostgresException>(() =>
            db.ExecuteAsync($"INSERT INTO {s}.password_reset_tokens VALUES (decode('{hash}','hex'),'{second}',now()+interval '15 minutes',NULL)"))).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() =>
            db.ExecuteAsync($"INSERT INTO {s}.account_security_events(event_id,action,outcome,created_at) VALUES ('{Guid.NewGuid()}','raw_password','success',now())"))).SqlState);
        await db.ExecuteAsync($"INSERT INTO {s}.account_security_events(event_id,user_id,action,outcome,created_at) VALUES ('{Guid.NewGuid()}','{first}','password_reset','success',now())");
        var family = Guid.NewGuid();
        await db.ExecuteAsync($"INSERT INTO {s}.session_families(family_id,user_id,device_id,device_name,platform,created_at,authenticated_at,last_seen_at,expires_at) VALUES ('{family}','{first}','{Guid.NewGuid()}','test','windows',now(),now(),now(),now()+interval '30 days')");
        await db.ExecuteAsync($"INSERT INTO {s}.reauth_proofs(proof_hash,user_id,family_id,purpose,security_version,provider_verified,expires_at) VALUES (decode('{other}','hex'),'{first}','{family}','set_recovery_email',1,false,now()+interval '5 minutes')");
    }
}
