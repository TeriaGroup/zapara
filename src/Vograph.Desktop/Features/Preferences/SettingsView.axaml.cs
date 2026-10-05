using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input.Platform;
namespace Vograph.Desktop.Features.Preferences;
public partial class SettingsView : UserControl
{
    private SettingsViewModel? boundSettings;
    public SettingsView(){InitializeComponent();SizeChanged+=(_,_)=>ApplyLayout();}
    protected override void OnDataContextChanged(EventArgs e)
    {
        if(boundSettings is not null){boundSettings.PropertyChanged-=SettingsChanged;boundSettings.Watch(false);boundSettings.SetDiagnosticsClipboardWriter(null);}
        base.OnDataContextChanged(e);boundSettings=DataContext as SettingsViewModel;
        if(boundSettings is not null){boundSettings.PropertyChanged+=SettingsChanged;boundSettings.Watch(IsVisible&&VisualRoot is not null);BindDiagnosticsClipboard();}
        ApplyLayout();
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); boundSettings?.Watch(IsVisible); BindDiagnosticsClipboard(); }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { boundSettings?.Watch(false); boundSettings?.SetDiagnosticsClipboardWriter(null); base.OnDetachedFromVisualTree(e); }
    private void BindDiagnosticsClipboard()
    {
        if(boundSettings is null)return;
        if(VisualRoot is null){boundSettings.SetDiagnosticsClipboardWriter(null);return;}
        boundSettings.SetDiagnosticsClipboardWriter(async value =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard unavailable");
            await clipboard.SetTextAsync(value);
        });
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if(change.Property==IsVisibleProperty)boundSettings?.Watch(IsVisible&&VisualRoot is not null); }
    private void SettingsChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(SettingsViewModel.ActivePanel))ApplyLayout();
        if(e.PropertyName!=nameof(SettingsViewModel.RequestedSettingAnchor) ||
           boundSettings?.RequestedSettingAnchor is not {Length:>0} anchor)return;
        Dispatcher.UIThread.Post(() =>
        {
            var target=this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control=>control.Name==anchor);
            target?.BringIntoView();target?.Focus();
        },DispatcherPriority.Loaded);
    }
    private void ApplyLayout()
    {
        if(SettingsColumns is null || boundSettings is null)return;
        var wide=Bounds.Width>=1000;
        SettingsColumns.ColumnDefinitions=new ColumnDefinitions(wide?"260,*":"*");
        Grid.SetColumn(SettingsDetails,wide?1:0);
        SettingsOverview.IsVisible=wide||boundSettings.ShowOverview;
        SettingsDetails.IsVisible=!boundSettings.ShowOverview;
    }
}
