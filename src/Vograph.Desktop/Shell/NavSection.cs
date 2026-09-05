using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Shell;

public enum SectionKey { Schedule, Week, Summary, Teachers, Maps, Friends, Homework, Settings }

public sealed partial class NavSection : ObservableObject
{
    public NavSection(SectionKey key, string labelKey, string iconKey, IRelayCommand<string> navigate)
    {
        Key = key;
        LabelKey = labelKey;
        IconKey = iconKey;
        NavigateCommand = navigate;
    }

    public SectionKey Key { get; }
    public string KeyName => Key.ToString();
    public string LabelKey { get; }
    public string IconKey { get; }
    public IRelayCommand<string> NavigateCommand { get; }
    public string Label => Loc.Current.T(LabelKey);

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isCompact;
    [ObservableProperty] private string? _badge;

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}
