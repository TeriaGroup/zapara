using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Account;

public sealed record DocumentMatch(int Index, string Text)
{
    public string Label => Text.Length <= 90 ? Text : Text[..90] + "…";
}

public sealed partial class AccountPanelViewModel
{
    [ObservableProperty] private string documentSearch = "";
    [ObservableProperty] private string selectedDocumentParagraph = "";
    [ObservableProperty] private int documentTextSize = 15;
    public IReadOnlyList<DocumentMatch> DocumentMatches
    {
        get
        {
            if (OpenDocument is null || string.IsNullOrWhiteSpace(DocumentSearch)) return [];
            var words = DocumentSearch.Trim().ToLowerInvariant().Replace('ё', 'е')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Regex.Split(OpenDocument.Body, @"\r?\n\s*\r?\n")
                .Select((paragraph, index) => new DocumentMatch(index, paragraph.Trim()))
                .Where(hit => hit.Text.Length > 0 && words.All(word =>
                    hit.Text.ToLowerInvariant().Replace('ё', 'е').Contains(word, StringComparison.Ordinal)))
                .ToArray();
        }
    }
    public string DocumentSearchCount => $"Найдено абзацев: {DocumentMatches.Count}";
    public bool NoDocumentMatches => DocumentSearch.Trim().Length > 0 && DocumentMatches.Count == 0;
    partial void OnDocumentSearchChanged(string value)
    {
        SelectedDocumentParagraph = "";
        OnPropertyChanged(nameof(DocumentMatches)); OnPropertyChanged(nameof(DocumentSearchCount));
        OnPropertyChanged(nameof(NoDocumentMatches));
    }
    [RelayCommand] private void SelectDocumentMatch(DocumentMatch? match)
    {
        if (match is null || !DocumentMatches.Contains(match)) return;
        SelectedDocumentParagraph = match.Text;
    }
    [RelayCommand] private void EnlargeDocumentText() => DocumentTextSize = Math.Min(23, DocumentTextSize + 2);
    [RelayCommand] private void ReduceDocumentText() => DocumentTextSize = Math.Max(13, DocumentTextSize - 2);
}
