using Avalonia;
using Avalonia.Controls;
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
}
