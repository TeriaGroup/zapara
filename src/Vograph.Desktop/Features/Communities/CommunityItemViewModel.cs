using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Communities;

public sealed partial class CommunityItemViewModel : ObservableObject
{
    private readonly CommunitiesViewModel owner;

    public CommunityItemViewModel(CommunityResponse row, CommunitiesViewModel owner, bool pending)
    {
        this.owner = owner;
        CommunityId = row.CommunityId;
        name = row.Name;
        description = row.Description;
        role = row.Role;
        isPending = pending && row.Role is null;
        DraftDeadline = DateTimeOffset.UtcNow.AddDays(1);
    }

    public Guid CommunityId { get; }
    public bool CanJoin => Role is null && !IsPending;
    public bool IsMember => Role is "member" or "headman" or "curator";
    public bool IsStaff => Role is "headman" or "curator";

    [ObservableProperty] private string name;
    [ObservableProperty] private string description;
    [ObservableProperty] private string? role;
    [ObservableProperty] private bool isPending;
    [ObservableProperty] private string draftTitle = "";
    [ObservableProperty] private string draftBody = "";
    [ObservableProperty] private string draftQuestion = "";
    [ObservableProperty] private string draftOptionA = "";
    [ObservableProperty] private string draftOptionB = "";
    [ObservableProperty] private DateTimeOffset draftDeadline;

    public ObservableCollection<CommunityHomeworkItemViewModel> Homework { get; } = [];
    public ObservableCollection<CommunityAnnouncementItemViewModel> Announcements { get; } = [];
    public ObservableCollection<CommunityPollItemViewModel> Polls { get; } = [];
    public ObservableCollection<CommunityJoinRequestItemViewModel> JoinRequests { get; } = [];
    public ObservableCollection<CommunityPersonItemViewModel> Members { get; } = [];
    public ObservableCollection<CommunityPersonItemViewModel> Staff { get; } = [];

    partial void OnRoleChanged(string? value) => NotifyFlags();
    partial void OnIsPendingChanged(bool value) => NotifyFlags();
    private void NotifyFlags()
    {
        OnPropertyChanged(nameof(CanJoin));
        OnPropertyChanged(nameof(IsMember));
        OnPropertyChanged(nameof(IsStaff));
    }

    internal void SetPending() => IsPending = true;

    internal void Present(IReadOnlyList<HomeworkResponse> homework, IReadOnlyDictionary<Guid, CompletionResponse> completions,
        IReadOnlyList<AnnouncementResponse> announcements, IReadOnlyList<PollResponse> polls,
        IReadOnlyList<JoinRequestResponse> joins, IReadOnlyList<MemberResponse> members, IReadOnlyList<MemberResponse> staff)
    {
        Homework.Clear();
        foreach (var row in homework)
        {
            completions.TryGetValue(row.HomeworkId, out var done);
            Homework.Add(new CommunityHomeworkItemViewModel(row, owner, done?.Completed == true, done?.Revision ?? 0, IsStaff));
        }
        Announcements.Clear();
        foreach (var row in announcements) Announcements.Add(new CommunityAnnouncementItemViewModel(row, owner, IsStaff));
        Polls.Clear();
        foreach (var row in polls) Polls.Add(new CommunityPollItemViewModel(row, owner));
        JoinRequests.Clear();
        foreach (var row in joins.Where(j => j.Status == "pending"))
            JoinRequests.Add(new CommunityJoinRequestItemViewModel(row, owner));
        Members.Clear();
        foreach (var row in members) Members.Add(new CommunityPersonItemViewModel(row.UserId, row.Role));
        Staff.Clear();
        foreach (var row in staff) Staff.Add(new CommunityPersonItemViewModel(row.UserId, row.Role));
    }

    [RelayCommand] private Task Select() => owner.SelectAsync(this);
    [RelayCommand] private Task Join() => owner.JoinAsync(this);
    [RelayCommand] private Task PublishHomework() => owner.PublishHomeworkAsync(this);
    [RelayCommand] private Task PublishAnnouncement() => owner.PublishAnnouncementAsync(this);
    [RelayCommand] private Task PublishPoll() => owner.PublishPollAsync(this);
}

