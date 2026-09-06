using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.States;

/// <summary>Stands in for sections that arrive in stage 2.</summary>
public sealed class PlaceholderViewModel : ViewModelBase
{
    private readonly string _labelKey;
    private readonly Action _onLanguage;

    public PlaceholderViewModel(AppServices app, string labelKey) : base(app)
    {
        _labelKey = labelKey;
        _onLanguage = () => OnPropertyChanged(nameof(Title));
        app.Loc.LanguageChanged += _onLanguage;
    }

    public override void Detach() => App.Loc.LanguageChanged -= _onLanguage;

    public string Title => T(_labelKey);
}
