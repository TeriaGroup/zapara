using System.Diagnostics;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class SyncCliTests
{
    [Fact]
    public async Task Compiled_operator_baseline_rerun_and_redacted_failure()
    {
        await using var db = await SyncPostgresFixture.CreateAsync();
        var success = await Run(db, false, "sync", "db-migrate");
        Assert.Equal(0, success.ExitCode);
        Assert.Contains("sync.db-migrate", success.Output);
        Assert.Contains("committed", success.Output);
        Assert.Equal(1L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.schema_migrations"));
        Assert.Equal(0, (await Run(db, false, "sync", "db-migrate")).ExitCode);
        Assert.Equal(2, (await Run(db, false, "sync", "db-migrate", "extra")).ExitCode);
        var failure = await Run(db, true, "sync", "db-migrate");
        Assert.Equal(5, failure.ExitCode);
        Assert.Equal("{\"operation\":\"sync.db-migrate\",\"status\":\"failed\"}", failure.Output.Trim());
        Assert.DoesNotContain("CANARY", failure.Output);
        Console.WriteLine("CLI sync baseline=0 rerun=0 bad_args=2 invalid_config=5 redacted=true");
    }

    // The invoking test shell holds Local\ZaparaServerBuildGate for the entire process tree.
    private static async Task<(int ExitCode, string Output)> Run(SyncPostgresFixture db, bool invalid, params string[] arguments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var dll = Path.Combine(root, "src", "Zapara.AdminCli", "bin", "Debug", "net8.0", "Zapara.AdminCli.dll");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET8") ?? throw new InvalidOperationException("Private runtime required"))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add(dll);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("Accounts__", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("Sync__", StringComparison.OrdinalIgnoreCase) || k.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["Accounts__Enabled"] = "false";
        start.Environment["Sync__Enabled"] = "false";
        start.Environment["Accounts__Schema"] = db.Accounts.Schema;
        start.Environment["Sync__Schema"] = db.Schema;
        start.Environment["ConnectionStrings__Accounts"] = invalid ? "CANARY_SECRET_INVALID" : Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("CLI did not start");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return (process.ExitCode, await output + await errors);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
    }
}
