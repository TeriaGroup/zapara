using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Models;

namespace Vograph.Desktop.Features.Homeworks;

public sealed partial class HomeworkViewModel
{
    private sealed record PostponeState(long Id, string Subject, string Text, DateTime CreatedAt, int Nth,
        DateTime? Due, string Status, DateTime? DoneAt, long Revision)
    {
        public static PostponeState Of(Homework item) => new(item.Id, item.SubjectRawNormalized, item.Text,
            item.CreatedAt, item.TargetNthOccurrence, item.DueDateComputed, item.Status, item.DoneAt, item.Revision);
        public bool Matches(Homework? item) => item is not null && this == Of(item);
    }
    private sealed record PostponeDraft(PostponeState Before, int NextNth, DateTime NextDue, Guid OperationId);
    private sealed record PostponeApplied(PostponeState Before, PostponeState After);
    private sealed record PostponePreviewResult(IReadOnlyList<PostponeDraft> Drafts, int Skipped);
    private sealed record PostponeMutation(IReadOnlyList<PostponeApplied> Applied, IReadOnlyList<PostponeDraft> Failed);
    private sealed record PostponeUndoResult(int Restored, int Skipped);
    private readonly List<PostponeDraft> postponeDrafts = [];
    private readonly List<PostponeDraft> postponeFailed = [];
    private readonly List<PostponeApplied> postponeUndo = [];
    private readonly DispatcherTimer postponeUndoTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private DateTimeOffset postponeUndoExpiresAt;
    private string? postponeScope;
    [ObservableProperty] private bool showPostponePreview;
    [ObservableProperty] private string postponePreviewText = "";
    [ObservableProperty] private string postponeFeedback = "";
    public bool HasPostponeUndo => postponeUndo.Count > 0 && postponeScope == CurrentBrowseScope() &&
        DateTimeOffset.UtcNow < postponeUndoExpiresAt;
    public bool HasPostponeFailures => postponeFailed.Count > 0 && postponeScope == CurrentBrowseScope();

    private void CancelPostponePreview()
    { ShowPostponePreview = false; postponeDrafts.Clear(); PostponePreviewText = ""; }
    private void ClearPostponeState()
    {
        CancelPostponePreview(); postponeFailed.Clear(); postponeUndo.Clear(); postponeScope = null;
        PostponeFeedback = "";
        postponeUndoTimer.Stop(); OnPropertyChanged(nameof(HasPostponeUndo)); OnPropertyChanged(nameof(HasPostponeFailures));
    }
    private void OnPostponeUndoExpired(object? sender, EventArgs e)
    { postponeUndo.Clear(); postponeUndoTimer.Stop(); OnPropertyChanged(nameof(HasPostponeUndo)); }
    private static string DateLabel(DateTime? date) => date is { } value ? value.ToString("dd.MM.yyyy") : "без срока";
    private static string PostponeLines(IEnumerable<PostponeDraft> drafts) => string.Join("\n", drafts.Select(draft =>
        $"• {draft.Before.Text}: {DateLabel(draft.Before.Due)} → {draft.NextDue:dd.MM.yyyy} (пара {draft.Before.Nth} → {draft.NextNth})"));

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RequestBulkPostpone()
    {
        if (!BulkMode || bulkSelectedIds.Count == 0)
        { PostponeFeedback = "Выберите личные активные задания для переноса."; return; }
        var ids = bulkSelectedIds.Take(50).ToArray();
        var scope = CurrentBrowseScope();
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var preview = await RunAsync(() =>
        {
            var drafts = new List<PostponeDraft>(); var skipped = 0;
            foreach (var id in ids)
            {
                var item = App.Homework.GetById(id);
                if (item is null || item.Status == "done" || item.TargetNthOccurrence >= 10 || item.DueDateComputed is null)
                { skipped++; continue; }
                var nextNth = item.TargetNthOccurrence + 1;
                var nextDue = App.Homework.ComputeDueDate(item.SubjectRawNormalized, item.CreatedAt, nextNth);
                if (nextDue is null || nextDue.Value.Date <= item.DueDateComputed.Value.Date)
                { skipped++; continue; }
                drafts.Add(new(PostponeState.Of(item), nextNth, nextDue.Value, Guid.NewGuid()));
            }
            return new PostponePreviewResult(drafts, skipped);
        }, "bulk homework postpone preview");
        if (preview is null || !operation.IsCurrent || CurrentBrowseScope() != scope) return;
        postponeDrafts.Clear(); postponeDrafts.AddRange(preview.Drafts); postponeScope = scope;
        PostponePreviewText = PostponeLines(preview.Drafts);
        ShowPostponePreview = preview.Drafts.Count > 0;
        PostponeFeedback = $"Можно перенести: {preview.Drafts.Count}; пропущено без нового срока: {preview.Skipped}.";
        ShowBulkPreview = false;
    }
    [RelayCommand] private void CancelBulkPostpone() => CancelPostponePreview();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ConfirmBulkPostpone()
    {
        if (!ShowPostponePreview || postponeDrafts.Count == 0 || postponeScope != CurrentBrowseScope()) return;
        var scope = postponeScope;
        var drafts = postponeDrafts.ToArray();
        CancelPostponePreview();
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var result = await RunAsync(() =>
        {
            var applied = new List<PostponeApplied>(); var failed = new List<PostponeDraft>();
            foreach (var draft in drafts)
            {
                if (!operation.IsCurrent || CurrentBrowseScope() != scope) break;
                try
                {
                    var current = App.Homework.GetById(draft.Before.Id);
                    if (!draft.Before.Matches(current) || current!.Status == "done" ||
                        App.Homework.ComputeDueDate(current.SubjectRawNormalized, current.CreatedAt, draft.NextNth)?.Date != draft.NextDue.Date)
                    { failed.Add(draft); continue; }
                    App.Homework.UpdateHomework(current.Id, current.Text, draft.NextNth, draft.OperationId);
                    var after = App.Homework.GetById(current.Id);
                    if (after is null || after.TargetNthOccurrence != draft.NextNth || after.DueDateComputed?.Date != draft.NextDue.Date)
                    { failed.Add(draft); continue; }
                    applied.Add(new(draft.Before, PostponeState.Of(after)));
                }
                catch (Exception) { failed.Add(draft); }
            }
            return new PostponeMutation(applied, failed);
        }, "bulk homework postpone");
        if (result is null || !operation.IsCurrent || CurrentBrowseScope() != scope) return;
        postponeUndo.Clear(); postponeUndo.AddRange(result.Applied);
        postponeFailed.Clear(); postponeFailed.AddRange(result.Failed);
        postponeScope = scope;
        postponeUndoExpiresAt = DateTimeOffset.UtcNow.AddSeconds(10);
        postponeUndoTimer.Stop(); postponeUndoTimer.Tick -= OnPostponeUndoExpired;
        if (postponeUndo.Count > 0) { postponeUndoTimer.Tick += OnPostponeUndoExpired; postponeUndoTimer.Start(); }
        OnPropertyChanged(nameof(HasPostponeUndo)); OnPropertyChanged(nameof(HasPostponeFailures));
        PostponeFeedback = $"Перенесено: {result.Applied.Count}; не удалось: {result.Failed.Count}.";
        BulkMode = false;
        if (result.Applied.Count > 0) await ChangedAsync();
    }

