using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Admin;
using Zapara.Server.Communities;
using Zapara.Server.Sync;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public sealed class RestoreSchemaTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task Every_historical_account_schema_survives_real_pg_dump_and_restore(int version)
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var sourceConfig = AccountsConfiguration.FromConfiguration(fixture.Config(false), true);
        await using var source = sourceConfig.CreateDataSource();
        await new AccountsMigrations(source, sourceConfig).EnsureAsync(Ct, version);
        await fixture.RestoreAsync();
        await using var connection = await fixture.OpenAsync(true);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        await new NpgsqlCommand("SET LOCAL search_path=pg_catalog", connection, tx).ExecuteNonQueryAsync(Ct);
        await AccountsMigrations.VerifyPreparedSchemaAsync(connection, tx, "acc", Ct);
        var restored = await AccountsSchemaShape.FingerprintAsync(connection, tx, "acc", Ct);
        output.WriteLine($"RESTORE accounts version={version} fingerprint={restored}");
        Assert.Equal(version, await fixture.ScalarAsync<int>(true, "SELECT max(version) FROM acc.schema_migrations"));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task Communities_sync_admin_and_timetable_survive_real_restore(int communityVersion)
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var config = fixture.Config(false);
        var accounts = AccountsConfiguration.FromConfiguration(config, true);
        await using var source = accounts.CreateDataSource();
        await new AccountsMigrations(source, accounts).EnsureAsync(Ct);
        var communities = CommunitiesConfiguration.FromConfiguration(config, true);
        await new CommunitiesMigrations(source, communities).EnsureAsync(Ct, communityVersion);
        var sync = SyncConfiguration.FromConfiguration(config, true);
        await new SyncMigrations(source, sync).EnsureAsync(Ct);
        var admin = AdminConfiguration.FromConfiguration(config, true);
        await new AdminMigrations(source, admin).EnsureAsync(Ct);
        var timetable = TimetableConfiguration.FromConfiguration(config);
        await using var timetableSource = timetable.CreateDataSource();
        var store = new SnapshotStore(timetableSource, "tt", TimeProvider.System, timetable.CreateDedicatedConnection);
        await store.EnsureSchemaAsync(Ct);
        Assert.Equal(0, await new IngestService(store, new TimetableInput(TimeProvider.System)).IngestFileAsync(PostgresFixture.FixturePath("valid-a.xml"), Ct));
        await fixture.RestoreAsync();
        var restoredConfig = fixture.Config(true);
        await using var connection = await fixture.OpenAsync(true);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        await new NpgsqlCommand("SET LOCAL search_path=pg_catalog", connection, tx).ExecuteNonQueryAsync(Ct);
        var communityHash = await CommunitiesSchemaShape.FingerprintAsync(connection, tx, CommunitiesConfiguration.FromConfiguration(restoredConfig), Ct);
        var syncHash = await SyncSchemaShape.FingerprintAsync(connection, tx, SyncConfiguration.FromConfiguration(restoredConfig), Ct);
        var adminHash = await AdminSchemaShape.FingerprintAsync(connection, tx, AdminConfiguration.FromConfiguration(restoredConfig), Ct);
        output.WriteLine($"RESTORE communities={communityVersion}:{communityHash}; sync={syncHash}; admin={adminHash}");
        Assert.Multiple(
            () => Assert.Equal(communityVersion == 1 ? CommunitiesSchemaShape.BaselineExpected : CommunitiesSchemaShape.Expected, communityHash),
            () => Assert.Equal(SyncSchemaShape.Expected, syncHash),
            () => Assert.Equal(AdminSchemaShape.Expected, adminHash));
        var restoredTimetable = TimetableConfiguration.FromConfiguration(restoredConfig);
        await using var restoredTimetableSource = restoredTimetable.CreateDataSource();
        Assert.True(await new SnapshotStore(restoredTimetableSource, "tt", TimeProvider.System, restoredTimetable.CreateDedicatedConnection).IsReadyAsync(Ct));
    }

    [Theory]
    [InlineData("length")]
    [InlineData("precedence")]
    [InlineData("literal")]
    [InlineData("not")]
    public async Task Restored_schema_still_rejects_real_constraint_changes(string kind)
    {
        await using var fixture = await RestoreFixture.CreateAsync();
        var config = AccountsConfiguration.FromConfiguration(fixture.Config(false), true);
        await using var source = config.CreateDataSource();
        await new AccountsMigrations(source, config).EnsureAsync(Ct);
        await fixture.RestoreAsync();
        var expression = kind switch
        {
            "length" => "display_name IS NULL OR (char_length(display_name) BETWEEN 1 AND 800 AND display_name !~ '[[:cntrl:]]')",
            "precedence" => "(display_name IS NULL OR char_length(display_name) >= 1) AND char_length(display_name) <= 80 AND display_name !~ '[[:cntrl:]]'",
            "literal" => "display_name IS NULL OR (char_length(display_name) BETWEEN 1 AND 80 AND display_name !~ '[(AND OR NOT)]')",
            _ => "display_name IS NULL OR NOT (char_length(display_name) BETWEEN 1 AND 80 AND display_name !~ '[[:cntrl:]]')"
        };
        // The restored database is exclusively owned by this fixture, with a verified pre-change dump.
        await fixture.ExecuteAsync(true, $"ALTER TABLE acc.users DROP CONSTRAINT users_display_name_check; ALTER TABLE acc.users ADD CONSTRAINT users_display_name_check CHECK ({expression})");
        await using var connection = await fixture.OpenAsync(true);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        await new NpgsqlCommand("SET LOCAL search_path=pg_catalog", connection, tx).ExecuteNonQueryAsync(Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AccountsMigrations.VerifyPreparedSchemaAsync(connection, tx, "acc", Ct));
    }
}

