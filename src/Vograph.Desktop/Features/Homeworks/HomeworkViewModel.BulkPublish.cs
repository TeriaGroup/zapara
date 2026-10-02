using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Communities;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Homeworks;

public sealed partial class HomeworkViewModel
{
    private sealed class PublicationItem(PostponeState before, HomeworkUpsert request)
    {
        public PostponeState Before { get; } = before;
        public HomeworkUpsert Request { get; } = request;
        public bool Attempted { get; set; }
        public bool Confirmed { get; set; }
        public bool Rejected { get; set; }
    }
    private sealed record PublicationContext(string Token, string GroupId, Guid CommunityId, Guid SelfUserId,
        GroupHomeResponse Home, GroupDeskResponse Desk);
    private sealed record PublicationBatch(string Scope, string GroupId, Guid CommunityId, Guid SelfUserId,
        HomeworkAudienceSnapshot Audience, IReadOnlyList<PublicationItem> Items);
    private sealed record PublicationPreviewResult(IReadOnlyList<PublicationItem> Items, int Skipped);
    private PublicationContext? publicationContext;
    private PublicationBatch? publicationBatch;
    public HomeworkAudienceSelection BulkPublishRecipients { get; } = new();
    [ObservableProperty] private bool loadingBulkPublishRecipients;
    [ObservableProperty] private bool bulkPublishBusy;
    [ObservableProperty] private bool showBulkPublishRecipients;
    [ObservableProperty] private bool showBulkPublishPreview;
    [ObservableProperty] private string bulkPublishPreviewText = "";
    [ObservableProperty] private string bulkPublishFeedback = "";
    public bool HasPendingBulkPublication => publicationBatch is { } batch &&
        batch.Scope == CurrentBrowseScope() && batch.Items.Any(item => item.Attempted && !item.Confirmed && !item.Rejected);
    public bool CanStartBulkPublication => publicationBatch is null ||
        publicationBatch.Items.All(item => !item.Attempted);

    private void ClearBulkPublication()
    {
        publicationContext = null; publicationBatch = null; BulkPublishRecipients.Clear();
        BulkPublishRecipients.Enabled = true;
        ShowBulkPublishRecipients = false; ShowBulkPublishPreview = false; BulkPublishPreviewText = ""; BulkPublishFeedback = "";
        OnPropertyChanged(nameof(HasPendingBulkPublication)); OnPropertyChanged(nameof(CanStartBulkPublication));
    }
    private void BulkPublicationSelectionChanged()
    {
        if (publicationBatch is { } batch && batch.Items.All(item => !item.Attempted))
        { publicationBatch = null; ShowBulkPublishPreview = false; }
        OnPropertyChanged(nameof(CanStartBulkPublication));
    }
    private bool PublicationScopeCurrent(string scope) => App.Work.CanPublish && !App.Profile.IsGuest &&
        scope == CurrentBrowseScope() && App.Profile.UserId is not null;

