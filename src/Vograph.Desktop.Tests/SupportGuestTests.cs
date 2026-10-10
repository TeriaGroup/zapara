using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Features.Communities;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>R2-02: «Поддержка» не должна быть тупиком для гостя, когда вход недоступен.</summary>
public class SupportGuestTests : UiTest
{
    private static readonly DateTime Mon = new(2026, 10, 12, 8, 0, 0);

    [Fact]
    public void Guest_text_offers_sign_in_only_when_it_works_and_never_promises_a_dead_form()
    {
        Assert.Contains("войдите в аккаунт", SupportGate.GuestText(signInWorks: true));
        var down = SupportGate.GuestText(signInWorks: false);
        Assert.Contains("временно недоступен", down);
        Assert.DoesNotContain("войдите", down);
        Assert.Contains("по ссылке ниже", SupportGate.GuestText(signInWorks: false, hasFallback: true));
        Assert.Null(SupportGate.FallbackUrl); // продуктовое решение: другого канала пока нет — кнопку не показываем
        Assert.False(SupportGate.HasFallback);
    }

    [AvaloniaFact]
    public async Task Guest_with_sign_in_unavailable_sees_an_explanation_instead_of_the_form()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services) { Clock = () => Mon };
        shell.Register(SectionKey.Schedule, () => new ScheduleViewModel(db.Services, shell, () => Mon));
        shell.Register(SectionKey.Community, () => new CommunitiesViewModel(db.Services));
        shell.Register(SectionKey.Settings, () => new SettingsViewModel(db.Services, shell, () => Mon));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        SetTheme(ThemeVariant.Light, db.Services.Theme);
        shell.NavigateTo(SectionKey.Settings);
        Pump(); await Task.Delay(200); Pump();
        var vm = (SettingsViewModel)window.GetVisualDescendants().OfType<SettingsView>().First().DataContext!;
        vm.OpenPanelCommand.Execute("help");
        Pump(); await Task.Delay(200); Pump();

        Assert.True(vm.AccountPanel.IsGuest);
        Assert.False(vm.AccountPanel.SignInWorks);
        bool Shown(string id) => window.GetVisualDescendants().OfType<Control>()
            .Any(c => AutomationProperties.GetAutomationId(c) == id && c.IsEffectivelyVisible);
        Assert.True(Shown("Settings.SupportGuest"));
        Assert.False(Shown("Settings.ReportSubject"));   // форму, которую нельзя отправить, не показываем
        Assert.False(Shown("Settings.Report"));
        Assert.False(Shown("Settings.SupportSignIn"));   // «Войти» — только когда вход работает
        Assert.False(Shown("Settings.SupportFallback")); // канала нет — кнопки нет
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToArray();
        Assert.Contains(texts, t => t.Contains("вход сейчас временно недоступен"));
        Assert.DoesNotContain(texts, t => t.Contains("войдите в аккаунт"));
        window.Close();
    }
}
