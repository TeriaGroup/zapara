using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace Vograph.Desktop.Features.Homeworks;

public partial class HomeworkView : UserControl
{
    private HomeworkViewModel? bound;
    public HomeworkView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); Bind(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { bound?.SetClipboardWriter(null); bound = null; base.OnDetachedFromVisualTree(e); }
    private void Bind()
    {
        bound?.SetClipboardWriter(null);
        bound = this.IsAttachedToVisualTree() ? DataContext as HomeworkViewModel : null;
        bound?.SetClipboardWriter(async value =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard unavailable");
            await clipboard.SetTextAsync(value);
        });
    }

    /// <summary>#9 (G-2): нажатие в любое место карточки задания открывает или закрывает её лист — как у карточки пары
    /// (LessonCardView) и на web. Нажатия по кнопкам, флажку выбора, полям и ссылкам внутри карточки остаются им.</summary>
    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Visual card || (card as StyledElement)?.DataContext is not HomeworkRowViewModel row) return;
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors().TakeWhile(v => v != card)
                .Any(v => v is Button or ToggleButton or TextBox or MenuItem or HyperlinkButton)) return;
        row.ToggleSheetCommand.Execute(null);
        e.Handled = true;
    }
}
