using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Preferences;

public sealed partial class SettingsViewModel
{
    [ObservableProperty] private string activePanel = "";
    [ObservableProperty] private string dataSummary = "";
    [ObservableProperty] private bool syncingData;
    public bool CanSyncData => App.PrivateSync?.IsAttached == true && !SyncingData;
    public IRelayCommand ResolveDataConflictsCommand => _shell.ResolveSyncConflictsCommand;
    public string SyncConflictSummary => _shell.SyncConflictSummary;
    public bool HasDataConflicts => _shell.HasSyncConflicts;
    public bool ShowOverview => ActivePanel.Length == 0;
    public bool ShowAccountPanel => ActivePanel == "account";
    public bool ShowStudyPanel => ActivePanel == "study";
    public bool ShowAppearancePanel => ActivePanel == "appearance";
    public bool ShowNotificationsPanel => ActivePanel == "notifications";
    public bool ShowDataPanel => ActivePanel == "data";
    public bool ShowHelpPanel => ActivePanel == "help";
    public string PanelTitle => ActivePanel switch
    {
        "account" => "Аккаунт", "study" => "Учёба", "appearance" => "Оформление",
        "notifications" => "Уведомления", "data" => "Данные и синхронизация",
        "help" => "Помощь и обновления", _ => "Настройки"
    };
    partial void OnActivePanelChanged(string value)
    {
        if(value=="data")_=ReadDataSummary();
        foreach (var name in new[] { nameof(ShowOverview), nameof(ShowAccountPanel), nameof(ShowStudyPanel),
            nameof(ShowAppearancePanel), nameof(ShowNotificationsPanel), nameof(ShowDataPanel), nameof(ShowHelpPanel), nameof(PanelTitle) })
            OnPropertyChanged(name);
    }
    [RelayCommand] private void OpenPanel(string panel) => ActivePanel = panel;
    [RelayCommand] private void BackToOverview() => ActivePanel = "";
    private async Task ReadDataSummary()
    {
        var count = await RunAsync(()=>App.Outbox.Pending().Count(x=>x.Status=="pending").ToString(),"data summary");
        DataSummary=App.Profile.IsGuest ? "Локальный профиль гостя. Данные хранятся на устройстве." : $"Локальная копия аккаунта · ожидают отправки: {count??"—"}.";
        DataSummary += App.PrivateSync?.IsAttached==true ? " Синхронизация подключена." : " Синхронизация не подключена.";
        OnPropertyChanged(nameof(CanSyncData));OnPropertyChanged(nameof(SyncConflictSummary));OnPropertyChanged(nameof(HasDataConflicts));
    }
    partial void OnSyncingDataChanged(bool value)=>OnPropertyChanged(nameof(CanSyncData));
    [RelayCommand] private async Task SyncData()
    {
        if(!CanSyncData||App.PrivateSync is not {} sync)return;
        SyncingData=true;
        try{await sync.PushPendingAsync();await ReadDataSummary();}
        catch(Exception ex)when(ex is Vograph.Core.Services.Accounts.AccountClientException or HttpRequestException or OperationCanceledException){DataSummary="Синхронизация не завершена. Локальные данные сохранены.";}
        finally{SyncingData=false;}
    }
}
