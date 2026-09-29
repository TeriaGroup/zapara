using Avalonia.Controls;
using Avalonia;
namespace Vograph.Desktop.Features.Preferences;
public partial class SettingsView : UserControl
{
    private SettingsViewModel? boundSettings;
    public SettingsView(){InitializeComponent();SizeChanged+=(_,_)=>ApplyLayout();}
    protected override void OnDataContextChanged(EventArgs e)
    {
        if(boundSettings is not null){boundSettings.PropertyChanged-=SettingsChanged;boundSettings.Watch(false);}
        base.OnDataContextChanged(e);boundSettings=DataContext as SettingsViewModel;
        if(boundSettings is not null){boundSettings.PropertyChanged+=SettingsChanged;boundSettings.Watch(IsVisible&&VisualRoot is not null);}
        ApplyLayout();
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); boundSettings?.Watch(IsVisible); }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { boundSettings?.Watch(false); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if(change.Property==IsVisibleProperty)boundSettings?.Watch(IsVisible&&VisualRoot is not null); }
    private void SettingsChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e){if(e.PropertyName==nameof(SettingsViewModel.ActivePanel))ApplyLayout();}
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
