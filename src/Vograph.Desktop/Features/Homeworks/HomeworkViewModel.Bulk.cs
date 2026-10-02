using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;

namespace Vograph.Desktop.Features.Homeworks;

public sealed partial class HomeworkViewModel
{
    private readonly HashSet<long> bulkSelectedIds = [];
    private readonly DispatcherTimer bulkUndoTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly List<HomeworkCompletionUndo> bulkUndo = [];
    private sealed record BulkMutation(IReadOnlyList<(Homework Before, Homework After)> Changed, int Failed);
    [ObservableProperty] private bool bulkMode;
    [ObservableProperty] private bool showBulkPreview;
    [ObservableProperty] private string bulkPreviewText = "";
    [ObservableProperty] private string bulkFeedback = "";
    public int BulkSelectedCount => bulkSelectedIds.Count;
    public bool HasBulkUndo => bulkUndo.Count > 0 && bulkUndo[0].Scope == CurrentBrowseScope() &&
        DateTimeOffset.UtcNow < bulkUndo[0].ExpiresAt;
    internal bool IsBulkSelected(long id) => bulkSelectedIds.Contains(id);
    internal void SetBulkSelection(HomeworkRowViewModel row, bool selected)
    {
        if (!BulkMode || row.IsDone || !Groups.SelectMany(group => group.Items).Contains(row)) return;
        if (selected && bulkSelectedIds.Count >= 50 && !bulkSelectedIds.Contains(row.Entry.Homework.Id))
        { row.SelectedForBulk = false; BulkFeedback = "За один раз можно выбрать до 50 личных заданий."; return; }
        if (selected) bulkSelectedIds.Add(row.Entry.Homework.Id);
        else bulkSelectedIds.Remove(row.Entry.Homework.Id);
        ShowBulkPreview = false;
        CancelPostponePreview();
        if (publicationBatch?.Items.Any(item => item.Attempted) != true)
        { publicationBatch = null; ShowBulkPublishPreview = false; }
        OnPropertyChanged(nameof(BulkSelectedCount));
    }
    internal void RefreshBulkSelection()
    {
        var visible = Groups.SelectMany(group => group.Items).Where(row => !row.IsDone)
            .Select(row => row.Entry.Homework.Id).ToHashSet();
        bulkSelectedIds.RemoveWhere(id => !visible.Contains(id));
        OnPropertyChanged(nameof(BulkSelectedCount));
        if (ShowBulkPreview) RequestBulkCompletion();
        if (ShowPostponePreview) CancelPostponePreview();
    }
    partial void OnBulkModeChanged(bool value)
    {
        foreach (var row in Groups.SelectMany(group => group.Items)) row.RefreshBulkSelectionVisibility();
        if (value) return;
        bulkSelectedIds.Clear();
        foreach (var row in Groups.SelectMany(group => group.Items)) row.SelectedForBulk = false;
        ShowBulkPreview = false; BulkPreviewText = "";
        CancelPostponePreview();
        OnPropertyChanged(nameof(BulkSelectedCount));
    }
    [RelayCommand] private void ToggleBulkMode() => BulkMode = !BulkMode;
    [RelayCommand]
    private void RequestBulkCompletion()
    {
        CancelPostponePreview();
        var rows = Groups.SelectMany(group => group.Items)
            .Where(row => !row.IsDone && bulkSelectedIds.Contains(row.Entry.Homework.Id)).ToArray();
        if (!BulkMode || rows.Length == 0)
        { BulkFeedback = "Выберите личные задания, которые нужно отметить готовыми."; ShowBulkPreview = false; return; }
        BulkPreviewText = "Отметить готовыми только у вас:\n" + string.Join("\n", rows.Select(row =>
            $"• {row.Subject}: {row.Text}"));
        ShowBulkPreview = true; BulkFeedback = "";
    }
    [RelayCommand] private void CancelBulkCompletion() => ShowBulkPreview = false;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ConfirmBulkCompletion()
    {
        if (!BulkMode || !ShowBulkPreview || bulkSelectedIds.Count == 0) return;
        var scope = CurrentBrowseScope();
        var ids = bulkSelectedIds.ToArray();
        ShowBulkPreview = false;
        ClearCompletionUndo(); ClearBulkUndo();
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var outcome = await RunAsync(() =>
        {
            var changed = new List<(Homework Before, Homework After)>();
            var failed = 0;
            foreach (var id in ids)
            {
                if (!operation.IsCurrent || CurrentBrowseScope() != scope) break;
                try
                {
                    var before = App.Homework.GetById(id);
                    if (before is null || before.Status == "done") { failed++; continue; }
                    App.Homework.MarkDone(id, true);
                    var after = App.Homework.GetById(id);
                    if (after is null || after.Status != "done") { failed++; continue; }
                    changed.Add((before, after));
                }
                catch (Exception) { failed++; }
            }
            return new BulkMutation(changed, failed);
        }, "bulk personal homework");
        if (outcome is null || !operation.IsCurrent || CurrentBrowseScope() != scope) return;
        var now = DateTimeOffset.UtcNow;
        bulkUndo.AddRange(outcome.Changed.Select(pair => HomeworkCompletionUndo.Create(pair.Before, pair.After, scope, now)));
        if (bulkUndo.Count > 0)
        { bulkUndoTimer.Stop(); bulkUndoTimer.Tick -= OnBulkUndoExpired; bulkUndoTimer.Tick += OnBulkUndoExpired; bulkUndoTimer.Start(); }
        OnPropertyChanged(nameof(HasBulkUndo));
        BulkFeedback = $"Отмечено готово: {outcome.Changed.Count}; не удалось: {outcome.Failed}.";
        BulkMode = false;
        if (outcome.Changed.Count > 0) await ChangedAsync();
    }
    private void OnBulkUndoExpired(object? sender, EventArgs e) => ClearBulkUndo();
    private void ClearBulkUndo()
    { bulkUndoTimer.Stop(); bulkUndo.Clear(); OnPropertyChanged(nameof(HasBulkUndo)); }
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task UndoBulkCompletion()
    {
        var undo = bulkUndo.ToArray();
        ClearBulkUndo();
        if (undo.Length == 0 || undo[0].Scope != CurrentBrowseScope()) return;
        var scope = CurrentBrowseScope();
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var result = await RunAsync(() =>
        {
            var restored = 0; var skipped = 0;
            foreach (var item in undo)
            {
                if (!operation.IsCurrent || CurrentBrowseScope() != scope) break;
                try
                {
                    var current = App.Homework.GetById(item.Id);
                    if (!item.Allows(current, scope, DateTimeOffset.UtcNow)) { skipped++; continue; }
                    App.Homework.MarkDone(item.Id, item.BeforeDone); restored++;
                }
                catch (Exception) { skipped++; }
            }
            return new BulkUndoResult(restored, skipped);
        }, "bulk personal homework undo");
        if (result is null || !operation.IsCurrent || CurrentBrowseScope() != scope) return;
        BulkFeedback = $"Отменено отметок: {result.Restored}; пропущено после других изменений: {result.Skipped}.";
        if (result.Restored > 0) await ChangedAsync();
    }
    private sealed record BulkUndoResult(int Restored, int Skipped);
}
