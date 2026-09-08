using System.Diagnostics;
using Xunit;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class AdminCliTests
{
    [Fact]
    public async Task Bootstrap_is_operator_only_audited_and_refuses_repeat()
    {
        await using var db = await AdminPostgresFixture.CreateAsync();
        var user = await db.AccountService.RegisterAsync(new("admin.owner", Password), TestContext.Current.CancellationToken);
        var first = await Run(db, false, "admin", "bootstrap", "--user-id", user.UserId.ToString("D"));
        Assert.Equal(0, first.ExitCode);
        Assert.Contains("admin.bootstrap", first.Output);
        Assert.Contains("committed", first.Output);
        Assert.Contains(user.UserId.ToString("D"), first.Output);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.platform_admins"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.admin_audit WHERE action='bootstrap' AND outcome='success'"));
        var repeat = await Run(db, false, "admin", "bootstrap", "--user-id", user.UserId.ToString("D"));
        Assert.Equal(4, repeat.ExitCode);
        Assert.Equal("{\"operation\":\"admin.bootstrap\",\"status\":\"refused\"}", repeat.Output.Trim());
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.platform_admins"));
        var other = await db.AccountService.RegisterAsync(new("admin.other", Password), TestContext.Current.CancellationToken);
        var secondUser = await Run(db, false, "admin", "bootstrap", "--user-id", other.UserId.ToString("D"));
        Assert.Equal(4, secondUser.ExitCode);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.platform_admins"));
        Assert.Equal(2, (await Run(db, false, "admin", "bootstrap")).ExitCode);
        Assert.Equal(2, (await Run(db, false, "admin", "bootstrap", "--user-id", "not-a-uuid")).ExitCode);
        Assert.Equal(2, (await Run(db, false, "admin", "bootstrap", "--user-id", Guid.Empty.ToString("D"))).ExitCode);
        var failure = await Run(db, true, "admin", "bootstrap", "--user-id", user.UserId.ToString("D"));
        Assert.Equal(5, failure.ExitCode);
        Assert.Equal("{\"operation\":\"admin.bootstrap\",\"status\":\"failed\"}", failure.Output.Trim());
        Assert.DoesNotContain("CANARY", failure.Output);
        Assert.DoesNotContain(Password, first.Output);
        Console.WriteLine("CLI admin bootstrap=0 refuse=4 bad_args=2 invalid_config=5 redacted=true");
    }

    private static async Task<(int ExitCode, string Output)> Run(AdminPostgresFixture db, bool invalid, params string[] arguments)
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
            k.StartsWith("Communities__", StringComparison.OrdinalIgnoreCase) || k.StartsWith("Admin__", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("Sync__", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["Accounts__Enabled"] = "false";
        start.Environment["Communities__Enabled"] = "false";
        start.Environment["Admin__Enabled"] = "false";
        start.Environment["Accounts__Schema"] = db.Accounts.Schema;
        start.Environment["Communities__Schema"] = db.Communities.Schema;
        start.Environment["Admin__Schema"] = db.Schema;
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
