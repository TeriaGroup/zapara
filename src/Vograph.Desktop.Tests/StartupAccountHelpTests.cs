using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Features.Account;
using Vograph.Desktop.Features.Chat;
using Vograph.Desktop.Features.Communities;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#18: ошибка запуска (D-01), состояние «вход недоступен» (D-02), тексты помощи и обновлений (D-08).</summary>
public class StartupAccountHelpTests : UiTest
{
    private const string Unavailable = "Вход временно недоступен. Расписание, карты и домашка работают без аккаунта.";

    [Theory]
    [InlineData("SQLite Error 5: 'database is locked'.", StartupErrorKind.AlreadyRunning)]
    [InlineData("System.IO.IOException: The process cannot access the file 'vograph.db' because it is being used by another process.", StartupErrorKind.AlreadyRunning)]
    [InlineData("SQLite Error 14: 'unable to open database file'.", StartupErrorKind.NoAccess)]
    [InlineData("System.UnauthorizedAccessException: Access to the path 'C:\\Users\\x\\AppData\\Local\\Vograph' is denied.", StartupErrorKind.NoAccess)]
    [InlineData("SQLite Error 13: 'database or disk is full'.", StartupErrorKind.DiskFull)]
    [InlineData("SQLite Error 11: 'database disk image is malformed'.", StartupErrorKind.Corrupt)]
    [InlineData("SQLite Error 26: 'file is not a database'.", StartupErrorKind.Corrupt)]
    [InlineData("System.IO.IOException: The file '/home/x/build/Vograph' already exists.", StartupErrorKind.DataPathIsFile)]
    [InlineData("System.InvalidOperationException: boom", StartupErrorKind.Unknown)]
    public void Known_startup_errors_get_plain_russian_text(string details, StartupErrorKind kind)
    {
        var error = new StartupError("raw", "/data", "/data/logs/startup-error.log", details);
        Assert.Equal(kind, error.Kind);
        Assert.DoesNotMatch("[A-Za-z]{4,}", error.Title.Replace("VOGRAPH_DATA_DIR", ""));
        Assert.DoesNotContain("SQLite", error.Explanation);
        Assert.EndsWith(".", error.Explanation);
        if (kind == StartupErrorKind.AlreadyRunning)
            Assert.Equal("Похоже, приложение уже запущено или прошлое обновление не завершилось. Закройте другие окна «Военмеха» и нажмите «Повторить».", error.Explanation);
    }

    [Fact]
    public void Report_contains_version_original_error_and_log_but_no_other_paths()
    {
        var error = StartupError.From(new InvalidOperationException("SQLite Error 5: 'database is locked'."), "/data", "/data/logs/startup-error.log");
        var report = StartupErrorCatalog.Report(error, "2.1.42", new DateTimeOffset(2026, 10, 9, 19, 4, 0, TimeSpan.FromHours(3)));
        Assert.Contains("Военмех 2.1.42 — ошибка запуска", report);
        Assert.Contains("Ошибка: System.InvalidOperationException: SQLite Error 5: 'database is locked'.", report);
        Assert.Contains("Журнал: /data/logs/startup-error.log", report);
        Assert.Contains("Время: 2026-10-09 19:04:00 +03:00", report);
    }

