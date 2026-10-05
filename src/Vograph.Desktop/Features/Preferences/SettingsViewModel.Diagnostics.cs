using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Preferences;

public sealed partial class SettingsViewModel
{
    private sealed record DiagnosticCounts(int Ready, int Total, int Pending);
    private Func<string, Task>? diagnosticsClipboardWriter;
    private string? diagnosticsScope;
    public void SetDiagnosticsClipboardWriter(Func<string, Task>? writer) => diagnosticsClipboardWriter = writer;
    [ObservableProperty] private string diagnosticsPreview = "";
    [ObservableProperty] private string diagnosticsStatus = "";
    public bool HasDiagnosticsPreview => DiagnosticsPreview.Length > 0;
    partial void OnDiagnosticsPreviewChanged(string value) => OnPropertyChanged(nameof(HasDiagnosticsPreview));
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task PreviewDiagnostics()
    {
        var scope = App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var counts = await RunAsync(() =>
        {
            var groups = App.Db.GetAllGroups();
            var cache = new TimetableApiCache(App.Db);
            var ready = groups.Count(group => group.LastFetchedAt is not null ||
                App.Db.GetAllLessonsForGroup(group.Id).Count > 0 || cache.Read(group.Id)?.FetchedAt is not null);
            return new DiagnosticCounts(ready, groups.Count, App.Outbox.Pending().Count);
        }, "safe support diagnostics");
        if (!operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId) return;
        try
        {
            var maps = await Task.Run(() => App.MapFiles.CacheStatus(), operation.Token);
            if (!operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId) return;
            DiagnosticsPreview = new SupportDiagnostics(AppVersion.Tag, counts.Ready, counts.Total,
                maps.Cached, maps.Total, counts.Pending).Format();
            diagnosticsScope = scope;
            DiagnosticsStatus = "Проверьте текст. Копирование выполняется только по отдельному нажатию; обращение не отправляется автоматически.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (operation.IsCurrent) DiagnosticsStatus = "Диагностику не удалось собрать. Личные данные не копировались."; }
    }
    [RelayCommand]
    private async Task CopyDiagnostics()
    {
        if (!HasDiagnosticsPreview || diagnosticsScope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId)
        { DiagnosticsPreview = ""; DiagnosticsStatus = "Профиль или группа изменились. Сформируйте текст заново."; return; }
        try
        {
            if (diagnosticsClipboardWriter is null) throw new InvalidOperationException("Clipboard unavailable");
            await diagnosticsClipboardWriter(DiagnosticsPreview);
            DiagnosticsStatus = "Безопасная сводка скопирована. Вставьте её в обращение только при желании.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { DiagnosticsStatus = "Не удалось скопировать сводку."; }
    }
    private void ClearDiagnostics()
    { DiagnosticsPreview = ""; DiagnosticsStatus = ""; diagnosticsScope = null; }
}
