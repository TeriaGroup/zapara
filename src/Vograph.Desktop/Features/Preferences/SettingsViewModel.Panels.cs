using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Preferences;

public sealed partial class SettingsViewModel
{
    [ObservableProperty] private string activePanel = "";
    [ObservableProperty] private string settingsSearch = "";
    private static readonly (string Id, string Text)[] Categories =
    [
        ("account", "аккаунт профиль вход логин пароль устройства фото"),
        ("study", "учёба учеба группа расписание подгруппа чётность четность"),
        ("appearance", "оформление тема светлая тёмная темная анимации"),
        ("notifications", "уведомления напоминания время утром вечером"),
        ("data", "данные синхронизация офлайн копия импорт конфликт"),
        ("help", "помощь поддержка баг обновление документы")
    ];
    private bool CategoryMatches(string id)
    {
        var words = SettingsSearch.Trim().ToLowerInvariant().Replace('ё', 'е')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var text = Categories.First(category => category.Id == id).Text.Replace('ё', 'е');
        return words.All(word => text.Contains(word, StringComparison.Ordinal));
    }
    public bool HasSettingsSearch => SettingsSearch.Trim().Length > 0;
    public int SettingsSearchCount => Categories.Count(category => CategoryMatches(category.Id));
    public bool NoSettingsSearchResults => HasSettingsSearch && SettingsSearchCount == 0;
    public bool ShowAccountCategory => CategoryMatches("account");
    public bool ShowStudyCategory => CategoryMatches("study");
    public bool ShowAppearanceCategory => CategoryMatches("appearance");
    public bool ShowNotificationsCategory => CategoryMatches("notifications");
    public bool ShowDataCategory => CategoryMatches("data");
    public bool ShowHelpCategory => CategoryMatches("help");
    partial void OnSettingsSearchChanged(string value)
    {
        foreach (var name in new[] { nameof(HasSettingsSearch), nameof(SettingsSearchCount), nameof(NoSettingsSearchResults),
            nameof(ShowAccountCategory), nameof(ShowStudyCategory), nameof(ShowAppearanceCategory),
            nameof(ShowNotificationsCategory), nameof(ShowDataCategory), nameof(ShowHelpCategory) }) OnPropertyChanged(name);
    }
    [RelayCommand] private void ClearSettingsSearch() => SettingsSearch = "";
    [ObservableProperty] private string dataSummary = "";
    [ObservableProperty] private bool syncingData;
    private long dataSummaryReadSequence;
    private long dataSummaryAppliedSequence;
    private sealed record SyncCounts(int Pending, int Conflicts, long Sequence);
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
        RefreshProfileTimer();
        if(value=="data")_=ReadDataSummary();
        foreach (var name in new[] { nameof(ShowOverview), nameof(ShowAccountPanel), nameof(ShowStudyPanel),
            nameof(ShowAppearancePanel), nameof(ShowNotificationsPanel), nameof(ShowDataPanel), nameof(ShowHelpPanel), nameof(PanelTitle) })
            OnPropertyChanged(name);
    }
    [RelayCommand] private void OpenPanel(string panel) => ActivePanel = panel;
    [RelayCommand] private void BackToOverview() => ActivePanel = "";
    private async Task ReadDataSummary()
    {
        var counts = await RunAsync(() =>
        {
            var rows = App.Outbox.Pending();
            return new SyncCounts(rows.Count(x => x.Status == "pending"), rows.Count(x => x.Status == "conflict"),
                Interlocked.Increment(ref dataSummaryReadSequence));
        }, "data summary");
        if (!App.Work.CanPublish || counts is null || counts.Sequence < dataSummaryAppliedSequence) return;
        dataSummaryAppliedSequence = counts.Sequence;
        if (App.Profile.IsGuest) DataSummary = "Локальный профиль гостя. Данные хранятся на устройстве.";
        else
        {
            var health = App.PrivateSync?.Health;
            DataSummary = $"Локальная копия аккаунта · ожидают отправки: {counts?.Pending.ToString() ?? "—"}; конфликтов: {counts?.Conflicts.ToString() ?? "—"}.";
            DataSummary += health switch
            {
                null or { Attached: false } => " Обмен с сервером не подключён.",
                { Exchanging: true } => " Идёт обмен данными.",
                { LastFailure: { } failure } => " Последняя попытка: " + failure,
                { LastSuccessAt: { } at } => " Последний успешный обмен: " + at.ToLocalTime().ToString("dd.MM HH:mm") + ".",
                _ => " Ожидается первый обмен с сервером."
            };
        }
        OnPropertyChanged(nameof(CanSyncData));OnPropertyChanged(nameof(SyncConflictSummary));OnPropertyChanged(nameof(HasDataConflicts));
    }
    partial void OnSyncingDataChanged(bool value)=>OnPropertyChanged(nameof(CanSyncData));
    [RelayCommand] private async Task SyncData()
    {
        if(!CanSyncData||App.PrivateSync is not {} sync)return;
        SyncingData=true;
        try{await sync.SyncNowAsync();await ReadDataSummary();}
        catch(Exception ex)when(ex is Vograph.Core.Services.Accounts.AccountClientException or HttpRequestException or OperationCanceledException){DataSummary="Синхронизация не завершена. Локальные данные сохранены.";}
        catch(Exception ex){App.Log.Error("manual private sync",ex);DataSummary="Синхронизация не завершена. Локальные данные сохранены.";}
        finally{SyncingData=false;}
    }
}
