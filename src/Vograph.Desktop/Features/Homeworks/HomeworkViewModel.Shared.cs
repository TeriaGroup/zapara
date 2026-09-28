using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Shell;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Homeworks;

public sealed partial class HomeworkViewModel
{
    public ObservableCollection<SharedHomeworkListRow> SharedTasks { get; } = [];
    private string? sharedGroupScope;
    private int sharedRequestSerial, sharedMutationSerial;
    [ObservableProperty] private bool sharedLoading;
    [ObservableProperty] private bool sharedLoaded;
    [ObservableProperty] private bool sharedBusy;
    [ObservableProperty] private string sharedFeedback = "";
    [ObservableProperty] private int sharedFilter;
    public IReadOnlyList<string> SharedFilters { get; } = ["Все задания", "Активные", "Готово у меня"];
    public bool ShowSharedTasks => HasGroup && !App.Profile.IsGuest;
    public IReadOnlyList<SharedHomeworkListRow> VisibleSharedTasks => SharedTasks.Where(row =>
        (SharedFilter == 0 || row.Item.Completed == (SharedFilter == 2)) &&
        (SubjectFilter.Length == 0 || ParityService.NormalizeSubject(row.Item.Title) == ParityService.NormalizeSubject(SubjectFilter))).ToArray();
    public bool SharedEmpty => SharedLoaded && !SharedLoading && VisibleSharedTasks.Count == 0;
    public string SharedSummary => $"Заданий группы: {SharedTasks.Count} · готово у вас: {SharedTasks.Count(row => row.Item.Completed)}";
    partial void OnSharedFilterChanged(int value) => NotifySharedTasks();
    partial void OnSharedLoadedChanged(bool value) => NotifySharedTasks();
    partial void OnSharedLoadingChanged(bool value) => NotifySharedTasks();
    private void NotifySharedTasks()
    {
        foreach (var name in new[] { nameof(ShowSharedTasks), nameof(VisibleSharedTasks), nameof(SharedEmpty), nameof(SharedSummary) }) OnPropertyChanged(name);
    }
    private void ResetSharedScope()
    {
        var group = App.Settings.MyGroupId;
        if (sharedGroupScope == group && !App.Profile.IsGuest) return;
        sharedGroupScope = group; sharedRequestSerial++; sharedMutationSerial++;
        SharedTasks.Clear(); SharedLoaded = false; SharedLoading = false; SharedBusy = false; SharedFeedback = "";
        NotifySharedTasks();
    }
    private bool SharedScopeCurrent(string? group) => App.Work.CanPublish && sharedGroupScope == group && App.Settings.MyGroupId == group && !App.Profile.IsGuest;

