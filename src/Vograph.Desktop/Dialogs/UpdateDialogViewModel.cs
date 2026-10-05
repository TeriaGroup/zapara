using Vograph.Desktop.Services;

namespace Vograph.Desktop.Dialogs;

/// <summary>«Доступна windows-v2.1.0 · 05.09.2026» — Install and restart / Later.</summary>
public sealed class UpdateDialogViewModel : DialogViewModelBase
{
    public UpdateDialogViewModel(string tag, string? publishedText)
    {
        Title = Loc.Current.T("updAvailable", tag);
        PublishedText = publishedText ?? "";
    }

    public string PublishedText { get; }
    public bool HasPublished => PublishedText.Length > 0;
    public string Hint => Loc.Current.T("updDialogHint");
}