    [AvaloniaFact]
    public void Startup_error_window_retry_is_primary_details_are_collapsed_and_report_is_copied()
    {
        var window = new StartupErrorWindow { DataContext = StartupError.From(new Exception("SQLite Error 5: 'database is locked'."), "/data", "/data/logs/startup-error.log") };
        string? copied = null;
        var attempts = new List<int>();
        window.CopyText = text => { copied = text; return Task.CompletedTask; };
        window.Retry = attempt => { attempts.Add(attempt); return attempt == 1 ? StartupError.From(new Exception("SQLite Error 5: 'database is locked'."), "/data", "/log") : null; };
        window.Show();
        Pump();
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "startup-error-light");
        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        var retry = Assert.Single(buttons, b => AutomationProperties.GetAutomationId(b) == "StartupError.Retry");
        Assert.True(retry.Classes.Contains("primary"));
        Assert.Equal("Повторить", retry.Content);
        Assert.Contains(buttons, b => Equals(b.Content, "Скопировать отчёт"));
        Assert.Contains(buttons, b => Equals(b.Content, "Закрыть"));
        Assert.Same(retry, buttons.Last(b => b.Parent is StackPanel { Orientation: Avalonia.Layout.Orientation.Horizontal }));
        var details = window.GetVisualDescendants().OfType<Expander>().Single();
        Assert.False(details.IsExpanded);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("database is locked") == true);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Военмех уже запущен?");

        window.OnCopyReport(null, new Avalonia.Interactivity.RoutedEventArgs());
        Pump();
        Assert.Contains("database is locked", copied);

        window.OnRetry(null, new Avalonia.Interactivity.RoutedEventArgs());
        Pump();
        Assert.Equal([1], attempts);
        var again = Assert.IsType<StartupError>(window.DataContext);
        Assert.Equal(1, again.Attempt);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Повторная попытка не помогла. Ошибка та же.");
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.OnRetry(null, new Avalonia.Interactivity.RoutedEventArgs());
        Pump();
        Assert.Equal([1, 2], attempts);
        Assert.True(closed);
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task Sign_in_unavailable_hides_the_form_and_dead_end_buttons_in_communities_and_chats()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var panel = db.Services.Shared.AccountPanel;
        Assert.Equal("Проверяем, доступен ли вход…", panel.Status);
        await panel.InitializeAsync();
        Assert.Equal(Unavailable, panel.Status);
        Assert.False(panel.SignInWorks);

        var shell = new ShellViewModel(db.Services);
        shell.Register(SectionKey.Settings, () => new SettingsViewModel(db.Services, shell, () => new DateTime(2026, 10, 9, 19, 4, 0)));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        SetTheme(ThemeVariant.Light);

        shell.NavigateTo(SectionKey.Settings); Pump();
        ((SettingsViewModel)shell.Current!).ActivePanel = "account"; Pump();
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible && AutomationProperties.GetAutomationId(box) == "Account.Username");
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) == "Account.Login");
        Frames.Capture(window, "account-unavailable-light");

        shell.NavigateTo(SectionKey.Community); await shell.Current!.ActivateAsync(); Pump();
        var communities = Assert.IsType<CommunitiesViewModel>(shell.Current);
        Assert.True(communities.NeedAccount);
        Assert.False(communities.ShowSignIn);
        Assert.Equal(Unavailable, communities.NeedAccountHint);
        AssertNoDeadEnd(window, "Community.SignIn");

        shell.NavigateTo(SectionKey.Chat); await shell.Current!.ActivateAsync(); Pump();
        var chats = Assert.IsType<ChatInboxViewModel>(shell.Current);
        Assert.True(chats.NeedAccount);
        Assert.False(chats.ShowSignIn);
        Assert.Equal(Unavailable, chats.NeedAccountHint);
        AssertNoDeadEnd(window, "Chat.SignIn");
        AssertNoBindingErrors();
    }

    private static void AssertNoDeadEnd(Window window, string signInId)
    {
        var visible = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.DoesNotContain(visible, b => AutomationProperties.GetAutomationId(b) == signInId);
        Assert.DoesNotContain(visible, b => Equals(b.Content, "Открыть настройки аккаунта"));
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Недоступно без входа");
    }

    [Fact]
    public void Sign_in_works_only_after_ready_with_a_service_and_capabilities()
    {
        using var vm = new AccountPanelViewModel();
        Assert.False(vm.SignInWorks);
        Assert.False(vm.ShowLoginForm);
    }

    [AvaloniaFact]
    public async Task Help_and_updates_use_plain_words_and_a_real_download_button()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        shell.Register(SectionKey.Settings, () => new SettingsViewModel(db.Services, shell, () => new DateTime(2026, 10, 9, 19, 4, 0)));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        shell.NavigateTo(SectionKey.Settings); Pump();
        ((SettingsViewModel)shell.Current!).ActivePanel = "help"; Pump();
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "help-updates-light");
        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.Contains(buttons, b => Equals(b.Content, "Стабильная"));
        Assert.Contains(buttons, b => Equals(b.Content, "Тестовая (нужен код доступа)"));
        var download = Assert.Single(buttons, b => AutomationProperties.GetAutomationId(b) == "Updates.Releases");
        Assert.Equal("Открыть страницу загрузки", download.Content);
        Assert.False(download.Classes.Contains("link"));
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Assert.Contains(texts, t => t.Contains("технический отчёт (без личных данных)"));
        Assert.DoesNotContain(texts, t => t.Contains("GitHub") || t.Contains("репы") || t.Contains("Логи") || t.Contains("Альфа"));
        Assert.Equal("Версия 2.1.42 — последняя · проверено в 19:04", Loc.Current.T("updUpToDate", "2.1.42", "19:04"));
        Assert.Equal("Обновлять автоматически", Loc.Current.T("autoUpdate"));
        AssertNoBindingErrors();
    }
}