internal sealed class RestoreFixture : IAsyncDisposable
{
    private readonly string container;
    private readonly string databaseUser;
    private readonly string sourceName = "restore_src_" + Guid.NewGuid().ToString("N");
    private readonly string cloneName = "restore_dst_" + Guid.NewGuid().ToString("N");
    private readonly HashSet<string> created = [];
    private readonly string control;
    private readonly string artifacts;
    private RestoreFixture()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES"));
        if (builder.Host != "127.0.0.1" || builder.Port != 56432 || builder.Database != "zapara_test") throw new InvalidOperationException("Only the approved local fixture is allowed.");
        databaseUser = builder.Username ?? throw new InvalidOperationException("Fixture PostgreSQL username required.");
        builder.Database = "postgres"; builder.Pooling = false; builder.IncludeErrorDetail = false;
        control = builder.ConnectionString;
        artifacts = Environment.GetEnvironmentVariable("ZAPARA_RESTORE_TEST_DIRECTORY") ?? throw new InvalidOperationException("Set ZAPARA_RESTORE_TEST_DIRECTORY to the session scratchpad before restore tests.");
        container = Environment.GetEnvironmentVariable("ZAPARA_RESTORE_TEST_CONTAINER") ?? throw new InvalidOperationException("Set ZAPARA_RESTORE_TEST_CONTAINER to the PostgreSQL fixture's owned Docker container.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(container, "^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$")) throw new InvalidOperationException("Invalid fixture container name.");
        if (!Path.IsPathFullyQualified(artifacts)) throw new InvalidOperationException("Absolute restore artifact directory required.");
        Directory.CreateDirectory(artifacts);
    }
    internal static async Task<RestoreFixture> CreateAsync()
    {
        var fixture = new RestoreFixture();
        await using var admin = new NpgsqlConnection(fixture.control);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        try
        {
            foreach (var name in new[] { fixture.sourceName, fixture.cloneName })
            {
                await new NpgsqlCommand($"CREATE DATABASE {name} TEMPLATE template0", admin).ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
                fixture.created.Add(name);
            }
            await fixture.ExecuteAsync(false, "CREATE SCHEMA acc; CREATE SCHEMA com; CREATE SCHEMA syn; CREATE SCHEMA adm; CREATE SCHEMA tt");
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
    private string Dsn(bool clone) => new NpgsqlConnectionStringBuilder(control) { Database = clone ? cloneName : sourceName }.ConnectionString;
    internal IConfiguration Config(bool clone) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Accounts:Enabled"]="true", ["Accounts:Schema"]="acc", ["ConnectionStrings:Accounts"]=Dsn(clone),
        ["Communities:Enabled"]="true", ["Communities:Schema"]="com", ["Sync:Enabled"]="true", ["Sync:Schema"]="syn",
        ["Admin:Enabled"]="true", ["Admin:Schema"]="adm", ["Timetable:Schema"]="tt", ["ConnectionStrings:Timetable"]=Dsn(clone)
    }).Build();
    internal async Task<NpgsqlConnection> OpenAsync(bool clone)
    {
        var connection = new NpgsqlConnection(Dsn(clone));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }
    internal async Task ExecuteAsync(bool clone, string sql)
    { await using var connection = await OpenAsync(clone); await new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync(TestContext.Current.CancellationToken); }
    internal async Task<T> ScalarAsync<T>(bool clone, string sql)
    { await using var connection = await OpenAsync(clone); return (T)(await new NpgsqlCommand(sql, connection).ExecuteScalarAsync(TestContext.Current.CancellationToken))!; }
    internal async Task RestoreAsync()
    {
        var backup = await DumpAsync(sourceName);
        await DockerAsync(["exec", "-i", container, "pg_restore", "-U", databaseUser, "-d", cloneName, "--exit-on-error", "--single-transaction"], backup);
    }
    private async Task<string> DumpAsync(string database)
    {
        if (!created.Contains(database)) throw new InvalidOperationException("Unowned database backup target.");
        var path = Path.Combine(artifacts, database + ".dump");
        await DockerAsync(["exec", container, "pg_dump", "-U", databaseUser, "-d", database, "--format=custom"], output: path);
        if (new FileInfo(path).Length < 512) throw new InvalidOperationException("Empty backup.");
        await DockerAsync(["exec", "-i", container, "pg_restore", "--list"], path);
        return path;
    }
    private static async Task DockerAsync(string[] args, string? input = null, string? output = null)
    {
        using var process = new Process { StartInfo = new("docker") { UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=input is not null, RedirectStandardOutput=true, RedirectStandardError=true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var error = process.StandardError.ReadToEndAsync();
        Task read;
        await using var destination = output is null ? null : File.Create(output);
        read = destination is null ? process.StandardOutput.BaseStream.CopyToAsync(Stream.Null) : process.StandardOutput.BaseStream.CopyToAsync(destination);
        if (input is not null)
        { await using var file = File.OpenRead(input); await file.CopyToAsync(process.StandardInput.BaseStream); process.StandardInput.Close(); }
        await process.WaitForExitAsync(); await read; await error;
        if (process.ExitCode != 0) throw new InvalidOperationException("Owned fixture dump/restore command failed; archive retained.");
    }
    public async ValueTask DisposeAsync()
    {
        // Never target a caller-supplied DB or terminate another owner's connections.
        await using var admin = new NpgsqlConnection(control); await admin.OpenAsync();
        foreach (var name in created)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^restore_(src|dst)_[0-9a-f]{32}$")) throw new InvalidOperationException("Invalid teardown target.");
            await DumpAsync(name);
            await new NpgsqlCommand($"DROP DATABASE {name}", admin).ExecuteNonQueryAsync();
        }
    }
}
