using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.Maps;

/// <summary>Window-wide overlay showing the same plan; Esc or ✕ closes it (ShellViewModel.Overlay = null).</summary>
public sealed partial class MapFullscreenViewModel : ViewModelBase
{
    public MapFullscreenViewModel(AppServices app, MapsViewModel owner) : base(app) => Owner = owner;
    public MapsViewModel Owner { get; }
    [RelayCommand] private void Close() => Owner.ToggleFullscreenCommand.Execute(null);
}
