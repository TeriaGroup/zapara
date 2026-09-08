using System.Diagnostics;
using Npgsql;
using Xunit;
using Zapara.Server.Accounts;

namespace Zapara.Server.Tests;

public sealed class AccountsMigrationTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FreshBaselineAndRerun()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true, targetVersion: 1);
        Assert.Equal(7, await db.ScalarAsync<long>($"SELECT count(*) FROM information_schema.tables WHERE table_schema='{db.Schema}'"));
        Assert.Equal("aa5f311445805d2e8ca0f0c6c2b06ff6cbf37041f0026a44541a96657e50e50b", AccountsMigrations.BaselineChecksum);
        Assert.Equal(AccountsMigrations.BaselineChecksum, await db.ScalarAsync<string>($"SELECT checksum FROM {db.QuotedSchema}.schema_migrations"));
        var fingerprint = await db.FingerprintAsync();
        output.WriteLine($"SQL baseline checksum={AccountsMigrations.BaselineChecksum} shape={fingerprint}");
        var before = await db.ScalarAsync<string>($"SELECT row_to_json(m)::text FROM {db.QuotedSchema}.schema_migrations m");
        await db.Migrations.EnsureAsync(Ct, targetVersion: 1);
        Assert.Equal(before, await db.ScalarAsync<string>($"SELECT row_to_json(m)::text FROM {db.QuotedSchema}.schema_migrations m"));
        Assert.Equal(fingerprint, await db.FingerprintAsync());
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("partial")]
    [InlineData("checksum")]
    [InlineData("future")]
    [InlineData("missing_table")]
    [InlineData("constraint")]
    [InlineData("index")]
    [InlineData("column")]
    public async Task InvalidSchemaIsNeverRepaired(string scenario)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, scenario is not ("foreign" or "partial"), targetVersion: 1);
        var sql = scenario switch
        {
            "foreign" => $"CREATE TABLE {db.QuotedSchema}.foreign_data(value text)",
            "partial" => $"CREATE TABLE {db.QuotedSchema}.users(user_id uuid)",
            "checksum" => $"UPDATE {db.QuotedSchema}.schema_migrations SET checksum='changed'",
            "future" => $"INSERT INTO {db.QuotedSchema}.schema_migrations VALUES (2,'future',CURRENT_TIMESTAMP)",
            "missing_table" => $"DROP TABLE {db.QuotedSchema}.account_security_events",
            "constraint" => $"ALTER TABLE {db.QuotedSchema}.access_tokens DROP CONSTRAINT access_tokens_token_hash_check",
            "index" => $"DROP INDEX {db.QuotedSchema}.refresh_active_family",
            "column" => $"ALTER TABLE {db.QuotedSchema}.users ADD COLUMN unexpected text",
            _ => throw new InvalidOperationException()
        };
        await db.ExecuteAsync(sql);
        var before = await db.FingerprintAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
        Assert.Equal(before, await db.FingerprintAsync());
        if (scenario == "checksum") Assert.Equal("changed", await db.ScalarAsync<string>($"SELECT checksum FROM {db.QuotedSchema}.schema_migrations"));
        if (scenario == "future") Assert.Equal(2, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.schema_migrations"));
        output.WriteLine($"SQL rejection={scenario} no_mutation=true");
    }

    [Fact]
    public async Task MissingSchemaIsNotCreated()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine);
        await db.ExecuteAsync($"DROP SCHEMA {db.QuotedSchema}");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Migrations.EnsureAsync(Ct));
            Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{db.Schema}'"));
        }
        finally { await db.ExecuteAsync($"CREATE SCHEMA {db.QuotedSchema}"); }
    }

    [Fact]
    public async Task ConcurrentInitializationCommitsOneBaseline()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => db.Migrations.EnsureAsync(Ct, targetVersion: 1)));
        Assert.Equal(1, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.schema_migrations"));
    }

    [Fact]
    public async Task TimetableSchemaAndRowsRemainUnchanged()
    {
        await using var timetable = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var before = await ApiTestFactory.DatabaseStateAsync(timetable);
        async Task<string> TimetableShapeAsync()
        {
            await using var connection = timetable.Configuration.CreateDedicatedConnection();
            await connection.OpenAsync(Ct);
            await using var tx = await connection.BeginTransactionAsync(Ct);
            return await AccountsSchemaShape.FingerprintAsync(connection, tx, timetable.Schema, Ct);
        }
        var shapeBefore = await TimetableShapeAsync();
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await db.Migrations.EnsureAsync(Ct);
        await timetable.Store.EnsureSchemaAsync(Ct);
        Assert.Equal(before, await ApiTestFactory.DatabaseStateAsync(timetable));
        Assert.Equal(shapeBefore, await TimetableShapeAsync());
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM information_schema.tables WHERE table_schema='{timetable.Schema}' AND table_name='users'"));
        output.WriteLine($"SQL timetable exact verifier and row fingerprint unchanged=true shape={shapeBefore}");
    }

    [Fact]
    public async Task ConstraintsEnforceCanonicalIdentityAndCurrentTokens()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        var s = db.QuotedSchema;
        var user = Guid.NewGuid();
        var family = Guid.NewGuid();
        await db.ExecuteAsync($"INSERT INTO {s}.users VALUES ('{user}','.Abc','.abc',NULL,now(),'active',1)");
        await db.ExecuteAsync($"INSERT INTO {s}.password_credentials(user_id,password_hash,changed_at) VALUES ('{user}','hash-placeholder',now())");
        await db.ExecuteAsync($"INSERT INTO {s}.session_families(family_id,user_id,device_id,device_name,platform,created_at,authenticated_at,last_seen_at,expires_at) VALUES ('{family}','{user}','{Guid.NewGuid()}','test','windows',now(),now(),now(),now()+interval '30 days')");
        foreach (var statement in new[]
        {
            $"INSERT INTO {s}.users VALUES ('{Guid.NewGuid()}','ABC','ABC',NULL,now(),'active',1)",
            $"UPDATE {s}.users SET credential_version=0",
            $"UPDATE {s}.users SET status='admin'",
            $"UPDATE {s}.session_families SET platform='ios'",
            $"INSERT INTO {s}.access_tokens VALUES (decode('aa','hex'),'{family}',now(),now()+interval '1 minute')",
            $"INSERT INTO {s}.account_security_events(event_id,action,outcome,created_at) VALUES ('{Guid.NewGuid()}','raw_password','success',now())"
        })
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(statement))).SqlState);
        var hash = new string('a', 64);
        var second = new string('b', 64);
        await db.ExecuteAsync($"INSERT INTO {s}.access_tokens VALUES (decode('{hash}','hex'),'{family}',now(),now()+interval '1 minute')");
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync($"INSERT INTO {s}.access_tokens VALUES (decode('{second}','hex'),'{family}',now(),now()+interval '1 minute')"))).SqlState);
        await db.ExecuteAsync($"INSERT INTO {s}.refresh_tokens(token_hash,family_id,created_at,expires_at) VALUES (decode('{hash}','hex'),'{family}',now(),now()+interval '1 day')");
        var insertRefresh = $"INSERT INTO {s}.refresh_tokens(token_hash,family_id,created_at,expires_at) VALUES (decode('{second}','hex'),'{family}',now(),now()+interval '1 day')";
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(insertRefresh))).SqlState);
        await db.ExecuteAsync($"UPDATE {s}.refresh_tokens SET consumed_at=now()");
        await db.ExecuteAsync(insertRefresh);
        Assert.Equal(2, await db.ScalarAsync<long>($"SELECT count(*) FROM {s}.refresh_tokens"));
        output.WriteLine("SQL constraints current_access=1 retained_refresh=2 active_refresh=1");
    }

    [Fact]
    public async Task CompiledCliMigratesAndReportsSafeExitCodes()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine);
        var success = await RunCliAsync(db, false, "accounts", "db-migrate");
        Assert.Equal(0, success.ExitCode);
        Assert.Contains("committed", success.Output);
        Assert.Equal(4, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.schema_migrations"));
        Assert.Equal(0, (await RunCliAsync(db, false, "accounts", "db-migrate")).ExitCode);
        Assert.Equal(2, (await RunCliAsync(db, true, "accounts", "bad-command")).ExitCode);
        var unavailable = await RunCliAsync(db, true, "accounts", "db-migrate");
        Assert.Equal(5, unavailable.ExitCode);
        Assert.DoesNotContain("CANARY", unavailable.Output);
        output.WriteLine($"CLI migrate exit={success.ExitCode} SQL latest=4 invalid_args=2 unavailable=5");
    }

    private static async Task<(int ExitCode, string Output)> RunCliAsync(AccountsPostgresFixture db, bool missing, params string[] args)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var dll = Path.Combine(root, "src", "Zapara.AdminCli", "bin", "Debug", "net8.0", "Zapara.AdminCli.dll");
        Assert.True(File.Exists(dll));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET8") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("Accounts__", StringComparison.OrdinalIgnoreCase) || k.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["Accounts__Schema"] = db.Schema;
        start.Environment["Accounts__Enabled"] = "false";
        start.Environment["ConnectionStrings__Accounts"] = missing ? "CANARY" : Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("CLI did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        try { await process.WaitForExitAsync(Ct); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
        return (process.ExitCode, await stdout + await stderr);
    }
}