    private async Task<PublicationContext?> FetchPublicationContext(string scope, CancellationToken ct)
    {
        if (!PublicationScopeCurrent(scope) || App.Communities is not { } api || App.CommunityAccess is not { } access) return null;
        var group = App.Settings.MyGroupId;
        if (string.IsNullOrWhiteSpace(group)) return null;
        var token = await access(ct);
        if (string.IsNullOrWhiteSpace(token) || !PublicationScopeCurrent(scope)) return null;
        var members = (await api.ListAsync(token, group, ct)).Where(item => !string.IsNullOrWhiteSpace(item.Role)).ToArray();
        if (members.Length != 1 || !PublicationScopeCurrent(scope)) return null;
        var home = await api.GroupHomeAsync(token, members[0].CommunityId, ct);
        var desk = await api.DeskAsync(token, members[0].CommunityId, ct);
        var self = home.Classmates.Where(person => person.Self).ToArray();
        if (!desk.Capabilities.HomeworkAudience || self.Length != 1 || self[0].UserId != App.Profile.UserId ||
            !PublicationScopeCurrent(scope)) return null;
        return new PublicationContext(token, group, members[0].CommunityId, self[0].UserId, home, desk);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadBulkPublishRecipients()
    {
        if (HasPendingBulkPublication)
        { ShowBulkPublishPreview = true; BulkPublishFeedback = "Есть неподтверждённая отправка. Повторите тот же пакет или оставьте его открытым."; return; }
        if (!BulkMode || bulkSelectedIds.Count == 0)
        { BulkPublishFeedback = "Выберите активные личные задания для публикации."; return; }
        var scope = CurrentBrowseScope();
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        LoadingBulkPublishRecipients = true;
        try
        {
            var context = await FetchPublicationContext(scope, operation.Token);
            if (!operation.IsCurrent || !PublicationScopeCurrent(scope)) return;
            if (context is null)
            { BulkPublishFeedback = "Нужны вход в аккаунт, членство ровно в одной группе и поддержка адресной домашки. Обновите группу и повторите."; return; }
            publicationContext = context;
            BulkPublishRecipients.Load(context.CommunityId, context.Home.GroupName ?? context.Home.Name,
                context.Desk, context.Home.Classmates);
            ShowBulkPublishRecipients = true;
            BulkPublishFeedback = "Выберите получателей. Личные задания останутся у вас, группе отправятся копии без файлов.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is CommunityClientException or InvalidOperationException or HttpRequestException)
        { if (operation.IsCurrent) BulkPublishFeedback = "Не удалось проверить группу и получателей. Повторите загрузку."; }
        finally { if (operation.IsCurrent) LoadingBulkPublishRecipients = false; }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task PreviewBulkPublication()
    {
        if (HasPendingBulkPublication)
        { ShowBulkPublishPreview = true; return; }
        if (!ShowBulkPublishRecipients || publicationContext is not { } context ||
            !PublicationScopeCurrent(CurrentBrowseScope()) || context.GroupId != App.Settings.MyGroupId ||
            !BulkPublishRecipients.Valid)
        { BulkPublishFeedback = BulkPublishRecipients.Valid ? "Сначала обновите получателей группы." : BulkPublishRecipients.Validation; return; }
        var audience = BulkPublishRecipients.Payload;
        var audienceSnapshot = BulkPublishRecipients.Snapshot;
        var selectedRows = Groups.SelectMany(group => group.Items).Where(row => bulkSelectedIds.Contains(row.Entry.Homework.Id))
            .Take(50).Select(row => (row.Entry.Homework.Id, row.Entry.SubjectRaw)).ToArray();
        var scope = CurrentBrowseScope();
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var preview = await RunAsync(() =>
        {
            var items = new List<PublicationItem>(); var skipped = 0;
            foreach (var (id, rawSubject) in selectedRows)
            {
                var current = App.Homework.GetById(id);
                if (current is null || current.Status == "done" ||
                    string.IsNullOrWhiteSpace(rawSubject) || string.IsNullOrWhiteSpace(current.Text))
                { skipped++; continue; }
                try
                {
                    var deadline = current.DueDateComputed is { } due
                        ? new DateTimeOffset(due.Year, due.Month, due.Day, 23, 59, 59, TimeSpan.FromHours(3)).ToUniversalTime() : (DateTimeOffset?)null;
                    var request = new HomeworkUpsert(rawSubject.Trim(), current.Text.Trim(), 0,
                        deadline, null, audience, Guid.NewGuid());
                    items.Add(new PublicationItem(PostponeState.Of(current), request));
                }
                catch (ArgumentException) { skipped++; }
            }
            return new PublicationPreviewResult(items, skipped);
        }, "bulk publication preview");
        if (preview is null || !operation.IsCurrent || !PublicationScopeCurrent(scope) ||
            publicationContext?.CommunityId != context.CommunityId || BulkPublishRecipients.Snapshot != audienceSnapshot ||
            selectedRows.Select(row => row.Id).Order().SequenceEqual(bulkSelectedIds.Order()) == false) return;
        var items = preview.Items; var skipped = preview.Skipped;
        if (items.Count == 0)
        { BulkPublishFeedback = $"Подходящих заданий нет; пропущено: {skipped}. Проверьте название, текст и срок задания.";
            ShowBulkPublishPreview = false; return; }
        publicationBatch = new PublicationBatch(CurrentBrowseScope(), context.GroupId, context.CommunityId,
            context.SelfUserId, audienceSnapshot, items);
        BulkPublishPreviewText = $"Опубликовать {items.Count} личных заданий в группе «{BulkPublishRecipients.GroupLabel}» " +
            $"для: {BulkPublishRecipients.Summary}. Файлы не отправляются.\n" +
            string.Join("\n", items.Select(item => $"• {item.Request.Title}: {item.Request.Body} · " +
                (item.Request.DeadlineAt is { } date ? $"срок {date.ToOffset(TimeSpan.FromHours(3)):dd.MM.yyyy}" : "без срока")));
        BulkPublishFeedback = $"Готово к публикации: {items.Count}; пропущено: {skipped}.";
        ShowBulkPublishPreview = true; ShowBulkPreview = false; CancelPostponePreview();
        OnPropertyChanged(nameof(CanStartBulkPublication));
    }

    [RelayCommand]
    private void HideBulkPublication()
    {
        ShowBulkPublishPreview = false;
        if (publicationBatch?.Items.Any(item => item.Attempted) != true)
        { publicationBatch = null; OnPropertyChanged(nameof(CanStartBulkPublication)); }
    }
    [RelayCommand] private void ReopenBulkPublication() { if (HasPendingBulkPublication) ShowBulkPublishPreview = true; }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ConfirmBulkPublication()
    {
        if (!ShowBulkPublishPreview || publicationBatch is not { } batch || BulkPublishBusy ||
            !PublicationScopeCurrent(batch.Scope)) return;
        if (batch.Items.All(item => !item.Attempted) && batch.Audience != BulkPublishRecipients.Snapshot)
        { publicationBatch = null; ShowBulkPublishPreview = false; BulkPublishFeedback = "Получатели изменились. Проверьте предпросмотр ещё раз."; return; }
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        BulkPublishBusy = true;
        var confirmed = 0; var uncertain = 0; var rejected = 0; var stale = 0;
        try
        {
            var context = await FetchPublicationContext(batch.Scope, operation.Token);
            if (context is null || context.CommunityId != batch.CommunityId || context.SelfUserId != batch.SelfUserId ||
                context.GroupId != batch.GroupId)
            { BulkPublishFeedback = "Группа или аккаунт изменились. Публикация не выполнена."; return; }
            var selection = new HomeworkAudienceSelection();
            selection.Load(context.CommunityId, context.Home.GroupName ?? context.Home.Name,
                context.Desk, context.Home.Classmates);
            selection.Restore(batch.Audience);
            if (!selection.Valid)
            { BulkPublishFeedback = "Состав получателей изменился. Проверьте доступ; ожидающие отправки сохранены."; return; }
            foreach (var item in batch.Items.Where(item => !item.Confirmed && !item.Rejected))
            {
                if (!operation.IsCurrent || !PublicationScopeCurrent(batch.Scope)) break;
                if (!item.Attempted)
                {
                    var current = await RunAsync(() => App.Homework.GetById(item.Before.Id), "bulk publication check");
                    if (!operation.IsCurrent || !PublicationScopeCurrent(batch.Scope)) break;
                    if (!item.Before.Matches(current)) { item.Rejected = true; stale++; continue; }
                }
                try
                {
                    // Mark before dispatch: the server may commit even when the response is lost.
                    item.Attempted = true;
                    BulkPublishRecipients.Enabled = false;
                    await App.Communities!.ShareHomeworkAsync(context.Token, batch.CommunityId, item.Request, operation.Token);
                    item.Confirmed = true; confirmed++;
                }
                catch (CommunityClientException ex) when (ex.Status is 400 or 403 or 404 or 413 or 415 &&
                    ex.Failure is CommunityClientFailure.InvalidRequest or CommunityClientFailure.Forbidden or
                    CommunityClientFailure.NotFound or CommunityClientFailure.PayloadTooLarge)
                { item.Rejected = true; rejected++; }
                catch (Exception ex) when (ex is CommunityClientException or HttpRequestException or OperationCanceledException)
                { uncertain++; }
            }
            if (!operation.IsCurrent || !PublicationScopeCurrent(batch.Scope)) return;
            BulkPublishFeedback = $"Опубликовано: {confirmed}; результат не подтверждён: {uncertain}; отказано: {rejected}; " +
                $"изменились до отправки: {stale}. Повтор отправит только неподтверждённые с теми же данными и ID операции.";
            ShowBulkPublishPreview = batch.Items.Any(item => item.Attempted && !item.Confirmed && !item.Rejected);
            if (batch.Items.Any(item => item.Attempted)) BulkMode = false;
            if (!ShowBulkPublishPreview) { publicationBatch = null; ShowBulkPublishRecipients = false; BulkMode = false; }
            OnPropertyChanged(nameof(HasPendingBulkPublication)); OnPropertyChanged(nameof(CanStartBulkPublication));
            if (confirmed > 0) _ = RefreshSharedTasks();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is CommunityClientException or HttpRequestException or InvalidOperationException)
        { if (operation.IsCurrent) BulkPublishFeedback = "Проверка группы не удалась. Пакет сохранён в этом окне; повторите без изменения задания и получателей."; }
        finally { if (operation.IsCurrent) BulkPublishBusy = false; }
    }
}
