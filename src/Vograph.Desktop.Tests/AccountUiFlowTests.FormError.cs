using System.Net;
using Xunit;
using Zapara.Contracts.Accounts;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

/// <summary>#148 (R3-02): ошибка формы входа не переживает смену режима и правку и не попадает в строку состояния.</summary>
public sealed partial class AccountUiFlowTests
{
    private static async Task<Fixture> RejectedLogin()
    {
        var f = new Fixture();
        await f.Vm.InitializeAsync();
        f.Handler.Send = (_, _) => Task.FromResult(Json(new AccountError("ignored", 401, "invalid_credentials"), HttpStatusCode.Unauthorized));
        f.Vm.Username = "Test.User"; f.Vm.Password = Password;
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.Contains("Неверный логин или пароль", f.Vm.FormError);
        Assert.True(f.Vm.HasFormError);
        // Строка состояния (и подзаголовок «Аккаунт» в настройках, который её повторяет) ошибку не показывает.
        Assert.DoesNotContain("Неверный", f.Vm.Status);
        Assert.Equal("Гостевой профиль: данные доступны без аккаунта и сети.", f.Vm.Status);
        return f;
    }

    [Fact]
    public async Task Bad_login_error_is_cleared_by_switching_to_registration()
    {
        await using var f = await RejectedLogin();
        f.Vm.ToggleRegistrationCommand.Execute(null);
        Assert.True(f.Vm.Registration);
        Assert.Equal("", f.Vm.FormError);
        Assert.False(f.Vm.HasFormError);
    }

    [Fact]
    public async Task Bad_login_error_is_cleared_by_editing_the_login_or_typing_a_password()
    {
        await using (var f = await RejectedLogin()) { f.Vm.Username = "Test.User2"; Assert.Equal("", f.Vm.FormError); }
        await using (var f = await RejectedLogin()) { f.Vm.Password = "x"; Assert.Equal("", f.Vm.FormError); }
    }

    [Fact]
    public async Task Registration_without_accepted_documents_reports_in_the_form_slot()
    {
        await using var f = new Fixture();
        await f.Vm.InitializeAsync();
        f.Vm.Registration = true;
        f.Vm.Username = "test.user"; f.Vm.Password = Password;
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.False(string.IsNullOrEmpty(f.Vm.FormError));
        Assert.Equal(f.Vm.FormError.Length > 0, f.Vm.HasFormError);
        f.Vm.Registration = false;
        Assert.Equal("", f.Vm.FormError);
    }

    [Fact]
    public void Form_error_is_a_danger_live_region_and_the_settings_subtitle_shows_only_the_status()
    {
        var root = FindRepoFile("src/Vograph.Desktop/Features/Account/AccountPanelView.axaml");
        var view = File.ReadAllText(root);
        var at = view.IndexOf("AutomationProperties.AutomationId=\"Account.FormError\"", StringComparison.Ordinal);
        Assert.True(at > 0, "Account.FormError");
        var block = view[view.LastIndexOf("<Border IsVisible=\"{Binding HasFormError}\"", at, StringComparison.Ordinal)..at];
        Assert.Contains("Brush.BadSoft", block);
        Assert.Contains("Icon.Alert", block);
        Assert.Contains("Classes=\"bad\" Text=\"{Binding FormError}\"", block);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", view[at..view.IndexOf("/>", at, StringComparison.Ordinal)]);
        var settings = File.ReadAllText(FindRepoFile("src/Vograph.Desktop/Features/Preferences/SettingsView.axaml"));
        Assert.Contains("Text=\"{Binding AccountPanel.Status}\"", settings);
        Assert.DoesNotContain("FormError", settings);
    }

    [Fact]
    public async Task Failed_reauthentication_of_a_signed_in_account_goes_to_the_form_not_the_status_line()
    {
        await using var f = new Fixture();
        await f.Login();
        var previous = f.Handler.Send;
        f.Handler.Send = (request, token) => request.RequestUri!.AbsolutePath == "/api/v1/account/me"
            ? Task.FromResult(Json(new AccountError("ignored", 401, "invalid_session"), HttpStatusCode.Unauthorized))
            : previous(request, token);
        await f.Vm.RefreshRemoteAsync();
        Assert.True(f.Vm.ShowLogin);
        Assert.False(f.Profiles.Snapshot.Profile.IsGuest);
        f.Handler.Send = (_, _) => Task.FromResult(Json(new AccountError("ignored", 401, "invalid_credentials"), HttpStatusCode.Unauthorized));
        f.Vm.Username = "Test.User"; f.Vm.Password = Password;
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.Contains("Неверный логин или пароль", f.Vm.FormError);
        Assert.DoesNotContain("Неверный", f.Vm.Status);
        Assert.False(string.IsNullOrWhiteSpace(f.Vm.Status));
        f.Vm.Username = "Test.User2";
        Assert.Equal("", f.Vm.FormError);
        Assert.DoesNotContain("Неверный", f.Vm.Status);
    }

    [Fact]
    public async Task Startup_vault_read_failure_still_shows_in_the_status_line()
    {
        await using var f = new Fixture();
        f.Vault.FailRead = true;
        await f.Vm.InitializeAsync();
        Assert.Equal("", f.Vm.FormError);
        Assert.NotEqual("Гостевой профиль: данные доступны без аккаунта и сети.", f.Vm.Status);
        Assert.Equal(Vograph.Desktop.Services.Loc.Current.T("accountFailed"), f.Vm.Status);
    }

    private static string FindRepoFile(string relative) => Path.Combine(ResourceKeysTests.RepoRoot(), relative);
}