    [RelayCommand] private void RetryFailedPostpone()
    {
        if (!HasPostponeFailures) return;
        postponeDrafts.Clear(); postponeDrafts.AddRange(postponeFailed);
        PostponePreviewText = "Повторить только неуспешные:\n" + PostponeLines(postponeDrafts);
        ShowPostponePreview = true;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task UndoBulkPostpone()
    {
        if (!HasPostponeUndo) return;
        var undo = postponeUndo.ToArray(); var scope = postponeScope;
        postponeUndo.Clear(); postponeUndoTimer.Stop(); OnPropertyChanged(nameof(HasPostponeUndo));
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || scope != CurrentBrowseScope()) return;
        var result = await RunAsync(() =>
        {
            var restored = 0; var skipped = 0;
            foreach (var entry in undo)
            {
                if (!operation.IsCurrent || CurrentBrowseScope() != scope) break;
                try
                {
                    var current = App.Homework.GetById(entry.After.Id);
                    if (!entry.After.Matches(current)) { skipped++; continue; }
                    var oldDueNow = App.Homework.ComputeDueDate(entry.Before.Subject, entry.Before.CreatedAt, entry.Before.Nth);
                    var appliedDueNow = App.Homework.ComputeDueDate(entry.After.Subject, entry.After.CreatedAt, entry.After.Nth);
                    if (oldDueNow?.Date != entry.Before.Due?.Date || appliedDueNow?.Date != entry.After.Due?.Date)
                    { skipped++; continue; }
                    App.Homework.UpdateHomework(entry.Before.Id, entry.Before.Text, entry.Before.Nth, Guid.NewGuid());
                    var verified = App.Homework.GetById(entry.Before.Id);
                    if (verified?.TargetNthOccurrence == entry.Before.Nth && verified.DueDateComputed?.Date == entry.Before.Due?.Date)
                    { restored++; continue; }
                    try { App.Homework.UpdateHomework(entry.After.Id, entry.After.Text, entry.After.Nth, Guid.NewGuid()); }
                    catch (Exception) { }
                    skipped++;
                }
                catch (Exception) { skipped++; }
            }
            return new PostponeUndoResult(restored, skipped);
        }, "bulk homework postpone undo");
        if (result is null || !operation.IsCurrent || scope != CurrentBrowseScope()) return;
        PostponeFeedback = $"Вернуто прежних сроков: {result.Restored}; пропущено после других изменений: {result.Skipped}.";
        if (result.Restored > 0) await ChangedAsync();
    }
}
