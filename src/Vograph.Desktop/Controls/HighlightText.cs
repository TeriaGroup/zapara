using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Vograph.Desktop.Dialogs;

namespace Vograph.Desktop.Controls;

/// <summary>A TextBlock that renders Source with the part matching Query (GroupSearch rules) in SemiBold.</summary>
public sealed class HighlightText : TextBlock
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<HighlightText, string?>(nameof(Source));
    public static readonly StyledProperty<string?> QueryProperty = AvaloniaProperty.Register<HighlightText, string?>(nameof(Query));

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public string? Query { get => GetValue(QueryProperty); set => SetValue(QueryProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == QueryProperty) Rebuild();
    }

    private void Rebuild()
    {
        var text = Source ?? "";
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        if (GroupSearch.MatchRange(text, Query ?? "") is not { } r)
        {
            Inlines.Add(new Run(text));
            return;
        }
        Inlines.Add(new Run(text[..r.Start]));
        Inlines.Add(new Run(text.Substring(r.Start, r.Length)) { FontWeight = FontWeight.SemiBold });
        Inlines.Add(new Run(text[(r.Start + r.Length)..]));
    }
}