    [RelayCommand]
    private Task RefreshSharedTasks() => LoadSharedTasksAsync();
    private async Task<bool> LoadSharedTasksAsync()
    {
        ResetSharedScope();
        if (!ShowSharedTasks || App.Communities is not { } api || App.CommunityAccess is not { } access) return false;
        var group = sharedGroupScope;
        var serial = ++sharedRequestSerial;
        SharedLoading = true; SharedFeedback = "";
        using var operation = App.Work.Enter();
        try
        {
            var token = await access(operation.Token);
            if (string.IsNullOrWhiteSpace(token)) { if (SharedScopeCurrent(group)) { SharedTasks.Clear(); SharedFeedback = "Войдите в аккаунт, чтобы получить задания группы."; } return false; }
            var memberships = await api.ListAsync(token, group, operation.Token);
            var rows = new List<SharedHomeworkListRow>();
            foreach (var member in memberships.Where(member => member.Role is not null))
            {
                if (!operation.IsCurrent || !SharedScopeCurrent(group) || serial != sharedRequestSerial) return false;
                var copies = await api.ListHomeworkCopiesAsync(token, member.CommunityId, operation.Token);
                rows.AddRange(copies.Select(item => MakeSharedRow(member.CommunityId, member.Name, item)));
            }
            if (!operation.IsCurrent || !SharedScopeCurrent(group) || serial != sharedRequestSerial) return false;
            SharedTasks.Clear(); foreach (var row in rows) SharedTasks.Add(row);
            SharedLoaded = true; NotifySharedTasks(); return true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is CommunityClientException or AccountClientException)
        {
            if (operation.IsCurrent && SharedScopeCurrent(group) && serial == sharedRequestSerial)
            {
                if (ex is AccountClientException || ex is CommunityClientException { Failure: CommunityClientFailure.InvalidSession or CommunityClientFailure.Forbidden or CommunityClientFailure.NotFound })
                { SharedTasks.Clear(); SharedLoaded = false; }
                SharedFeedback = "Задания группы не загрузились. Повторите обновление.";
                NotifySharedTasks();
            }
        }
        finally { if (operation.IsCurrent && SharedScopeCurrent(group) && serial == sharedRequestSerial) SharedLoading = false; }
        return false;
    }
    private SharedHomeworkListRow MakeSharedRow(Guid community, string name, GroupHomeworkCopyResponse item) =>
        new(community, name, item, ToggleSharedTask, row =>
        {
            if (!SharedTasks.Contains(row)) return;
            var group = _shell.Section<Features.Groups.GroupViewModel>(SectionKey.Group);
            group.RequestCommunity(row.Community); group.RequestHomework(row.Item); _shell.NavigateTo(SectionKey.Group);
        });
    private async Task ToggleSharedTask(SharedHomeworkListRow row)
    {
        if (SharedBusy || !row.Item.CanComplete || !SharedTasks.Contains(row) || App.Communities is not { } api || App.CommunityAccess is not { } access) return;
        var group = sharedGroupScope; var mutation = ++sharedMutationSerial;
        sharedRequestSerial++; SharedLoading = false; SharedBusy = true; SharedFeedback = "";
        using var operation = App.Work.Enter();
        try
        {
            var token = await access(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || !operation.IsCurrent || !SharedScopeCurrent(group)) return;
            var changed = await api.UpsertCompletionAsync(token, row.Community, row.Item.HomeworkId,
                new(!row.Item.Completed, row.Item.CompletionRevision), operation.Token);
            if (!operation.IsCurrent || !SharedScopeCurrent(group) || mutation != sharedMutationSerial) return;
            sharedRequestSerial++; SharedLoading = false;
            var at = SharedTasks.ToList().FindIndex(current => current.Community == row.Community && current.Item.HomeworkId == row.Item.HomeworkId);
            if (at >= 0)
            {
                var current = SharedTasks[at]; var item = current.Item;
                SharedTasks[at] = MakeSharedRow(current.Community, current.GroupName, new(item.HomeworkId, item.Title, item.Body, item.Revision,
                    changed.Completed, changed.Revision, item.DeadlineAt, item.TopicId, item.Audience, item.CanEdit, item.CanComplete));
            }
            SharedFeedback = "Ваша отметка сохранена. У остальных участников она не изменилась."; NotifySharedTasks();
        }
        catch (CommunityClientException ex) when (ex.Failure == CommunityClientFailure.RevisionConflict)
        {
            if (operation.IsCurrent && SharedScopeCurrent(group) && mutation == sharedMutationSerial)
            { var refreshed = await LoadSharedTasksAsync(); if (refreshed && SharedScopeCurrent(group) && mutation == sharedMutationSerial) SharedFeedback = "Показана актуальная отметка с другого устройства. При необходимости измените её ещё раз."; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is CommunityClientException or AccountClientException)
        {
            if (operation.IsCurrent && SharedScopeCurrent(group) && mutation == sharedMutationSerial)
            {
                if (ex is AccountClientException || ex is CommunityClientException { Failure: CommunityClientFailure.InvalidSession })
                { SharedTasks.Clear(); SharedLoaded = false; }
                else if (ex is CommunityClientException { Failure: CommunityClientFailure.Forbidden or CommunityClientFailure.NotFound })
                { foreach (var old in SharedTasks.Where(old => old.Community == row.Community && old.Item.HomeworkId == row.Item.HomeworkId).ToArray()) SharedTasks.Remove(old); }
                SharedFeedback = "Не удалось сохранить отметку. Проверьте доступ и повторите обновление."; NotifySharedTasks();
            }
        }
        finally { if (operation.IsCurrent && SharedScopeCurrent(group) && mutation == sharedMutationSerial) SharedBusy = false; }
    }
}

public sealed class SharedHomeworkListRow
{
    public Guid Community { get; }
    public string GroupName { get; }
    public GroupHomeworkCopyResponse Item { get; }
    public string AudienceLabel => Item.Audience.Kind == "all" ? "Вся группа" : $"Подгрупп: {Item.Audience.RoleIds.Count} · участников отдельно: {Item.Audience.UserIds.Count}";
    public string Deadline => Item.DeadlineAt is { } date ? $"Срок: {date.LocalDateTime:dd.MM.yyyy HH:mm}" : "Без срока";
    public string CompleteLabel => Item.Completed ? "Готово у меня · снять отметку" : "Отметить готово у меня";
    public string OwnershipHint => Item.CanComplete ? "Отметка относится только к вам" : "Задание назначено другим участникам";
    public IAsyncRelayCommand ToggleCommand { get; }
    public IRelayCommand OpenCommand { get; }
    public SharedHomeworkListRow(Guid community, string groupName, GroupHomeworkCopyResponse item,
        Func<SharedHomeworkListRow, Task> toggle, Action<SharedHomeworkListRow> open)
    {
        Community = community; GroupName = groupName; Item = item;
        ToggleCommand = new AsyncRelayCommand(() => toggle(this), () => Item.CanComplete);
        OpenCommand = new RelayCommand(() => open(this));
    }
}
