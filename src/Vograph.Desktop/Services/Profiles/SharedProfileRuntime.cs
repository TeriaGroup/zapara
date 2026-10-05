using Vograph.Core.Services;

namespace Vograph.Desktop.Services.Profiles;

/// <summary>Installation-scoped state. Construct on process startup, never on a candidate worker.</summary>
public sealed class SharedProfileRuntime
{
    internal SharedProfileRuntime(string root, string language, Func<bool>? systemAnimations)
    {
        GlobalDataDir = Path.GetFullPath(root);
        Log = new AppLog(Path.Combine(root, "logs"));
        I18n = new I18nService("ru");
        Services.Loc.Init(I18n);
        Loc = Services.Loc.Current;
        AccountPanel = new();
        Prefs = UiPrefs.Load(Path.Combine(root, "ui.json"), ex => Log.Error("prefs", ex));
        Motion = new MotionSettings(Prefs, systemAnimations);
    }
    public string GlobalDataDir { get; }
    public AppLog Log { get; }
    public I18nService I18n { get; }
    public Loc Loc { get; }
    public UiPrefs Prefs { get; }
    public MotionSettings Motion { get; }
    public ThemeService? Theme { get; set; }
    public Features.Account.AccountPanelViewModel AccountPanel { get; private set; }
    internal void SetAccountPanel(Features.Account.AccountPanelViewModel panel)
    {
        AccountPanel.Dispose();
        AccountPanel = panel;
    }
}
