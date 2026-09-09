using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Communities;

public sealed partial class CommunitiesViewModel : ViewModelBase
{
    private readonly CommunityHttpClient? client;
    private readonly Func<CancellationToken, Task<string?>>? accessToken;
    private readonly Func<string?>? groupId;
    private readonly HashSet<Guid> pending = [];
    private readonly Action relabel;
    private int version;

    public CommunitiesViewModel(AppServices app, CommunityHttpClient? client = null,
        Func<CancellationToken, Task<string?>>? accessToken = null, Func<string?>? groupId = null) : base(app)
    {
        this.client = client;
        this.accessToken = accessToken;
        this.groupId = groupId;
        NeedAccount = client is null || accessToken is null;
        if (NeedAccount) Status = T("communityNeedAccount");
        relabel = Relabel;
        app.Loc.LanguageChanged += relabel;
    }

    public override void Detach() => App.Loc.LanguageChanged -= relabel;
    public override Task ActivateAsync() => LoadAsync();

    public string Title => T("communityTitle");
    public ObservableCollection<CommunityItemViewModel> Communities { get; } = [];

    [ObservableProperty] private bool needAccount;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private bool isForbidden;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private CommunityItemViewModel? selected;

    internal CommunityClientFailure? LastFailure { get; private set; }

    public async Task LoadAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var round = ++version;
        if (client is null || accessToken is null)
        {
            ShowNeedAccount();
            return;
        }
        IsBusy = true;
        try
        {
            var token = await accessToken(operation.Token);
            if (!operation.IsCurrent || round != version) return;
            if (string.IsNullOrEmpty(token))
            {
                ShowNeedAccount();
                return;
            }
            NeedAccount = false;
            var memberships = await client.ListAsync(token, ct: operation.Token);
            IReadOnlyList<CommunityResponse> catalog = [];
            var group = groupId?.Invoke();
            if (!string.IsNullOrWhiteSpace(group))
                catalog = await client.ListAsync(token, group, operation.Token);
            if (!operation.IsCurrent || round != version) return;
            var rows = Merge(memberships, catalog);
            Communities.Clear();
            Selected = null;
            foreach (var row in rows)
                Communities.Add(new CommunityItemViewModel(row, this, pending.Contains(row.CommunityId) && row.Role is null));
            IsForbidden = false;
            IsEmpty = Communities.Count == 0;
            Status = IsEmpty ? T("communityEmpty") : "";
            OnPropertyChanged(nameof(Title));
        }
        catch (CommunityClientException ex) when (operation.IsCurrent && round == version)
        {
            ApplyFailure(ex);
            if (ex.Failure is CommunityClientFailure.Forbidden)
            {
                Communities.Clear();
                Selected = null;
                IsEmpty = false;
            }
        }
        catch (OperationCanceledException) { }
        finally { if (round == version) IsBusy = false; }
    }

    internal Task SelectAsync(CommunityItemViewModel item) => OpenAsync(item);

    internal async Task OpenAsync(CommunityItemViewModel item)
    {
        Selected = item;
        if (!item.IsMember) return;
        await ExecuteAsync(async (api, token, ct) =>
        {
            var homework = await api.ListHomeworkAsync(token, item.CommunityId, ct);
            var completions = new Dictionary<Guid, CompletionResponse>();
            foreach (var row in homework)
            {
                try { completions[row.HomeworkId] = await api.GetCompletionAsync(token, item.CommunityId, row.HomeworkId, ct); }
                catch (CommunityClientException) { }
            }
            var announcements = await api.ListAnnouncementsAsync(token, item.CommunityId, ct);
            var polls = await api.ListPollsAsync(token, item.CommunityId, ct);
            IReadOnlyList<JoinRequestResponse> joins = [];
            IReadOnlyList<MemberResponse> members = [];
            IReadOnlyList<MemberResponse> staff = [];
            if (item.IsStaff)
            {
                joins = await api.ListJoinRequestsAsync(token, item.CommunityId, ct);
                members = await api.ListMembersAsync(token, item.CommunityId, ct);
                staff = await api.ListStaffAsync(token, item.CommunityId, ct);
            }
            item.Present(homework, completions, announcements, polls, joins, members, staff);
        });
    }

    internal async Task JoinAsync(CommunityItemViewModel item)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || client is null || accessToken is null || !item.CanJoin) return;
        IsBusy = true;
        try
        {
            var token = await accessToken(operation.Token);
            if (!operation.IsCurrent) return;
            if (string.IsNullOrEmpty(token)) { ShowNeedAccount(); return; }
            var result = await client.RequestJoinAsync(token, item.CommunityId, operation.Token);
            if (!operation.IsCurrent) return;
            if (result.Status == "pending") MarkPending(item);
        }
        catch (CommunityClientException ex) when (ex.Failure is CommunityClientFailure.AlreadyRequested)
        {
            if (operation.IsCurrent) MarkPending(item);
        }
        catch (CommunityClientException ex) when (ex.Failure is CommunityClientFailure.AlreadyMember)
        {
            pending.Remove(item.CommunityId);
            if (operation.IsCurrent) await LoadAsync();
        }
        catch (CommunityClientException ex)
        {
            if (operation.IsCurrent) ApplyFailure(ex);
        }
        catch (OperationCanceledException) { }
        finally { IsBusy = false; }
    }

    internal Task ToggleCompletionAsync(CommunityHomeworkItemViewModel homework) =>
        WithSelected(async (api, token, community, ct) =>
        {
            var result = await api.UpsertCompletionAsync(token, community.CommunityId, homework.HomeworkId,
                new CompletionUpsert(!homework.Completed, homework.CompletionRevision), ct);
            homework.Apply(result);
        });

    internal Task UpdateHomeworkAsync(CommunityHomeworkItemViewModel homework) =>
        WithSelected(async (api, token, community, ct) =>
        {
            var updated = await api.UpdateHomeworkAsync(token, community.CommunityId, homework.HomeworkId,
                new HomeworkUpsert(homework.Title, homework.Body, homework.Revision), ct);
            homework.Apply(updated);
        });

    internal Task PublishHomeworkAsync(CommunityItemViewModel item) => ExecuteAsync(async (api, token, ct) =>
    {
        var created = await api.PublishHomeworkAsync(token, item.CommunityId,
            new HomeworkUpsert(item.DraftTitle, item.DraftBody, 0), ct);
        item.Homework.Add(new CommunityHomeworkItemViewModel(created, this, false, 0, item.IsStaff));
        item.DraftTitle = item.DraftBody = "";
    });

    internal Task UpdateAnnouncementAsync(CommunityAnnouncementItemViewModel announcement) =>
        WithSelected(async (api, token, community, ct) =>
        {
            var updated = await api.UpdateAnnouncementAsync(token, community.CommunityId, announcement.AnnouncementId,
                new AnnouncementUpsert(announcement.Title, announcement.Body, announcement.Revision), ct);
            announcement.Apply(updated);
        });

    internal Task PublishAnnouncementAsync(CommunityItemViewModel item) => ExecuteAsync(async (api, token, ct) =>
    {
        var created = await api.PublishAnnouncementAsync(token, item.CommunityId,
            new AnnouncementUpsert(item.DraftTitle, item.DraftBody, 0), ct);
        item.Announcements.Add(new CommunityAnnouncementItemViewModel(created, this, item.IsStaff));
        item.DraftTitle = item.DraftBody = "";
    });

    internal Task PublishPollAsync(CommunityItemViewModel item) => ExecuteAsync(async (api, token, ct) =>
    {
        var created = await api.PublishPollAsync(token, item.CommunityId,
            new PollUpsert(item.DraftQuestion, item.DraftDeadline, [item.DraftOptionA, item.DraftOptionB], 0), ct);
        item.Polls.Add(new CommunityPollItemViewModel(created, this));
        item.DraftQuestion = item.DraftOptionA = item.DraftOptionB = "";
    });

    internal async Task VoteAsync(CommunityPollItemViewModel poll, Guid optionId)
    {
        LastFailure = null;
        var voted = await ExecuteAsync((api, token, ct) =>
            api.VoteAsync(token, Selected!.CommunityId, poll.PollId, new VoteRequest(optionId), ct));
        if (voted || LastFailure is CommunityClientFailure.AlreadyVoted or CommunityClientFailure.PollClosed)
            await ResultsAsync(poll);
    }

    internal Task ResultsAsync(CommunityPollItemViewModel poll) =>
        WithSelected(async (api, token, community, ct) =>
        {
            var results = await api.ResultsAsync(token, community.CommunityId, poll.PollId, ct);
            poll.Apply(results);
        });

    internal Task AcceptAsync(CommunityJoinRequestItemViewModel request) =>
        WithSelected(async (api, token, community, ct) =>
        {
            await api.AcceptJoinAsync(token, community.CommunityId, request.RequestId, ct);
            community.JoinRequests.Remove(request);
        });

    internal Task RejectAsync(CommunityJoinRequestItemViewModel request) =>
        WithSelected(async (api, token, community, ct) =>
        {
            await api.RejectJoinAsync(token, community.CommunityId, request.RequestId, ct);
            community.JoinRequests.Remove(request);
        });

    private Task WithSelected(Func<CommunityHttpClient, string, CommunityItemViewModel, CancellationToken, Task> action)
    {
        var item = Selected;
        return item is null ? Task.CompletedTask : ExecuteAsync((api, token, ct) => action(api, token, item, ct));
    }

    private async Task<bool> ExecuteAsync(Func<CommunityHttpClient, string, CancellationToken, Task> action)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent || client is null || accessToken is null) return false;
        LastFailure = null;
        IsBusy = true;
        try
        {
            var token = await accessToken(operation.Token);
            if (!operation.IsCurrent) return false;
            if (string.IsNullOrEmpty(token)) { ShowNeedAccount(); return false; }
            await action(client, token, operation.Token);
            return operation.IsCurrent;
        }
        catch (CommunityClientException ex)
        {
            LastFailure = ex.Failure;
            if (operation.IsCurrent && ex.Failure is not CommunityClientFailure.AlreadyVoted
                and not CommunityClientFailure.PollClosed)
                ApplyFailure(ex);
            return false;
        }
        catch (ArgumentException) { return false; }
        catch (OperationCanceledException) { return false; }
        finally { IsBusy = false; }
    }

    private void MarkPending(CommunityItemViewModel item)
    {
        pending.Add(item.CommunityId);
        item.SetPending();
        IsForbidden = false;
        Status = T("communityPending");
    }

    private void ShowNeedAccount()
    {
        NeedAccount = true;
        IsEmpty = false;
        IsForbidden = false;
        Status = T("communityNeedAccount");
        Communities.Clear();
        Selected = null;
    }

    private void ApplyFailure(CommunityClientException ex)
    {
        if (ex.Failure is CommunityClientFailure.Forbidden)
        {
            IsForbidden = true;
            Status = T("communityForbidden");
        }
        else if (ex.Failure is CommunityClientFailure.InvalidSession) ShowNeedAccount();
        else Status = ex.Message;
    }

    private void Relabel()
    {
        OnPropertyChanged(nameof(Title));
        if (NeedAccount) Status = T("communityNeedAccount");
        else if (IsForbidden) Status = T("communityForbidden");
        else if (IsEmpty) Status = T("communityEmpty");
        else if (Communities.Any(c => c.IsPending)) Status = T("communityPending");
    }

    private static List<CommunityResponse> Merge(IReadOnlyList<CommunityResponse> memberships,
        IReadOnlyList<CommunityResponse> catalog)
    {
        var rows = memberships.ToList();
        var seen = rows.Select(c => c.CommunityId).ToHashSet();
        foreach (var row in catalog)
            if (seen.Add(row.CommunityId)) rows.Add(row);
        return rows;
    }
}
