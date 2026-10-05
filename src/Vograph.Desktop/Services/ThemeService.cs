using System.Diagnostics;
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

    /// <summary>Set by MainWindow: runs the variant switch inside a crossfade (snapshot of the old theme fading out).
    /// Null = switch instantly. The switch itself always happens synchronously inside the delegate, before any await.</summary>
    public Func<Action, Task>? Transition { get; set; }

    public void Apply(ThemeChoice choice, bool save = true)
    {
        var variant = choice switch
        {
            ThemeChoice.Light => ThemeVariant.Light,
            ThemeChoice.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        if (Transition is { } transition)
        {
            // ThemeCrossfade guards its whole body — every failure there falls back to the plain switch and is
            // logged — so this fire-and-forget cannot leave an unobserved exception behind. A transition that
            // still manages to throw synchronously must not cost the user the switch either.
            try { _ = transition(() => _apply(variant)); }
            catch (Exception ex)
            {
                Trace.TraceWarning($"theme: the transition failed, switching plainly: {ex.GetType().Name}: {ex.Message}");
                _apply(variant);
            }
        }
        else _apply(variant);
        _prefs.Theme = choice;
        if (save) _prefs.Save();
        Changed?.Invoke();
    }

    /// <summary>Sidebar button: flips between explicit Light and Dark (leaves "System" mode).</summary>
    public void Toggle() => Apply(IsDark ? ThemeChoice.Light : ThemeChoice.Dark);
}
