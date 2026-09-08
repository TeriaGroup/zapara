using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class IngestCliTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Compiled_db_init_idempotent_and_file_summary()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, initialize: false, ct: Ct);
        foreach (var unused in Enumerable.Range(0, 2))
        {
            var init = await ExecuteAsync(db.Schema, ["db-init"]);
            Assert.Equal(0, init.Code);
            Assert.Empty(init.Error);
            Assert.Equal("success", JsonDocument.Parse(init.Output).RootElement.GetProperty("outcome").GetString());
        }
        Assert.Null((await db.Store.ReadSelectionAsync(ct: Ct)).Refresh.LastAttemptId);
        var result = await ExecuteAsync(db.Schema, ["ingest", "--file", PostgresFixture.FixturePath("valid-a.xml")]);
        Assert.Equal(0, result.Code);
        Assert.Empty(result.Error);
        using var summary = JsonDocument.Parse(result.Output);
        var root = summary.RootElement;
        Assert.Equal(2, root.GetProperty("counts").GetProperty("groups").GetInt32());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("lessons").GetInt32());
        var current = Assert.IsType<Zapara.Server.Timetable.SnapshotRead>(await db.Store.ReadCurrentAsync(Ct));
        Assert.Equal(current.Meta.SnapshotId, root.GetProperty("snapshotId").GetGuid());
        Assert.Equal(current.Refresh.LastAttemptId, root.GetProperty("attemptId").GetGuid());
        var invalid = await ExecuteAsync(db.Schema, ["ingest", "--file", PostgresFixture.FixturePath("invalid.xml")]);
        Assert.Equal(2, invalid.Code);
        Assert.Contains("snapshot_malformed", invalid.Error);
        Assert.DoesNotContain("invalid.xml", invalid.Error + invalid.Output);
        await db.ReceiptAsync(Ct);
    }

    [Theory]
    [InlineData("")]
    [InlineData("db-init poison-secret-sentinel")]
    [InlineData("ingest --file")]
    [InlineData("ingest --fetch poison-secret-sentinel")]
    [InlineData("ingest --url poison-secret-sentinel")]
    [InlineData("ingest --file --fetch")]
    [InlineData("ingest --file a --fetch")]
    [InlineData("ingest")]
    [InlineData("poison-secret-sentinel")]
    public async Task Compiled_invalid_args_before_configuration(string text)
    {
        var args = text.Length == 0 ? Array.Empty<string>() : text.Split(' ');
        var result = await ExecuteAsync(null, args);
        Assert.Equal(2, result.Code);
        Assert.Contains("invalid_arguments", result.Error);
        Assert.DoesNotContain("poison-secret-sentinel", result.Error + result.Output);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task Compiled_busy_and_missing_schema_are_3_and_5()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await using (var lease = await db.Store.TryAcquireAsync(Ct))
        {
            var result = await ExecuteAsync(db.Schema, ["ingest", "--file", "missing-secret-sentinel"]);
            Assert.Equal(3, result.Code);
            Assert.Contains("busy", result.Error);
            Assert.DoesNotContain("missing-secret-sentinel", result.Error + result.Output);
        }
        var absent = "tt_missing_" + Guid.NewGuid().ToString("N");
        var missing = await ExecuteAsync(absent, ["db-init"]);
        Assert.Equal(5, missing.Code);
        Assert.Contains("db_unavailable", missing.Error);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM pg_namespace WHERE nspname='{absent}'", Ct));
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task Compiled_unknown_commit_warns_inspection_and_never_retries()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var a = await ExecuteAsync(db.Schema, ["ingest", "--file", PostgresFixture.FixturePath("valid-a.xml")]);
        Assert.Equal(0, a.Code);
        var before = (await db.Store.ReadCurrentAsync(Ct))!.Meta.SnapshotId;
        // Only the backend invoking this fixture-owned deferred trigger can terminate itself.
        await db.ExecuteAsync($"""
            CREATE FUNCTION {db.QuotedSchema}.ingest_terminate_self() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN PERFORM pg_terminate_backend(pg_backend_pid()); RETURN NEW; END $$;
            CREATE CONSTRAINT TRIGGER ingest_terminate_self AFTER INSERT ON {db.QuotedSchema}.snapshots
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION {db.QuotedSchema}.ingest_terminate_self();
            """, Ct);
        var result = await ExecuteAsync(db.Schema, ["ingest", "--file", PostgresFixture.FixturePath("valid-b.xml")]);
        Assert.Equal(5, result.Code);
        Assert.Contains("publication_unknown", result.Error);
        Assert.Contains("Проверьте попытку", result.Error);
        using var summary = JsonDocument.Parse(result.Output);
        var root = summary.RootElement;
        Assert.Equal("unknown", root.GetProperty("outcome").GetString());
        var attempt = root.GetProperty("attemptId").GetGuid();
        Assert.Equal("running", await db.ScalarAsync<string>($"SELECT status FROM {db.QuotedSchema}.refresh_attempts WHERE attempt_id='{attempt}'", Ct));
        Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_attempts", Ct));
        Assert.Equal(before, (await db.Store.ReadCurrentAsync(Ct))!.Meta.SnapshotId);
        Assert.DoesNotContain("valid-b.xml", result.Output + result.Error);
        await db.ReceiptAsync(Ct);
    }

    private async Task<(int Code, string Output, string Error)> ExecuteAsync(string? schema, string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT_X64")!, "dotnet.exe"))
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        // TestServer's shared-framework graph omits CLI-private assemblies from the test output.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var cli = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Zapara.Ingest", "bin", configuration, "net8.0", "Zapara.Ingest.dll"));
        Assert.True(File.Exists(cli));
        start.ArgumentList.Add(cli);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (schema is null)
        {
            start.Environment.Remove("ConnectionStrings__Timetable");
            start.Environment.Remove("Timetable__Schema");
        }
        else
        {
            start.Environment["ConnectionStrings__Timetable"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES");
            start.Environment["Timetable__Schema"] = schema;
        }
        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var result = (process.ExitCode, await stdout, await stderr);
            output.WriteLine($"COMPILED_CLI exit={result.ExitCode} stdout={result.Item2} stderr={result.Item3}");
            return result;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }
}
