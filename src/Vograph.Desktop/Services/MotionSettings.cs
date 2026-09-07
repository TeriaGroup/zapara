using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Services;

/// <summary>
/// Spec §7 «Reduce motion»: animations run only while the user's switch (ui.json Animations) AND the system
/// setting (SPI_GETCLIENTAREAANIMATION) both allow them. Code-driven animations ask Duration(); XAML transitions
/// live in Theme/Motion.axaml, which App includes or removes as Enabled flips. Behaviour never depends on it.
/// </summary>
public sealed partial class MotionSettings : ObservableObject
{
    /// <summary>The spec's single curve, cubic-bezier(.2,.8,.2,1).</summary>
    public static readonly Easing Ease = new SplineEasing(0.2, 0.8, 0.2, 1.0);

    /// <summary>What code finds for a control outside any window: never animate.</summary>
    public static readonly MotionSettings Off = new(new UiPrefs { Animations = false }, () => false);

    private readonly UiPrefs _prefs;
    private readonly Func<bool> _systemAllows;

    public MotionSettings(UiPrefs prefs, Func<bool>? systemAllows = null)
    {
        _prefs = prefs;
        _systemAllows = systemAllows ?? ReadSystemSetting;
        _enabled = prefs.Animations && _systemAllows();
    }

    [ObservableProperty] private bool _enabled;

    /// <summary>Re-reads both switches: Settings flips ui.json, and the system setting can change while the app runs.</summary>
    public void Refresh() => Enabled = _prefs.Animations && _systemAllows();

    public TimeSpan Duration(int ms) => Enabled ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;

    /// <summary>The settings a control obeys: the nearest ViewModelBase DataContext up the visual tree (a section, or the
    /// window's ShellViewModel). Off when there is none — a control in a bare test window never animates by accident.</summary>
    public static MotionSettings Resolve(Visual? visual)
    {
        for (var v = visual; v is not null; v = v.GetVisualParent())
            if (v is StyledElement { DataContext: ViewModelBase vm }) return vm.Motion;
        return Off;
    }

    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

    /// <summary>Windows «Показывать анимацию в окнах»; true elsewhere and whenever the call fails.</summary>
    public static bool ReadSystemSetting()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try { return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var value, 0) || value != 0; }
        catch (Exception) { return true; }
    }
}
