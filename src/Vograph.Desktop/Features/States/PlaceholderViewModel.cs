using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.States;

/// <summary>Stands in for sections that arrive in stage 2.</summary>
public sealed class PlaceholderViewModel : ViewModelBase
{
    private readonly string _labelKey;

    public PlaceholderViewModel(AppServices app, string labelKey) : base(app)
    {
        _labelKey = labelKey;
        app.Loc.LanguageChanged += () => OnPropertyChanged(nameof(Title));
    }

    public string Title => T(_labelKey);
}
