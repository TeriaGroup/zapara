using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Vograph.Desktop.Features.Schedule;

public partial class LessonCardView : UserControl
{
    public LessonCardView()
    {
        InitializeComponent();
        Card.Tapped += OnCardTapped;
    }

    /// <summary>#9 (G-2): нажатие в любое место карточки открывает или закрывает лист пары, как на web.
    /// Нажатия по кнопкам, полям и ссылкам внутри карточки остаются им.</summary>
    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not LessonRowViewModel row) return;
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors().TakeWhile(v => v != Card)
                .Any(v => v is Button or ToggleButton or TextBox or MenuItem or HyperlinkButton)) return;
        row.ToggleSheetCommand.Execute(null);
        e.Handled = true;
    }
}
