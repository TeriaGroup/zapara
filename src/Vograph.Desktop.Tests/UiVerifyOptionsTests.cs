using Vograph.Desktop.UiVerify;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class UiVerifyOptionsTests
{
    [Fact]
    public void Accessibility_is_a_distinct_bounded_account_mode()
    {
        Assert.NotEqual(Options.Parse(Arguments), Options.Parse([.. Arguments, "--account-accessibility"]));
    }

    [Theory]
    [InlineData("--registration-disabled")]
    [InlineData("--minimum-keyboard")]
    public void Accessibility_rejects_mixed_modes(string other)
    {
        Assert.Throws<ArgumentException>(() => Options.Parse([.. Arguments, "--account-accessibility", other]));
    }

    [Fact]
    public void Accessibility_requires_account_endpoint()
    {
        var error = Assert.Throws<ArgumentException>(() => Options.Parse(["--account-accessibility"]));
        Assert.Contains("requires --account-api", error.Message);
    }

    private static string[] Arguments => ["--exe", typeof(UiVerifyOptionsTests).Assembly.Location,
        "--out", Path.GetTempPath(), "--account-api", "http://127.0.0.1:5193"];

    [Fact]
    public void Minimum_keyboard_is_opt_in_for_online_logout_only()
    {
        var baseline = Options.Parse([.. Arguments, "--logout", "online"]);
        Assert.NotEqual(baseline, Options.Parse([.. Arguments, "--logout", "online", "--minimum-keyboard"]));
        Assert.Equal(baseline, Options.Parse([.. Arguments, "--logout", "online"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("offline")]
    public void Minimum_keyboard_rejects_other_scenarios(string mode)
    {
        string[] args = mode == "" ? Arguments : [.. Arguments, "--logout", mode];
        var error = Assert.Throws<ArgumentException>(() => Options.Parse([.. args, "--minimum-keyboard"]));
        Assert.Contains("requires --logout online", error.Message);
    }

    [Theory]
    [InlineData("online")]
    [InlineData("offline")]
    public void Logout_is_distinct_and_cannot_mix_with_registration_disabled(string mode)
    {
        var selected = Options.Parse([.. Arguments, "--logout", mode]);
        Assert.NotEqual(Options.Parse(Arguments), selected);
        Assert.NotEqual(Options.Parse([.. Arguments, "--logout", mode == "online" ? "offline" : "online"]), selected);
        var error = Assert.Throws<ArgumentException>(() => Options.Parse([.. Arguments, "--logout", mode, "--registration-disabled"]));
        Assert.Contains("cannot combine", error.Message);
    }

    [Theory]
    [InlineData("online")]
    [InlineData("offline")]
    public void Logout_requires_owned_account_endpoint(string mode)
    {
        var error = Assert.Throws<ArgumentException>(() => Options.Parse(["--logout", mode]));
        Assert.Contains("requires --account-api", error.Message);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("")]
    public void Logout_rejects_unknown_mode(string mode)
    {
        var error = Assert.Throws<ArgumentException>(() => Options.Parse([.. Arguments, "--logout", mode]));
        Assert.Contains("online or offline", error.Message);
    }

    [Fact]
    public void Disabled_registration_is_an_explicit_distinct_account_scenario()
    {
        var normal = Options.Parse(Arguments);
        var disabled = Options.Parse([.. Arguments, "--registration-disabled"]);
        Assert.Equal(normal.AccountApi, disabled.AccountApi);
        Assert.NotEqual(normal, disabled);
        Assert.Equal(normal, Options.Parse(Arguments));
    }

    [Fact]
    public void Disabled_registration_requires_account_api()
    {
        var error = Assert.Throws<ArgumentException>(() => Options.Parse(
            ["--exe", typeof(UiVerifyOptionsTests).Assembly.Location, "--registration-disabled"]));
        Assert.Contains("requires --account-api", error.Message);
    }

    [Theory]
    [InlineData("http://localhost:5193")]
    [InlineData("https://example.invalid")]
    public void Disabled_registration_preserves_loopback_guard(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => Options.Parse(
            ["--exe", typeof(UiVerifyOptionsTests).Assembly.Location,
                "--account-api", endpoint, "--registration-disabled"]));
    }
}
