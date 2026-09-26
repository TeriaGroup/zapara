using Avalonia.Controls;
namespace Vograph.Desktop.Features.Preferences;
public partial class SettingsView : UserControl
{
    private SettingsViewModel? boundSettings;
    public SettingsView(){InitializeComponent();SizeChanged+=(_,_)=>ApplyLayout();}
    protected override void OnDataContextChanged(EventArgs e)
    {
        if(boundSettings is not null)boundSettings.PropertyChanged-=SettingsChanged;
        base.OnDataContextChanged(e);boundSettings=DataContext as SettingsViewModel;
        if(boundSettings is not null)boundSettings.PropertyChanged+=SettingsChanged;
        ApplyLayout();
    }
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
