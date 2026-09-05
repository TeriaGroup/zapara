using Avalonia;
using Avalonia.Styling;

namespace Vograph.Desktop.Services;

/// <summary>
/// System / Light / Dark switch. Avalonia follows the OS itself when the variant is Default,
/// so no registry polling is needed. Pure delegates keep it constructible in plain unit tests.
/// </summary>
public sealed class ThemeService
{
    private readonly Action<ThemeVariant> _apply;
    private readonly Func<bool> _isDark;
    private readonly UiPrefs _prefs;

    public ThemeService(Action<ThemeVariant> apply, Func<bool> isDark, UiPrefs prefs)
    {
        _apply = apply;
        _isDark = isDark;
        _prefs = prefs;
        Apply(prefs.Theme, save: false);
    }

    public static ThemeService ForApplication(Application app, UiPrefs prefs) =>
        new(v => app.RequestedThemeVariant = v, () => app.ActualThemeVariant == ThemeVariant.Dark, prefs);

    public ThemeChoice Choice => _prefs.Theme;
    public bool IsDark => _isDark();
    public event Action? Changed;

    public void Apply(ThemeChoice choice, bool save = true)
    {
        _apply(choice switch
        {
            ThemeChoice.Light => ThemeVariant.Light,
            ThemeChoice.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        });
        _prefs.Theme = choice;
        if (save) _prefs.Save();
        Changed?.Invoke();
    }

    /// <summary>Sidebar button: flips between explicit Light and Dark (leaves "System" mode).</summary>
    public void Toggle() => Apply(IsDark ? ThemeChoice.Light : ThemeChoice.Dark);
}
