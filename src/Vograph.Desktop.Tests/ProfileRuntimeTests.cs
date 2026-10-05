using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class ProfileRuntimeTests
{
    [Fact]
    public void Guest_database_and_preferences_keep_legacy_paths()
    {
        using var directory = new ProfileTestDirectory();
        using (var app = AppServices.Create(directory.Root, () => false))
        {
            var settings = app.Db.GetSettings();
            settings.MyGroupId = "guest-sentinel";
            app.Db.SaveSettings(settings);
            app.Prefs.Save();
            Assert.True(File.Exists(Path.Combine(directory.Root, "vograph.db")));
            Assert.True(File.Exists(Path.Combine(directory.Root, "ui.json")));
        }
        using var reopened = AppServices.Create(directory.Root, () => false);
        Assert.Equal("guest-sentinel", reopened.Settings.MyGroupId);
    }

    [Theory]
    [InlineData("ProfileDescriptor")]
    [InlineData("ProfileWorkLifetime")]
    [InlineData("ProfileSwitchCoordinator")]
    public void Runtime_exposes_the_required_non_UI_contract(string name)
    {
        Assert.NotNull(typeof(AppServices).Assembly.GetType("Vograph.Desktop.Services.Profiles." + name));
    }
}
