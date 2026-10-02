using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    private string? obligationsScope;
    private DispatcherTimer? obligationsClock;
    private readonly Dictionary<(string Kind, Guid Id), GroupObligationEntry> obligationIndex = [];
    public event Action<string, Guid>? ObligationFocusRequested;
    [ObservableProperty] private IReadOnlyList<GroupObligationChoice> obligationRows = [];
    [ObservableProperty] private DateTime? obligationStartDate = DateTime.Today;
    [ObservableProperty] private bool obligationsNeedMeOnly;
    [ObservableProperty] private string obligationsStatus = "";
    [ObservableProperty] private bool obligationsLoading;
    public IReadOnlyList<GroupObligationChoice> VisibleObligations => ObligationRows.Where(row =>
        (!ObligationsNeedMeOnly || row.Entry.NeedsAction(DateTimeOffset.Now)) &&
        (ObligationStartDate is not { } date || row.Entry.Deadline is null ||
            row.Entry.Deadline.Value.LocalDateTime.Date is var due &&
            (due >= date.Date && due < date.Date.AddDays(28) || row.Entry.NeedsMe && due < date.Date)))
        .ToArray();
    public bool HasObligations => VisibleObligations.Count > 0;
    partial void OnObligationRowsChanged(IReadOnlyList<GroupObligationChoice> value) => NotifyObligations();
    partial void OnObligationStartDateChanged(DateTime? value) => NotifyObligations();
    partial void OnObligationsNeedMeOnlyChanged(bool value) => NotifyObligations();
    private void NotifyObligations()
    {
        foreach (var row in ObligationRows) row.RefreshLabel();
        OnPropertyChanged(nameof(VisibleObligations)); OnPropertyChanged(nameof(HasObligations));
    }
    private void ClearObligations()
    {
        obligationsClock?.Stop(); obligationsClock = null;
        obligationsScope = null; obligationIndex.Clear(); ObligationRows = [];
        ObligationsStatus = ""; ObligationsLoading = false;
    }
    private string CurrentObligationScope(Guid community) => App.Profile.DatabasePath + ":" + App.Settings.MyGroupId + ":" + community;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadGroupObligations()
    {
        if (!HasHome || communityId is not Guid community || Api is null || Access is null || App.Profile.IsGuest) return;
        var group = App.Settings.MyGroupId;
        if (string.IsNullOrWhiteSpace(group)) return;
        var scope = CurrentObligationScope(community);
        var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        ObligationsLoading = true;
        var failures = 0; var successes = 0; var scanned = 0; var totalFormTopics = 0;
        var homeworks = new List<GroupHomeworkCopyResponse>();
        var forms = new List<GroupFormResponse>();
        var ballots = new List<BallotResponse>();
        IReadOnlyList<GroupTopicResponse> topics = [];
        bool Current() => operation.IsCurrent && communityId == community && navigationGeneration == ticket &&
            CurrentObligationScope(community) == scope && !App.Profile.IsGuest;
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || !Current()) return;
            var memberships = await Api.ListAsync(token, group, operation.Token);
            if (!Current()) return;
            if (!memberships.Any(item => item.CommunityId == community && !string.IsNullOrWhiteSpace(item.Role)))
            { ClearObligations(); ObligationsStatus = "Членство в этой учебной группе не подтверждено."; return; }
            try { topics = (await Api.TopicsAsync(token, community, operation.Token)).Topics; successes++; }
            catch (CommunityClientException ex) when (!ReadDenied(ex)) { failures++; }
            if (!Current()) return;
            try { homeworks.AddRange(await Api.ListHomeworkCopiesAsync(token, community, operation.Token)); successes++; }
            catch (CommunityClientException ex) when (!ReadDenied(ex)) { failures++; }
            if (!Current()) return;
            try { ballots.AddRange((await Api.BallotsAsync(token, community, ct: operation.Token)).Ballots); successes++; }
            catch (CommunityClientException ex) when (!ReadDenied(ex)) { failures++; }
            if (!Current()) return;
            var formTopics = topics.Where(topic => !topic.Archived && topic.Supported && topic.Kind == "forms" &&
                topic.TopicId is not null && topic.Permissions.Contains("read")).ToArray();
            totalFormTopics = formTopics.Length;
            foreach (var topic in formTopics.Take(24))
            {
                if (!Current()) return;
                try { forms.AddRange((await Api.FormsAsync(token, community, topic.TopicId!.Value, operation.Token)).Forms); successes++; scanned++; }
                catch (CommunityClientException ex) when (!ReadDenied(ex)) { failures++; }
            }
            if (!Current()) return;
            if (successes == 0)
            { ObligationsStatus = "Источники группы сейчас недоступны. Последний открытый обзор сохранён; повторите загрузку."; return; }
            var entries = GroupObligationPlanner.Merge(homeworks, forms, ballots, topics);
            obligationIndex.Clear(); foreach (var entry in entries) obligationIndex[(entry.Kind, entry.Id)] = entry;
            ObligationRows = entries.Select(entry => new GroupObligationChoice(entry, this)).ToArray();
            obligationsScope = scope;
            obligationsClock?.Stop();
            obligationsClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            obligationsClock.Tick += (_, _) => NotifyObligations();
            obligationsClock.Start();
            ObligationsStatus = $"Загружено: {entries.Count} · тем с анкетами проверено {scanned} из {totalFormTopics} (не более 24) · " +
                $"источников с ошибкой {failures}. " +
                (failures > 0 || totalFormTopics > 24 ? "Обзор может быть неполным." : "Показаны разрешённые данные текущей группы.");
        }
        catch (CommunityClientException ex) when (ReadDenied(ex))
        { if (Current()) { ClearObligations(); ObligationsStatus = "Доступ к группе изменился. Обновите членство."; } }
        catch (AccountClientException) { if (Current()) ClearObligations(); }
        catch (OperationCanceledException) { }
        finally
        {
            if (operation.IsCurrent && communityId == community && CurrentObligationScope(community) == scope)
                ObligationsLoading = false;
        }
    }

    internal async Task OpenObligationAsync(GroupObligationEntry entry)
    {
        if (communityId is not Guid community || Api is null || Access is null ||
            obligationsScope != CurrentObligationScope(community) ||
            !obligationIndex.TryGetValue((entry.Kind, entry.Id), out var current) || current != entry) return;
        var scope = obligationsScope; var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        bool Current() => operation.IsCurrent && communityId == community &&
            obligationsScope == scope && CurrentObligationScope(community) == scope && navigationGeneration == ticket;
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || !Current()) return;
            if (entry.Kind == "homework")
            {
                var refreshed = (await Api.ListHomeworkCopiesAsync(token, community, operation.Token))
                    .FirstOrDefault(row => row.HomeworkId == entry.Id);
                if (!Current()) return;
                if (refreshed is null) { ObligationsStatus = "Задание больше недоступно. Обновите обзор."; return; }
                RequestHomework(refreshed); ApplyRequestedHomework();
            }
            else if (entry.Kind == "form" && entry.TopicId is { } formTopic)
            {
                var exists = (await Api.FormsAsync(token, community, formTopic, operation.Token)).Forms
                    .Any(row => row.FormId == entry.Id);
                if (!exists || !Current()) { if (Current()) ObligationsStatus = "Анкета изменилась. Обновите обзор."; return; }
                var channel = Channels.FirstOrDefault(row => row.TopicId == formTopic && row.Kind == "forms");
                if (channel is null) { ObligationsStatus = "Тема анкеты недоступна. Обновите группу."; return; }
                await OpenChannelAsync(channel);
                if (!operation.IsCurrent || communityId != community ||
                    obligationsScope != scope || CurrentObligationScope(community) != scope ||
                    SelectedChannel?.TopicId != formTopic ||
                    Forms.All(row => row.Form.FormId != entry.Id)) { ObligationsStatus = "Анкета изменилась. Обновите обзор."; return; }
            }
            else if (entry.Kind == "ballot")
            {
                var exists = (await Api.BallotsAsync(token, community, entry.TopicId, operation.Token)).Ballots
                    .Any(row => row.BallotId == entry.Id);
                if (!exists || !Current()) { if (Current()) ObligationsStatus = "Голосование изменилось. Обновите обзор."; return; }
                var channel = entry.TopicId is { } topic
                    ? Channels.FirstOrDefault(row => row.TopicId == topic && row.Kind == "ballots")
                    : Channels.FirstOrDefault(row => row.IsGlobalBallots);
                if (channel is null) { ObligationsStatus = "Доска голосований недоступна. Обновите группу."; return; }
                await OpenChannelAsync(channel);
                if (!operation.IsCurrent || communityId != community ||
                    obligationsScope != scope || CurrentObligationScope(community) != scope ||
                    Ballots.All(row => row.BallotId != entry.Id))
                { ObligationsStatus = "Голосование изменилось. Обновите обзор."; return; }
            }
            else return;
            ObligationFocusRequested?.Invoke(entry.Kind, entry.Id);
        }
        catch (CommunityClientException ex) when (ReadDenied(ex))
        { if (Current()) { ClearObligations(); ObligationsStatus = "Доступ к объекту изменился. Обновите группу."; } }
        catch (CommunityClientException) { if (Current()) ObligationsStatus = "Объект не открылся. Обновите обзор и повторите."; }
        catch (AccountClientException) { if (Current()) ClearObligations(); }
        catch (OperationCanceledException) { }
    }
}

public sealed partial class GroupObligationChoice(GroupObligationEntry entry, GroupViewModel owner) : ObservableObject
{
    public GroupObligationEntry Entry { get; } = entry;
    public string Label => Entry.LabelAt(DateTimeOffset.Now);
    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
    [RelayCommand] private Task Open() => owner.OpenObligationAsync(Entry);
}
