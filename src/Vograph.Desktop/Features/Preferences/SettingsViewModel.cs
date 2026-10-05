using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services;
using Zapara.Client.Domain;
using Vograph.Desktop.Shell;
using Vograph.Desktop.ViewModels;
using Vograph.Timetable;

namespace Vograph.Desktop.Features.Preferences;

public sealed partial class SettingsViewModel : ViewModelBase
{
    public const string ReleasesUrl = "https://github.com/TeriaGroup/zapara/releases";
    public const string TimetableSourceUrl = VoenmehScheduleClient.Origin;
    public const string MapsSourceUrl = "https://voenmeh.ru/openmap/";

    private readonly ShellViewModel _shell;
    private readonly Func<DateTime> _clock;
    private readonly Action _reload;
    private readonly Action _groupReload;
    private int studyRenderEpoch;
    private readonly PropertyChangedEventHandler _onShell;
    private readonly Action _onTheme;
    private readonly Action _onSyncHealth;
    private bool _suppress;
    private int _version;
    private DispatcherTimer? profileRefreshTimer;
    private bool settingsVisible;
    private int profileRefreshBusy;

    public void Watch(bool visible)
    {
        settingsVisible = visible;
        RefreshProfileTimer();
    }

    private void RefreshProfileTimer()
    {
        profileRefreshTimer?.Stop();
        profileRefreshTimer = null;
        if (!settingsVisible || ActivePanel != "account" || AccountPanel.IsGuest) return;
        _ = RefreshVisibleProfileAsync();
        profileRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        profileRefreshTimer.Tick += async (_, _) => await RefreshVisibleProfileAsync();
        profileRefreshTimer.Start();
    }

    private async Task RefreshVisibleProfileAsync()
    {
        if (Interlocked.Exchange(ref profileRefreshBusy, 1) != 0) return;
        try { if (settingsVisible && ActivePanel == "account") await AccountPanel.RefreshRemoteAsync(); }
        finally { Interlocked.Exchange(ref profileRefreshBusy, 0); }
    }

