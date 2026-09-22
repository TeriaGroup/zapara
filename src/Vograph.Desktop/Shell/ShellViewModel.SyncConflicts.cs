using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Shell;

public sealed partial class ShellViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSyncConflicts), nameof(SyncConflictSummary))]
    [NotifyCanExecuteChangedFor(nameof(ResolveSyncConflictsCommand))]
    private int syncConflictCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveSyncConflictsCommand))]
    private bool syncConflictsBusy;

    public bool HasSyncConflicts => SyncConflictCount > 0;
    public string SyncConflictSummary => $"Конфликты синхронизации: {SyncConflictCount}. Выбрать версии";
    private bool CanOpenSyncConflicts => CanPublish && !IsStopped && HasSyncConflicts && !SyncConflictsBusy && !Dialogs.IsOpen;
    private sealed record ConflictBatch(PrivateSyncDraft[] Items);
    private long conflictReadVersion;

    private void QueueSyncConflictRefresh()
    {
        // This is only an invalidation signal, carrying no captured profile data or mutation.
        // Acquire the current profile lease when the UI actually reads; an unpumped UI
        // notification must not hold the database open during logout/shutdown. Do not
        // inherit the sender's AsyncLocal lease: it is released before this callback runs.
        void Post() => Dispatcher.UIThread.Post(() =>
        {
            if (attached && !IsStopped) _ = RefreshSyncConflictsAsync();
        });
        if (ExecutionContext.IsFlowSuppressed()) Post();
        else { using (ExecutionContext.SuppressFlow()) Post(); }
    }

    private async Task RefreshSyncConflictsAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var version = ++conflictReadVersion;
        var batch = await ReadConflictsAsync();
        if (operation.IsCurrent && version == conflictReadVersion && batch is not null)
            SyncConflictCount = batch.Items.Length;
    }

    private Task<ConflictBatch?> ReadConflictsAsync() => RunAsync(() => new ConflictBatch(App.Outbox.Drafts()
        .GroupBy(d => (d.EntityType, d.EntityId)).Select(g => g.First())
        .OrderBy(d => d.EntityType, StringComparer.Ordinal).ThenBy(d => d.EntityId).ToArray()), "sync conflicts");

    private void ConflictDialogStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DialogHostViewModel.IsOpen)) ResolveSyncConflictsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenSyncConflicts))]
    private async Task ResolveSyncConflictsAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || Dialogs.IsOpen || SyncConflictsBusy) return;
        SyncConflictsBusy = true;
        var seen = new HashSet<(string, Guid)>();
        try
        {
            while (operation.IsCurrent && !Dialogs.IsOpen)
            {
                var batch = await ReadConflictsAsync();
                if (!operation.IsCurrent || batch is null) return;
                // A choice is never trusted as queue state: re-read the committed durable drafts.
                ++conflictReadVersion;
                SyncConflictCount = batch.Items.Length;
                var next = batch.Items.FirstOrDefault();
                if (next is null || !seen.Add((next.EntityType, next.EntityId))) return;
                if (!await ShowSyncConflictAsync(new(next.EntityType, next.EntityId, "Состояние записи не совпало."))) return;
            }
        }
        catch (OperationCanceledException) when (!operation.IsCurrent) { }
        finally
        {
            SyncConflictsBusy = false;
            if (operation.IsCurrent) await RefreshSyncConflictsAsync();
        }
    }
}