public sealed partial class CommunityHomeworkItemViewModel : ObservableObject
{
    private readonly CommunitiesViewModel owner;
    public CommunityHomeworkItemViewModel(HomeworkResponse row, CommunitiesViewModel owner, bool completed, long completionRevision, bool canEdit)
    {
        this.owner = owner;
        HomeworkId = row.HomeworkId;
        title = row.Title;
        body = row.Body;
        Revision = row.Revision;
        this.completed = completed;
        CompletionRevision = completionRevision;
        CanEdit = canEdit;
    }

    public Guid HomeworkId { get; }
    public bool CanEdit { get; }
    internal long Revision { get; private set; }
    internal long CompletionRevision { get; private set; }

    [ObservableProperty] private string title;
    [ObservableProperty] private string body;
    [ObservableProperty] private bool completed;

    internal void Apply(HomeworkResponse row)
    {
        Title = row.Title;
        Body = row.Body;
        Revision = row.Revision;
    }

    internal void Apply(CompletionResponse row)
    {
        Completed = row.Completed;
        CompletionRevision = row.Revision;
    }

    [RelayCommand] private Task ToggleCompletion() => owner.ToggleCompletionAsync(this);
    [RelayCommand] private Task Update() => owner.UpdateHomeworkAsync(this);
}

public sealed partial class CommunityAnnouncementItemViewModel : ObservableObject
{
    private readonly CommunitiesViewModel owner;
    public CommunityAnnouncementItemViewModel(AnnouncementResponse row, CommunitiesViewModel owner, bool canEdit)
    {
        this.owner = owner;
        AnnouncementId = row.AnnouncementId;
        title = row.Title;
        body = row.Body;
        Revision = row.Revision;
        CanEdit = canEdit;
    }

    public Guid AnnouncementId { get; }
    public bool CanEdit { get; }
    internal long Revision { get; private set; }
    [ObservableProperty] private string title;
    [ObservableProperty] private string body;

    internal void Apply(AnnouncementResponse row)
    {
        Title = row.Title;
        Body = row.Body;
        Revision = row.Revision;
    }

    [RelayCommand] private Task Update() => owner.UpdateAnnouncementAsync(this);
}

public sealed partial class CommunityPollItemViewModel : ObservableObject
{
    private readonly CommunitiesViewModel owner;
    public CommunityPollItemViewModel(PollResponse row, CommunitiesViewModel owner)
    {
        this.owner = owner;
        PollId = row.PollId;
        Question = row.Question;
        Options = row.Options.OrderBy(o => o.Ordinal).Select(o => new CommunityPollOptionItemViewModel(o, this, owner)).ToList();
    }

    public Guid PollId { get; }
    public string Question { get; }
    public IReadOnlyList<CommunityPollOptionItemViewModel> Options { get; }

    [ObservableProperty] private bool hasResults;
    [ObservableProperty] private string resultsText = "";

    internal void Apply(PollResultsResponse results)
    {
        HasResults = true;
        ResultsText = string.Join(" · ", results.Options.Select(o => $"{o.Label} — {o.Votes}"));
    }

    [RelayCommand] private Task Results() => owner.ResultsAsync(this);
}

public sealed partial class CommunityPollOptionItemViewModel : ObservableObject
{
    private readonly CommunityPollItemViewModel poll;
    private readonly CommunitiesViewModel owner;
    public CommunityPollOptionItemViewModel(PollOptionResponse row, CommunityPollItemViewModel poll, CommunitiesViewModel owner)
    {
        this.poll = poll;
        this.owner = owner;
        OptionId = row.OptionId;
        Label = row.Label;
    }

    public Guid OptionId { get; }
    public string Label { get; }

    [RelayCommand] private Task Vote() => owner.VoteAsync(poll, OptionId);
}

public sealed partial class CommunityJoinRequestItemViewModel : ObservableObject
{
    private readonly CommunitiesViewModel owner;
    public CommunityJoinRequestItemViewModel(JoinRequestResponse row, CommunitiesViewModel owner)
    {
        this.owner = owner;
        RequestId = row.RequestId;
        UserId = row.UserId;
        Status = row.Status;
    }

    public Guid RequestId { get; }
    public Guid UserId { get; }
    public string Status { get; }

    [RelayCommand] private Task Accept() => owner.AcceptAsync(this);
    [RelayCommand] private Task Reject() => owner.RejectAsync(this);
}

public sealed record CommunityPersonItemViewModel(Guid UserId, string Role);
