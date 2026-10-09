using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Features.Communities;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>Scratch: header x and frames for #21 (not committed).</summary>
public class Shots21 : UiTest
{
    private readonly ITestOutputHelper output;
    public Shots21(ITestOutputHelper output) => this.output = output;
    private static readonly DateTime Mon7 = new(2026, 9, 14, 8, 0, 0);

    [AvaloniaFact]
    public async Task Capture()
    {
        var tag = Environment.GetEnvironmentVariable("SHOT21") ?? "x";
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        db.Services.Launcher = new FakeLauncher();
        db.Services.FileDialogs = new FakeFileDialogs();
        db.Services.UpdateSource = new FakeUpdateSource();
        var shell = new ShellViewModel(db.Services) { Clock = () => Mon7 };
        shell.Register(SectionKey.Schedule, () => new ScheduleViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Homework, () => new HomeworkViewModel(db.Services, shell, () => Mon7));
        shell.Register(SectionKey.Community, () => new CommunitiesViewModel(db.Services));
        shell.Register(SectionKey.Settings, () => new SettingsViewModel(db.Services, shell, () => Mon7));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        SetTheme(ThemeVariant.Light, db.Services.Theme);
        foreach (var key in new[] { SectionKey.Schedule, SectionKey.Community, SectionKey.Chat, SectionKey.Homework, SectionKey.Settings })
        {
            shell.NavigateTo(key);
            Pump(); await Task.Delay(400); Pump();
            Report(window, key.ToString());
            Frames.Capture(window, $"21-{tag}-{key.ToString().ToLowerInvariant()}");
        }
        var settings = (SettingsViewModel)shell.Current!;
        settings.OpenPanelCommand.Execute("study");
        Pump(); await Task.Delay(300); Pump();
        Report(window, "Settings/study");
        Frames.Capture(window, $"21-{tag}-settings-study");
        window.Close();
    }

    private void Report(Window window, string name)
    {
        var heads = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.FontSize >= 20 && !string.IsNullOrEmpty(t.Text) && t.Bounds.Width > 0)
            .Select(t => (t.Text, X: t.TranslatePoint(new Point(0, 0), window)?.X ?? -1, t.FontSize)).Take(3);
        output.WriteLine($"{name}: " + string.Join(" | ", heads.Select(h => $"«{h.Text}» x={h.X:0} fs={h.FontSize}")));
    }
}