    public SettingsViewModel(AppServices app, ShellViewModel shell, Func<DateTime>? clock = null) : base(app)
    {
        _shell = shell;
        _clock = clock ?? (() => DateTime.Now);
        StudyImpactWeekDate = _clock().Date;
        _themeItems = BuildThemeItems();
        _themeIndex = app.Theme is { } t ? (int)t.Choice : (int)app.Prefs.Theme;
        _compactSidebar = shell.SidebarCollapsed;
        _animations = app.Prefs.Animations;
        _notificationsEnabled = app.Prefs.NotificationsEnabled;
        _lanSync = app.LanSync.IsRunning;
        _reload = () => _ = LoadAsync();
        _groupReload = () => { studyRenderEpoch++; ClearStudySubgroupUndo(); ClearStudyImpact(); ClearDiagnostics(); _ = LoadAsync(); };
        _onShell = (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.SidebarCollapsed)) Suppressed(() => CompactSidebar = shell.SidebarCollapsed);
            if (e.PropertyName == nameof(ShellViewModel.IsRefreshing)) IsRefreshing = shell.IsRefreshing;
        };
        _onTheme = () => { if (App.Theme is { } t) Suppressed(() => ThemeIndex = (int)t.Choice); };
        _onSyncHealth = () => Dispatcher.UIThread.Post(() =>
        {
            if (ActivePanel == "data" && App.Work.CanPublish) _ = ReadDataSummary();
        });
        shell.PropertyChanged += _onShell;
        shell.GroupChanged += _groupReload;
        shell.ScheduleChanged += _reload;
        app.Loc.LanguageChanged += Relabel;
        app.Outbox.Changed += _onSyncHealth;
        if (app.PrivateSync is { } sync) sync.HealthChanged += _onSyncHealth;
        // LanSync.Imported is the shell's to handle (NotifyImportedAsync): a LAN push has to refresh the app
        // whether or not this section was ever built, and ScheduleChanged brings this card along with it.
        if (app.Theme is { } theme) theme.Changed += _onTheme; // mirrors the sidebar's quick-toggle back into ThemeIndex
    }

    public override void Detach()
    {
        studyUndoClock.Stop();
        Watch(false);
        SetDiagnosticsClipboardWriter(null); ClearDiagnostics();
        _shell.PropertyChanged -= _onShell;
        _shell.GroupChanged -= _groupReload;
        _shell.ScheduleChanged -= _reload;
        App.Loc.LanguageChanged -= Relabel;
        App.Outbox.Changed -= _onSyncHealth;
        if (App.PrivateSync is { } sync) sync.HealthChanged -= _onSyncHealth;
        if (App.Theme is { } theme) theme.Changed -= _onTheme;
        QrVisible = false;
        QrImage?.Dispose(); // the section is going away: the decoded QR goes with it (T10 #2)
        QrImage = null;
    }

    /// <summary>Mirrors external state into a bound property without triggering the property's own save.</summary>
    private void Suppressed(Action apply)
    {
        _suppress = true;
        try { apply(); }
        finally { _suppress = false; }
    }

    /// <summary>Spec 5.8: entering Settings checks for an update once per session, and only while the switch is on.
    /// AllowNetwork is the process-wide gate (false under TestDb), so the check never reaches GitHub in tests.</summary>
    public override async Task ActivateAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        await LoadAsync();
        await LoadReportAsync();
        await Updates.LoadAsync();
        if (App.AllowNetwork && Updates.AutoUpdate && !Updates.CheckedThisSession && !Updates.IsChecking) _ = Updates.CheckAsync();
    }

    public string Title => T("navSettings");
    public Features.Account.AccountPanelViewModel AccountPanel => App.Shared.AccountPanel;
    public ObservableCollection<SupportNote> ReportMessages { get; } = [];
    public ObservableCollection<SupportThreadItem> ReportThreads { get; } = [];
    [ObservableProperty] private string reportThreadSearch = "";
    public IReadOnlyList<SupportThreadItem> FilteredReportThreads => ReportThreads.Where(thread =>
        thread.Subject.Contains(ReportThreadSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    public bool NoReportThreadMatches => ReportThreads.Count > 0 && FilteredReportThreads.Count == 0;
    partial void OnReportThreadSearchChanged(string value) => RefreshReportThreadSearch();
    [RelayCommand] private void ClearReportThreadSearch() => ReportThreadSearch = "";
    private void RefreshReportThreadSearch()
    { OnPropertyChanged(nameof(FilteredReportThreads)); OnPropertyChanged(nameof(NoReportThreadMatches)); }
    public ObservableCollection<SupportDraftAttachment> ReportFiles { get; } = [];
    [ObservableProperty] private string reportSubject = "";
    [ObservableProperty] private string reportBody = "";
    private long reportDraftRevision;
    partial void OnReportSubjectChanged(string value) => reportDraftRevision++;
    partial void OnReportBodyChanged(string value) => reportDraftRevision++;
    internal static bool MayClearReportDraft(long sentRevision, long currentRevision) => sentRevision == currentRevision;
    [ObservableProperty] private string reportNote = "";
    [ObservableProperty] private string reportReplyBody = "";
    [ObservableProperty] private string reportHistoryStatus = "";
    [ObservableProperty] private bool reportHistoryLoading;
    [ObservableProperty] private string selectedReportSubject = "";
    public bool HasSelectedReportThread => reportThreadId.HasValue;
    public bool HasReportThreads => ReportThreads.Count > 0;
    [ObservableProperty] private string reportFilesText = "";
    [ObservableProperty] private bool reportAttachmentLoading;
    [ObservableProperty] private bool reportSending;
    public bool CanSendReport => !ReportSending && !ReportAttachmentLoading;
    partial void OnReportSendingChanged(bool value) => OnPropertyChanged(nameof(CanSendReport));
    partial void OnReportAttachmentLoadingChanged(bool value) => OnPropertyChanged(nameof(CanSendReport));
    [ObservableProperty] private bool confirmDiscardReport;
    private long pendingDiscardReportRevision;
    [RelayCommand] private void RequestDiscardReport()
    {
        if (ReportSending || ReportAttachmentLoading ||
            ReportSubject.Length == 0 && ReportBody.Length == 0 && ReportFiles.Count == 0) return;
        pendingDiscardReportRevision = reportDraftRevision;
        ConfirmDiscardReport = true;
    }
    [RelayCommand] private void CancelDiscardReport() => ConfirmDiscardReport = false;
    [RelayCommand] private void DiscardReport()
    {
        if (!ConfirmDiscardReport || ReportSending || ReportAttachmentLoading) return;
        ConfirmDiscardReport = false;
        if (pendingDiscardReportRevision != reportDraftRevision)
        { ReportNote = "Черновик изменился. Подтвердите очистку ещё раз."; return; }
        ReportSubject = ""; ReportBody = "";
        reportPhotos.Clear(); reportLogs.Clear(); ReportFiles.Clear(); ReportFilesText = "";
        reportDraftRevision++;
        ReportNote = "Черновик очищен.";
    }
    private Guid? reportThreadId;
    private readonly List<SupportUpload> reportPhotos = [];
    private readonly List<SupportUpload> reportLogs = [];
    private readonly Dictionary<Guid, string> reportReplyDrafts = [];
    private int reportHistoryVersion;

    public async Task LoadReportAsync()
    {
        using var operation = App.Work.Enter();
        var version = ++reportHistoryVersion;
        if (AccountPanel.IsGuest)
        {
            ReportHistoryLoading = false;
            ReportThreads.Clear();
            ReportThreadSearch = "";
            RefreshReportThreadSearch();
            ReportMessages.Clear();
            reportReplyDrafts.Clear();
            reportThreadId = null;
            ReportReplyBody = "";
            SelectedReportSubject = "";
            ReportHistoryStatus = "Войдите в аккаунт, чтобы увидеть обращения.";
            OnPropertyChanged(nameof(HasReportThreads));
            OnPropertyChanged(nameof(HasSelectedReportThread));
            return;
        }
        ReportHistoryLoading = true;
        ReportHistoryStatus = "Загружаем обращения…";
        try
        {
            var list = await AccountPanel.LoadSupportAsync(operation.Token);
            if (!operation.IsCurrent || version != reportHistoryVersion || AccountPanel.IsGuest) return;
            if (list is null) { ReportHistoryStatus = "Не удалось загрузить обращения. Повторите загрузку."; return; }
            ApplyReportHistoryIfCurrent(version, list);
            ReportHistoryStatus = ReportThreads.Count == 0 ? "Обращений пока нет." : "";
        }
        catch (OperationCanceledException) { }
        catch (AccountClientException) { if (operation.IsCurrent && version == reportHistoryVersion) ReportHistoryStatus = "Не удалось загрузить обращения. Повторите загрузку."; }
        finally { if (operation.IsCurrent && version == reportHistoryVersion) ReportHistoryLoading = false; }
    }

    [RelayCommand] private Task RetryReportHistory() => LoadReportAsync();

    internal int ReportHistoryVersion => reportHistoryVersion;
    internal bool ApplyReportHistoryIfCurrent(int version, IReadOnlyList<SupportThreadResponse> threads)
    {
        if (version != reportHistoryVersion) return false;
        ApplyReportThreads(threads);
        return true;
    }

    internal void ApplyAcceptedReportThread(SupportThreadResponse saved, bool select = true)
    {
        ++reportHistoryVersion;
        ReportHistoryLoading = false;
        ReportHistoryStatus = "";
        var old = ReportThreads.FirstOrDefault(item => item.Id == saved.Id);
        if (old is not null) ReportThreads.Remove(old);
        var updated = new SupportThreadItem(saved.Id, saved.Subject, saved.Messages);
        ReportThreads.Add(updated);
        RefreshReportThreadSearch();
        if (select) SelectReportThread(updated);
        OnPropertyChanged(nameof(HasReportThreads));
    }

    internal void ApplyReportThreads(IReadOnlyList<SupportThreadResponse> threads)
    {
        var selected = reportThreadId;
        ReportThreads.Clear();
        foreach (var thread in threads) ReportThreads.Add(new SupportThreadItem(thread.Id, thread.Subject, thread.Messages));
        RefreshReportThreadSearch();
        var target = ReportThreads.FirstOrDefault(item => item.Id == selected) ?? ReportThreads.LastOrDefault();
        SelectReportThread(target);
        OnPropertyChanged(nameof(HasReportThreads));
    }

    [RelayCommand]
    private void SelectReportThread(SupportThreadItem? item)
    {
        if (item is not null && !ReportThreads.Contains(item)) return;
        if (reportThreadId is Guid previous) reportReplyDrafts[previous] = ReportReplyBody;
        reportThreadId = item?.Id;
        ReportReplyBody = item is not null && reportReplyDrafts.TryGetValue(item.Id, out var draft) ? draft : "";
        SelectedReportSubject = item?.Subject ?? "";
        ReportMessages.Clear();
        if (item is not null) foreach (var line in item.Messages) ReportMessages.Add(new(line.Author, Shown(line)));
        OnPropertyChanged(nameof(HasSelectedReportThread));
    }

    [RelayCommand]
    private async Task SendReport()
    {
        if (ReportSending || ReportAttachmentLoading) { ReportNote = "Дождитесь завершения текущего действия."; return; }
        var (_, error) = SupportChat.Submit(!AccountPanel.IsGuest, ReportMessages.ToArray(), ReportSubject, ReportBody);
        ReportNote = error ?? "";
        if (error is not null) return;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        ReportSending = true;
        var revision = reportDraftRevision;
        var subject = ReportSubject.Trim();
        var body = ReportBody.Trim();
        var files = reportPhotos.Concat(reportLogs).ToArray();
        try
        {
            var saved = await AccountPanel.SendSupportAsync(null, subject, body, files, operation.Token);
            if (!operation.IsCurrent) return;
            if (saved is null)
            {
                ReportNote = "Войдите в аккаунт, чтобы отправить сообщение и увидеть ответ.";
                return;
            }
            ApplyAcceptedReportThread(saved);
            if (MayClearReportDraft(revision, reportDraftRevision))
            {
                ReportSubject = "";
                ReportBody = "";
                reportPhotos.Clear();
                reportLogs.Clear();
                ReportFiles.Clear();
                ReportFilesText = "";
            }
            else ReportNote = "Предыдущее сообщение отправлено. Новый черновик сохранён.";
        }
        catch (OperationCanceledException) { }
        catch (AccountClientException) { if (operation.IsCurrent) ReportNote = "Сообщение не отправилось"; }
        finally { ReportSending = false; }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SendReportReply()
    {
        var id = reportThreadId;
        var text = ReportReplyBody.Trim();
        if (id is null) { ReportNote = "Выберите обращение для ответа."; return; }
        if (AccountPanel.IsGuest || text.Length < 3) { ReportNote = "Напишите ответ длиной не меньше трёх символов."; return; }
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var draft = ReportReplyBody;
        try
        {
            var saved = await AccountPanel.SendSupportAsync(id, "", text, operation.Token);
            if (!operation.IsCurrent) return;
            if (saved is null) { if (reportThreadId == id) ReportNote = "Войдите в аккаунт, чтобы отправить ответ."; return; }
            var stillSelected = reportThreadId == id;
            if (stillSelected && ReportReplyBody == draft) ReportReplyBody = "";
            ApplyAcceptedReportThread(saved, stillSelected);
            if (reportThreadId != id)
            {
                if (reportReplyDrafts.TryGetValue(id.Value, out var oldDraft) && oldDraft == draft) reportReplyDrafts.Remove(id.Value);
                return;
            }
            ReportNote = "Ответ отправлен.";
        }
        catch (OperationCanceledException) { }
        catch (AccountClientException) { if (operation.IsCurrent && reportThreadId == id) ReportNote = "Ответ не отправился. Текст сохранён."; }
    }

    [RelayCommand]
    private Task PickReportPhoto() => PickReport("photo");

    [RelayCommand]
    private Task PickReportLog() => PickReport("log");

    private async Task PickReport(string kind)
    {
        if (ReportAttachmentLoading) return;
        ReportAttachmentLoading = true;
        try
        {
        var paths = await App.FileDialogs.OpenSupportAsync(kind);
        var list = kind == "photo" ? reportPhotos : reportLogs;
        foreach (var path in paths)
        {
            if (list.Count >= 3)
            {
                ReportNote = kind == "photo" ? "Можно приложить не больше трёх фотографий." : "Можно приложить не больше трёх логов.";
                break;
            }
            var info = new FileInfo(path);
            var ext = info.Extension.ToLowerInvariant();
            if (kind == "photo" && ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            {
                ReportNote = "Нужна фотография JPEG, PNG или WebP.";
                break;
            }
            if (kind == "log" && ext is not (".txt" or ".log"))
            {
                ReportNote = "Лог должен быть текстовым файлом .txt или .log.";
                break;
            }
            if (info.Length <= 0) { ReportNote = "Файл пустой."; break; }
            if (info.Length > (kind == "photo" ? 4 * 1024 * 1024 : 512 * 1024))
            {
                ReportNote = kind == "photo" ? "Фото больше 4 МиБ." : "Лог больше 512 КиБ.";
                break;
            }
            var bytes = await File.ReadAllBytesAsync(path);
            var type = ext switch { ".png" => "image/png", ".webp" => "image/webp", ".jpg" or ".jpeg" => "image/jpeg", _ => "text/plain" };
            var upload = new SupportUpload(kind, info.Name, type, bytes);
            list.Add(upload);
            ReportFiles.Add(new SupportDraftAttachment(Guid.NewGuid(), kind, info.Name, upload));
            reportDraftRevision++;
            ReportNote = "";
        }
        ReportFilesText = string.Join("\n", reportPhotos.Select(file => "Фото: " + file.Name).Concat(reportLogs.Select(file => "Лог: " + file.Name)));
        }
        finally { ReportAttachmentLoading = false; }
    }
    [RelayCommand]
    private void RemoveReportFile(SupportDraftAttachment? file)
    {
        if (file is null || !ReportFiles.Remove(file)) return;
        if (file.Kind == "photo") reportPhotos.Remove(file.Upload);
        else reportLogs.Remove(file.Upload);
        reportDraftRevision++;
        ReportFilesText = string.Join("\n", reportPhotos.Select(item => "Фото: " + item.Name).Concat(reportLogs.Select(item => "Лог: " + item.Name)));
    }

    private static string Shown(SupportLineResponse line)
    {
        var extra = string.Join("\n", line.Attachments.Select(file => file.Kind == "photo" ? "Фото: " + file.Name : "Лог: " + file.Name));
        return extra.Length == 0 ? line.Body : line.Body + "\n" + extra;
    }
    public bool LegacyTransferAvailable => App.Profile.IsGuest;

    /// <summary>The shell's single update state: the card here and the sidebar item show the same check.</summary>
    public UpdateCheckViewModel Updates => _shell.Updates;

    // ---- Appearance ----
    [ObservableProperty] private IList<string> _themeItems;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private bool _compactSidebar;
    [ObservableProperty] private bool _animations;

    public string ThemeSummary => ThemeIndex >= 0 && ThemeIndex < ThemeItems.Count
        ? ThemeItems[ThemeIndex] : T("themeSystem");

    private IList<string> BuildThemeItems() => new[] { T("themeSystem"), T("themeLight"), T("themeDark") };

    partial void OnThemeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ThemeSummary));
        if (_suppress || !CanPublish) return;
        var choice = (ThemeChoice)Math.Clamp(value, 0, 2);
        if (App.Theme is { } theme) theme.Apply(choice);
        else { App.Prefs.Theme = choice; App.Prefs.Save(); }
    }

    partial void OnCompactSidebarChanged(bool value)
    {
        if (!_suppress) _shell.SidebarCollapsed = value;
    }

    partial void OnAnimationsChanged(bool value)
    {
        if (_suppress || !CanPublish) return;
        App.Prefs.Animations = value;
        App.Prefs.Save();
        App.Motion.Refresh(); // App listens and adds/removes Theme/Motion.axaml
    }

    // ---- Schedule ----
    [ObservableProperty] private string _groupName = "—";
    [ObservableProperty] private bool _parityInvert;
    [ObservableProperty] private string _updatedText = "";
    [ObservableProperty] private string _autoCheckText = "";
    [ObservableProperty] private bool _isRefreshing;

    partial void OnParityInvertChanged(bool value)
    {
        if (_suppress) return;
        _ = SaveInvertAsync(value);
    }

    private async Task SaveInvertAsync(bool value)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var ok = await RunAsync(() =>
        {
            var s = App.Db.GetSettings();
            s.ParityInvert = value;
            App.Db.SaveSettings(s);
            App.Homework.RecomputeAllStatuses();
        }, "parity");
        if (ok) _shell.RaiseScheduleChanged();
    }

    [RelayCommand] private Task ChangeGroup() => _shell.OpenGroupPickerCommand.ExecuteAsync(null);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Refresh()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        await _shell.RefreshScheduleAsync(force: true, quiet: false);
        await LoadAsync();
    }

    // ---- Notifications ----
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private string _notifyTime1 = "20:00";
    [ObservableProperty] private string _notifyTime2 = "07:30";
    private string savedNotifyTime1 = "20:00", savedNotifyTime2 = "07:30";
    public bool HasNotifyTimeDraft => NotifyTime1.Trim() != savedNotifyTime1 || NotifyTime2.Trim() != savedNotifyTime2;
    public bool CanSaveTimes => NotificationScheduler.IsValidTime(NotifyTime1) && NotificationScheduler.IsValidTime(NotifyTime2)
        && HasNotifyTimeDraft;
    public string NotifyTimesHint => !NotificationScheduler.IsValidTime(NotifyTime1) || !NotificationScheduler.IsValidTime(NotifyTime2)
        ? "Укажите время в формате чч:мм. Действующие уведомления не изменены."
        : CanSaveTimes ? "Время изменится после сохранения." : "Время сохранено.";

    public string NotificationSummary => NotificationsEnabled
        ? $"{savedNotifyTime1} · {savedNotifyTime2}" : "Выключены";

    partial void OnNotificationsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(NotificationSummary));
        if (_suppress || !CanPublish) return;
        App.Prefs.NotificationsEnabled = value;
        App.Prefs.Save();
    }

    partial void OnNotifyTime1Changed(string value) { OnPropertyChanged(nameof(CanSaveTimes)); OnPropertyChanged(nameof(HasNotifyTimeDraft)); OnPropertyChanged(nameof(NotifyTimesHint)); }
    partial void OnNotifyTime2Changed(string value) { OnPropertyChanged(nameof(CanSaveTimes)); OnPropertyChanged(nameof(HasNotifyTimeDraft)); OnPropertyChanged(nameof(NotifyTimesHint)); }
    [RelayCommand] private void DiscardNotifyTimes() { NotifyTime1 = savedNotifyTime1; NotifyTime2 = savedNotifyTime2; }
    [RelayCommand] private void UseEarlyNotifyPreset() { NotifyTime1 = "19:00"; NotifyTime2 = "07:00"; }
    [RelayCommand] private void UseLateNotifyPreset() { NotifyTime1 = "21:00"; NotifyTime2 = "09:00"; }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SaveTimes()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        if (!NotificationScheduler.IsValidTime(NotifyTime1) || !NotificationScheduler.IsValidTime(NotifyTime2))
        {
            App.Toasts.Warn(T("notifBadTime"));
            return;
        }
        var (t1, t2) = (NotifyTime1.Trim(), NotifyTime2.Trim());
        var ok = await RunAsync(() => { var s = App.Db.GetSettings(); s.NotifyTime1 = t1; s.NotifyTime2 = t2; App.Db.SaveSettings(s); }, "notify times");
        if (ok)
        {
            savedNotifyTime1 = t1; savedNotifyTime2 = t2;
            OnPropertyChanged(nameof(NotificationSummary)); OnPropertyChanged(nameof(CanSaveTimes)); OnPropertyChanged(nameof(HasNotifyTimeDraft)); OnPropertyChanged(nameof(NotifyTimesHint));
            App.Toasts.Ok(T("notifSaved", t1, t2));
        }
    }

    [RelayCommand] private async Task TestNotification(){if(!NotificationPreviewVisible){await PreviewNotification();return;}await App.NotificationScheduler.ShowTestAsync(_clock());}

    // ---- Sync ----
    [ObservableProperty] private Bitmap? _qrImage;
    [ObservableProperty] private bool _qrVisible;
    [ObservableProperty] private string _qrHint = "";
    [ObservableProperty] private bool _lanSync;
    [ObservableProperty] private string _lanAddress = "";
    private bool _qrViaServer; // which of the two hints the visible QR earned

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Export()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || !App.Profile.IsGuest) return;
        var path = await App.FileDialogs.SaveJsonAsync($"vograph-sync-{_clock().ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.json");
        if (path is null || !operation.IsCurrent) return;
        if (await RunAsync(() => App.Sync.ExportToFile(path), "export"))
            App.Toasts.Ok(T("syncExported", Path.GetFileName(path)));
    }

    private sealed record ImportResult(int Overrides, int Homework, int Friends);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Import()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || !App.Profile.IsGuest) return;
        var path = await App.FileDialogs.OpenJsonAsync();
        if (path is null || !operation.IsCurrent) return;
        string json;
        try { json = await File.ReadAllTextAsync(path, Encoding.UTF8, operation.Token); }
        catch (Exception ex)
        {
            App.Log.Error("import read", ex);
            App.Toasts.Error($"{T("errorTitle")}: {ex.Message}");
            return;
        }
        var result = await RunAsync(() =>
        {
            var (o, h, f) = App.Sync.ImportFromJson(json);
            return new ImportResult(o, h, f);
        }, "import");
        if (result is null) return;
        App.Toasts.Ok(T("importOk", result.Overrides, result.Homework, result.Friends));
        // The same path a LAN push takes: recompute the due dates, recompose the sections, refresh the badge.
        await _shell.NotifyImportedAsync();
        await LoadAsync();
    }

    private sealed record QrData(string Path, bool ViaServer);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ToggleQr()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || !App.Profile.IsGuest) return;
        if (QrVisible)
        {
            QrVisible = false;
            QrImage?.Dispose();
            QrImage = null;
            return;
        }
        var qrPath = Path.Combine(App.DataDir, "sync-qr.png");
        var host = await App.LanSync.ResolveHostAsync(); // DNS off the UI thread and outside the Core gate
        var data = await RunAsync(() =>
        {
            var json = App.Sync.ExportToJson();
            var content = SyncService.GenerateQrContent(json, host);
            App.Sync.SaveQrImage(content, qrPath);
            return new QrData(qrPath, content.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        }, "qr");
        if (data is null) return;
        try
        {
            var image = await Task.Run(() => new Bitmap(data.Path));
            if (!operation.IsCurrent) { image.Dispose(); return; }
            QrImage = image;
        }
        catch (Exception ex)
        {
            App.Log.Error("qr image", ex);
            App.Toasts.Error($"{T("errorTitle")}: {ex.Message}");
            return;
        }
        _qrViaServer = data.ViaServer;
        QrHint = T(_qrViaServer ? "syncQrServerHint" : "syncQrHint");
        QrVisible = true;
    }

    partial void OnLanSyncChanged(bool value)
    {
        if (_suppress) return;
        if (!CanPublish || !App.Profile.IsGuest)
        {
            Suppressed(() => LanSync = false);
            return;
        }
        if (value)
        {
            try
            {
                App.LanSync.Start();
            }
            catch (Exception ex)
            {
                App.Log.Error("lan sync start", ex);
                App.Toasts.Error(App.LanSync.StartFailureText(ex));
                _suppress = true;
                LanSync = false;
                _suppress = false;
                return;
            }
            LanAddress = "";
            _ = ShowLanAddressAsync(); // the address needs DNS; it lands a moment after the switch
        }
        else
        {
            App.LanSync.Stop();
            LanAddress = "";
        }
        App.Prefs.LanSync = value;
        App.Prefs.Save();
    }

    /// <summary>«Адрес: …» for the running server. The host name behind it comes from a DNS lookup, so it is resolved
    /// off the UI thread and shown once it is known — the UI thread never waits on a resolver.</summary>
    private async Task ShowLanAddressAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || !App.Profile.IsGuest) return;
        var address = await App.LanSync.ResolveAddressAsync(); // never throws: falls back to the loopback address
        if (operation.IsCurrent && App.LanSync.IsRunning) LanAddress = T("syncLanAddress", address);
    }

    private sealed record SettingsData(Settings Settings, string? GroupName);

    public async Task LoadAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++_version;
        var data = await RunAsync(() =>
        {
            var s = App.Db.GetSettings();
            return new SettingsData(s, string.IsNullOrEmpty(s.MyGroupId) ? null : App.Db.GetGroup(s.MyGroupId)?.Name);
        }, "settings");
        if (data is null || version != _version || !operation.IsCurrent) return;
        _suppress = true;
        GroupName = data.GroupName ?? T("noGroup");
        ParityInvert = data.Settings.ParityInvert;
        ThemeIndex = App.Theme is { } t ? (int)t.Choice : (int)App.Prefs.Theme;
        CompactSidebar = _shell.SidebarCollapsed;
        Animations = App.Prefs.Animations;
        NotificationsEnabled = App.Prefs.NotificationsEnabled;
        savedNotifyTime1 = data.Settings.NotifyTime1 ?? "20:00";
        savedNotifyTime2 = data.Settings.NotifyTime2 ?? "07:30";
        NotifyTime1 = savedNotifyTime1;
        NotifyTime2 = savedNotifyTime2;
        OnPropertyChanged(nameof(NotificationSummary)); OnPropertyChanged(nameof(CanSaveTimes)); OnPropertyChanged(nameof(HasNotifyTimeDraft)); OnPropertyChanged(nameof(NotifyTimesHint));
        LanSync = App.LanSync.IsRunning;
        _suppress = false;
        if (QrVisible) QrHint = T(_qrViaServer ? "syncQrServerHint" : "syncQrHint");
        UpdatedText = T("updatedChip", Stamp(data.Settings.LastFetchedAt));
        AutoCheckText = T("setAutoCheckAt", Stamp(data.Settings.LastAutoCheckAt));
        IsRefreshing = _shell.IsRefreshing;
        // Last, because it awaits: the address is resolved off the UI thread and cached by the server.
        await LoadStudyChoices();
        if (App.LanSync.IsRunning) await ShowLanAddressAsync();
        else LanAddress = "";
    }

    /// <summary>ISO UTC → «06.09 15:00» local, or «ещё не было». UpdatedText reuses the sidebar group card's
    /// "updatedChip" template instead of a second key with the same value; only the argument's own format
    /// (date+time here vs. date-only there) differs.</summary>
    private string Stamp(string? iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at.ToLocalTime().ToString("dd.MM HH:mm", CultureInfo.InvariantCulture)
            : T("setNever");

    // ---- About ----
    public string VersionText => T("setVersion", AppVersion.Tag);
    [RelayCommand] private Task OpenReleases() => App.Launcher.OpenUrlAsync(ReleasesUrl);
    [RelayCommand] private Task OpenTimetableSource() => App.Launcher.OpenUrlAsync(TimetableSourceUrl);
    [RelayCommand] private Task OpenMapsSource() => App.Launcher.OpenUrlAsync(MapsSourceUrl);
    [RelayCommand] private Task OpenDataFolder() => App.Launcher.OpenFolderAsync(App.DataDir);

    private void Relabel()
    {
        _suppress = true;
        ThemeItems = BuildThemeItems();
        _suppress = false;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(VersionText));
        _ = LoadAsync();
    }
}

public sealed record SupportThreadItem(Guid Id, string Subject, IReadOnlyList<SupportLineResponse> Messages)
{
    public string Label => $"{Subject} · {Messages.Count} сообщений";
}

public sealed record SupportDraftAttachment(Guid Id, string Kind, string Name, SupportUpload Upload)
{
    public string Label => (Kind == "photo" ? "Фото: " : "Лог: ") + Name;
}
