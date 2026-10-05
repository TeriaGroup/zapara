using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class AccountUiTests
{
    [Fact]
    public void Settings_exposes_installation_account_panel_and_real_view()
    {
        var assembly = typeof(AppServices).Assembly;
        Assert.NotNull(assembly.GetType("Vograph.Desktop.Features.Account.AccountPanelViewModel"));
        Assert.NotNull(assembly.GetType("Vograph.Desktop.Features.Account.AccountPanelView"));
        Assert.NotNull(typeof(Features.Preferences.SettingsViewModel).GetProperty("AccountPanel"));
    }

    [Fact]
    public void Legacy_English_database_does_not_change_application_language_or_stored_data()
    {
        using var directory = new ProfileTestDirectory();
        using (var initial = AppServices.Create(directory.Root, () => false))
        {
            var settings = initial.Db.GetSettings();
            settings.Language = "en";
            initial.Db.SaveSettings(settings);
        }
        using var reopened = AppServices.Create(directory.Root, () => false);
        Assert.Equal("ru", reopened.Loc.Language);
        Assert.Equal("en", reopened.Db.GetSettings().Language);
    }
}
