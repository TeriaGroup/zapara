using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ThemeTests
{
    private static UiPrefs Prefs() => UiPrefs.Load(Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"), "ui.json"));

    private static Color BrushColor(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryFindResource(key, variant, out var value), $"resource {key} missing for {variant}");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    [AvaloniaFact]
    public void Tokens_Differ_Between_Variants()
    {
        Assert.Equal(Color.Parse("#0D0D0D"), BrushColor("Brush.Canvas", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#FFFFFF"), BrushColor("Brush.Canvas", ThemeVariant.Light));
        Assert.Equal(Color.Parse("#F2F2F2"), BrushColor("Brush.Accent", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#111111"), BrushColor("Brush.Accent", ThemeVariant.Light));
    }

    [AvaloniaFact]
    public void Apply_Changes_Actual_Variant_And_Persists_Choice()
    {
        var prefs = Prefs();
        var svc = ThemeService.ForApplication(Application.Current!, prefs);

        svc.Apply(ThemeChoice.Dark);
        Assert.Equal(ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
        Assert.Equal(ThemeChoice.Dark, UiPrefs.Load(prefs.FilePath).Theme);

        svc.Toggle();
        Assert.Equal(ThemeVariant.Light, Application.Current.ActualThemeVariant);
        Assert.False(svc.IsDark);

        svc.Apply(ThemeChoice.System);
        Assert.Equal(ThemeChoice.System, svc.Choice);
    }

    [AvaloniaFact]
    public void Window_Uses_Inter_And_Canvas_Background()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new Window { Width = 200, Height = 100, Content = new TextBlock { Text = "Aa 09:00" } };
        window.Show();

        Assert.Contains("Inter", window.FontFamily.ToString());
        Assert.Equal(Color.Parse("#0D0D0D"), Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
        Frames.Capture(window, "theme-window-dark");
    }
}
