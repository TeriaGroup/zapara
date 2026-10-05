using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
using Zapara.Server.Platform;

namespace Zapara.Server.Tests;

public sealed class PlatformReadyTests
{
    private static Task<string> Status(string value) => Task.FromResult(value);

    [Fact]
    public async Task Disabled_accounts_do_not_block_platform_when_timetable_present()
    {
        var ready = new PlatformReady(
            timetable: _ => Status(PlatformReady.Present),
            accounts: _ => Status(PlatformReady.Disabled),
            sync: _ => Status(PlatformReady.Disabled),
            communities: _ => Status(PlatformReady.Disabled),
            admin: _ => Status(PlatformReady.Disabled));

        var report = await ready.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(report.IsReady);
        Assert.Equal(PlatformReady.Present, report.Modules["timetable"]);
        Assert.Equal(PlatformReady.Disabled, report.Modules["accounts"]);
        Assert.Equal(PlatformReady.Disabled, report.Modules["sync"]);
        Assert.Equal(PlatformReady.Disabled, report.Modules["communities"]);
        Assert.Equal(PlatformReady.Disabled, report.Modules["admin"]);
        Assert.Equal(new[] { "timetable", "accounts", "sync", "communities", "admin" }, report.Modules.Keys);
    }

    [Fact]
    public async Task Missing_enabled_module_makes_platform_not_ready()
    {
        var ready = new PlatformReady(
            timetable: _ => Status(PlatformReady.Present),
            accounts: _ => Status(PlatformReady.Missing),
            sync: _ => Status(PlatformReady.Disabled),
            communities: _ => Status(PlatformReady.Disabled),
            admin: _ => Status(PlatformReady.Disabled));

        var report = await ready.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(report.IsReady);
        Assert.Equal(PlatformReady.Missing, report.Modules["accounts"]);
    }

    [Fact]
    public async Task Ready_result_lists_modules_without_secrets()
    {
        const string canary = "Host=evil;Password=SECRET_canary_dsn_value";
        var ready = new PlatformReady(
            timetable: _ => Status(PlatformReady.Present),
            accounts: _ => Status(PlatformReady.Disabled),
            sync: _ => Status(PlatformReady.Disabled),
            communities: _ => Status(PlatformReady.Disabled),
            admin: _ => Status(PlatformReady.Disabled));
        var report = await ready.CheckAsync(TestContext.Current.CancellationToken);
        var result = PlatformReady.ToResult(report);

        await using var stream = new MemoryStream();
        var context = new DefaultHttpContext { Response = { Body = stream } };
        await result.ExecuteAsync(context);
        stream.Position = 0;
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        var root = json.RootElement;
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        Assert.Equal("ready", root.GetProperty("status").GetString());
        var modules = root.GetProperty("modules");
        Assert.Equal(PlatformReady.Present, modules.GetProperty("timetable").GetString());
        Assert.Equal(PlatformReady.Disabled, modules.GetProperty("accounts").GetString());
        var text = root.GetRawText() + ready + report;
        Assert.DoesNotContain(canary, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET_canary", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_ready_result_is_russian_problem_json_with_modules()
    {
        var ready = new PlatformReady(
            timetable: _ => Status(PlatformReady.Missing),
            accounts: _ => Status(PlatformReady.Present),
            sync: _ => Status(PlatformReady.Present),
            communities: _ => Status(PlatformReady.Present),
            admin: _ => Status(PlatformReady.Present));
        var report = await ready.CheckAsync(TestContext.Current.CancellationToken);
        var result = PlatformReady.ToResult(report);

        await using var stream = new MemoryStream();
        var context = new DefaultHttpContext { Response = { Body = stream } };
        await result.ExecuteAsync(context);
        stream.Position = 0;
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        var root = json.RootElement;
        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(503, root.GetProperty("status").GetInt32());
        Assert.Equal("platform_not_ready", root.GetProperty("code").GetString());
        Assert.Matches("[А-Яа-я]", root.GetProperty("title").GetString()!);
        Assert.Equal(PlatformReady.Missing, root.GetProperty("modules").GetProperty("timetable").GetString());
        Assert.DoesNotContain("ConnectionString", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FromConfiguration_maps_disabled_flags_without_calling_probes()
    {
        var called = false;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "false",
            ["Sync:Enabled"] = "false",
            ["Communities:Enabled"] = "false",
            ["Admin:Enabled"] = "false"
        }).Build();

        Task<bool> Never(CancellationToken _)
        {
            called = true;
            return Task.FromResult(true);
        }

        var ready = PlatformReady.FromConfiguration(config,
            timetable: _ => Task.FromResult(true),
            accounts: Never,
            sync: Never,
            communities: Never,
            admin: Never);
        var report = await ready.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(report.IsReady);
        Assert.False(called);
        Assert.Equal(PlatformReady.Present, report.Modules["timetable"]);
        Assert.Equal(PlatformReady.Disabled, report.Modules["accounts"]);
        Assert.Equal(PlatformReady.Disabled, report.Modules["sync"]);
        Assert.Equal(PlatformReady.Disabled, report.Modules["communities"]);
        Assert.Equal(PlatformReady.Disabled, report.Modules["admin"]);
    }

    [Fact]
    public async Task FromConfiguration_enabled_false_probe_is_missing_and_true_is_present()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true",
            ["Sync:Enabled"] = "true",
            ["Communities:Enabled"] = "true",
            ["Admin:Enabled"] = "true"
        }).Build();

        var ready = PlatformReady.FromConfiguration(config,
            timetable: _ => Task.FromResult(false),
            accounts: _ => Task.FromResult(true),
            sync: _ => Task.FromResult(false),
            communities: _ => Task.FromResult(true),
            admin: _ => Task.FromResult(true));
        var report = await ready.CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(report.IsReady);
        Assert.Equal(PlatformReady.Missing, report.Modules["timetable"]);
        Assert.Equal(PlatformReady.Present, report.Modules["accounts"]);
        Assert.Equal(PlatformReady.Missing, report.Modules["sync"]);
        Assert.Equal(PlatformReady.Present, report.Modules["communities"]);
        Assert.Equal(PlatformReady.Present, report.Modules["admin"]);
    }

    [Fact]
    public void FromConfiguration_rejects_invalid_enablement_without_leaking_dsn()
    {
        const string canary = "Password=SECRET_canary_dsn_value";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "maybe",
            ["ConnectionStrings:Accounts"] = canary
        }).Build();

        var error = Assert.Throws<ArgumentException>(() => PlatformReady.FromConfiguration(config,
            timetable: _ => Task.FromResult(true),
            accounts: _ => Task.FromResult(true),
            sync: _ => Task.FromResult(true),
            communities: _ => Task.FromResult(true),
            admin: _ => Task.FromResult(true)));
        Assert.DoesNotContain("SECRET_canary", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(canary, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_probe_status_is_rejected()
    {
        var ready = new PlatformReady(
            timetable: _ => Status("ok"),
            accounts: _ => Status(PlatformReady.Disabled),
            sync: _ => Status(PlatformReady.Disabled),
            communities: _ => Status(PlatformReady.Disabled),
            admin: _ => Status(PlatformReady.Disabled));
        await Assert.ThrowsAsync<ArgumentException>(() => ready.CheckAsync(TestContext.Current.CancellationToken));
    }
}
