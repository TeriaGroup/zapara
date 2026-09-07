using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Shell;

public enum SectionKey { Schedule, Week, Summary, Teachers, Maps, Friends, Homework, Settings }

public sealed partial class NavSection : ObservableObject
{
    public NavSection(SectionKey key, string labelKey, string iconKey, string hotkey, IRelayCommand<string> navigate)
    {
        Key = key;
        LabelKey = labelKey;
        IconKey = iconKey;
        Hotkey = hotkey;
        NavigateCommand = navigate;
    }

    public SectionKey Key { get; }
    public string KeyName => Key.ToString();
    public string LabelKey { get; }
    public string IconKey { get; }
    public IRelayCommand<string> NavigateCommand { get; }
    public string Label => Loc.Current.T(LabelKey);

    /// <summary>«Ctrl+1» … «Ctrl+8» — spec §4.3, shown only inside tooltips.</summary>
    public string Hotkey { get; }
    public string AutomationId => "Nav." + KeyName;

    /// <summary>Tooltip on the collapsed rail only: the label is visible otherwise, and the hotkey rides along.</summary>
    public string? Tip => IsCompact ? $"{Label} ({Hotkey})" : null;

    partial void OnIsCompactChanged(bool value) => OnPropertyChanged(nameof(Tip));

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isCompact;
    [ObservableProperty] private string? _badge;

    public void RefreshLabel()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Tip));
    }
}
