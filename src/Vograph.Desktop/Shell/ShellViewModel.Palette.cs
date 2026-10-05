using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Shell;

public sealed partial class ShellViewModel
{
    [ObservableProperty] private bool showCommandPalette;
    [ObservableProperty] private string commandQuery = "";
    public IReadOnlyList<NavSection> PaletteSections
    {
        get
        {
            var words = CommandQuery.Trim().ToLowerInvariant().Replace('ё', 'е')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return AllSections.Where(section =>
            {
                var text = (section.Label + " " + section.KeyName).ToLowerInvariant().Replace('ё', 'е');
                return words.All(word => text.Contains(word, StringComparison.Ordinal));
            }).ToArray();
        }
    }
    public bool NoPaletteMatches => ShowCommandPalette && PaletteSections.Count == 0;
    partial void OnCommandQueryChanged(string value)
    { OnPropertyChanged(nameof(PaletteSections)); OnPropertyChanged(nameof(NoPaletteMatches)); }
    partial void OnShowCommandPaletteChanged(bool value) => OnPropertyChanged(nameof(NoPaletteMatches));
    [RelayCommand] private void OpenCommandPalette()
    {
        if (!CanPublish || Dialogs.HasDialog) return;
        Overlay = null;
        CommandQuery = "";
        ShowCommandPalette = true;
    }
    [RelayCommand] private void CloseCommandPalette() => ShowCommandPalette = false;
    [RelayCommand] private void OpenPaletteSection(NavSection? section)
    {
        if (!ShowCommandPalette || section is null || !AllSections.Contains(section)) return;
        ShowCommandPalette = false;
        NavigateTo(section.Key);
    }
}
