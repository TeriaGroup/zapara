using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Features.Communities;
using Xunit;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class CommunitiesUiTests : UiTest
{
    [Fact]
    public async Task Guest_shows_need_account_and_never_touches_the_network()
    {
        using var guest = new CommunitiesUiHarness(guest: true);
        await guest.Vm.ActivateAsync();
        Assert.True(guest.Vm.NeedAccount);
        Assert.False(guest.Vm.IsEmpty);
        Assert.False(guest.Vm.IsForbidden);
        Assert.Empty(guest.Vm.Communities);
        Assert.Equal("Сообщества", guest.Vm.Title);
        Assert.Equal("Чтобы вступить в сообщество, войдите в аккаунт", guest.Vm.Status);
        Assert.Equal(0, guest.Calls);

        using var withClient = new CommunitiesUiHarness(guestWithClient: true);
        await withClient.Vm.LoadAsync();
        Assert.True(withClient.Vm.NeedAccount);
        Assert.Equal(0, withClient.Calls);
        Assert.Equal("Чтобы вступить в сообщество, войдите в аккаунт", withClient.Vm.Status);
    }

    [Fact]
    public async Task Empty_memberships_show_communityEmpty()
    {
        using var h = new CommunitiesUiHarness();
        await h.Vm.LoadAsync();
        Assert.False(h.Vm.NeedAccount);
        Assert.True(h.Vm.IsEmpty);
        Assert.Empty(h.Vm.Communities);
        Assert.Equal("Сообществ пока нет", h.Vm.Status);
        Assert.Equal(1, h.Calls);
        Assert.Equal(("GET", ""), Assert.Single(h.Requests));
    }

    [Fact]
    public async Task List_forbidden_maps_to_communityForbidden()
    {
        using var h = new CommunitiesUiHarness();
        h.Force = (403, "forbidden");
        await h.Vm.LoadAsync();
        Assert.True(h.Vm.IsForbidden);
        Assert.Equal("Нет доступа к этому сообществу", h.Vm.Status);
        Assert.Empty(h.Vm.Communities);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Member_lists_joins_pending_homework_completion_announcements_vote_and_results()
    {
        using var h = new CommunitiesUiHarness(groupId: "O3313");
        h.Catalog.Add(Catalog);
        await h.Vm.LoadAsync();
        var item = Assert.Single(h.Vm.Communities);
        Assert.True(item.CanJoin);
        Assert.False(item.IsMember);
        Assert.False(item.IsPending);
        Assert.Equal("О3313", item.Name);

        await item.JoinCommand.ExecuteAsync(null);
        Assert.True(item.IsPending);
        Assert.False(item.CanJoin);
        Assert.Equal("Заявка на рассмотрении", h.Vm.Status);
        Assert.Contains(h.Requests, r => r.Method == "POST" && r.Path.EndsWith("/join-requests", StringComparison.Ordinal));

        h.Memberships.Add(Membership);
        h.Homework.Add(Homework);
        h.Announcements.Add(Announcement);
        h.Polls.Add(Poll);
        await h.Vm.LoadAsync();
        var member = Assert.Single(h.Vm.Communities);
        Assert.True(member.IsMember);
        Assert.False(member.IsStaff);
        Assert.False(member.CanJoin);

        await member.SelectCommand.ExecuteAsync(null);
        Assert.Same(member, h.Vm.Selected);
        var hw = Assert.Single(member.Homework);
        Assert.Equal(("ДЗ", "Текст", false), (hw.Title, hw.Body, hw.Completed));
        Assert.Equal("Собрание", Assert.Single(member.Announcements).Title);
        var poll = Assert.Single(member.Polls);
        Assert.Equal("Придете?", poll.Question);
        Assert.False(poll.HasResults);
        Assert.Empty(member.JoinRequests);

        await hw.ToggleCompletionCommand.ExecuteAsync(null);
        Assert.True(hw.Completed);
        Assert.Contains(h.Requests, r => r.Method == "PUT" && r.Path.Contains("/completion", StringComparison.Ordinal));

        await poll.Options[0].VoteCommand.ExecuteAsync(null);
        Assert.True(poll.HasResults);
        Assert.Equal("Да — 1 · Нет — 1", poll.ResultsText);
        Assert.DoesNotContain(UserId.ToString("D"), poll.ResultsText);
        Assert.Contains(h.Requests, r => r.Path.Contains("/votes", StringComparison.Ordinal));
        Assert.Contains(h.Requests, r => r.Path.Contains("/results", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Staff_accepts_rejects_and_publishes_or_updates_content()
    {
        using var h = new CommunitiesUiHarness();
        h.Memberships.Add(new CommunityResponse(CommunityId, "О3313", "Сообщество учебной группы", 1, "headman"));
        h.Joins.Add(Pending);
        h.Homework.Add(Homework);
        h.Announcements.Add(Announcement);
        h.Polls.Add(Poll);
        await h.Vm.LoadAsync();
        var staff = Assert.Single(h.Vm.Communities);
        Assert.True(staff.IsStaff);
        await staff.SelectCommand.ExecuteAsync(null);

        var request = Assert.Single(staff.JoinRequests);
        await request.AcceptCommand.ExecuteAsync(null);
        Assert.Empty(staff.JoinRequests);
        Assert.Contains(h.Requests, r => r.Method == "POST" && r.Path.Contains("/accept", StringComparison.Ordinal));

        h.Joins.Add(new JoinRequestResponse(RequestId, CommunityId, UserId, "pending", CommunityClientTestSupport.Now));
        await staff.SelectCommand.ExecuteAsync(null);
        await Assert.Single(staff.JoinRequests).RejectCommand.ExecuteAsync(null);
        Assert.Contains(h.Requests, r => r.Method == "POST" && r.Path.Contains("/reject", StringComparison.Ordinal));

        staff.DraftTitle = "ДЗ";
        staff.DraftBody = "Текст";
        await staff.PublishHomeworkCommand.ExecuteAsync(null);
        Assert.Contains(h.Requests, r => r.Method == "POST" && r.Path.EndsWith("/homework", StringComparison.Ordinal));
        Assert.Contains(staff.Homework, x => x.Title == "ДЗ");

        var hw = staff.Homework[0];
        hw.Title = "ДЗ+";
        hw.Body = "Текст+";
        await hw.UpdateCommand.ExecuteAsync(null);
        Assert.Contains(h.Requests, r => r.Method == "PUT" && r.Path.Contains("/homework/", StringComparison.Ordinal));

        staff.DraftTitle = "Собрание";
        staff.DraftBody = "Текст";
        await staff.PublishAnnouncementCommand.ExecuteAsync(null);
        Assert.Contains(h.Requests, r => r.Method == "POST" && r.Path.EndsWith("/announcements", StringComparison.Ordinal));
        var announcement = staff.Announcements[0];
        announcement.Title = "Собрание+";
        announcement.Body = "Текст+";
        await announcement.UpdateCommand.ExecuteAsync(null);
        Assert.Contains(h.Requests, r => r.Method == "PUT" && r.Path.Contains("/announcements/", StringComparison.Ordinal));

        staff.DraftQuestion = "Придете?";
        staff.DraftOptionA = "Да";
        staff.DraftOptionB = "Нет";
        staff.DraftDeadline = Deadline;
        await staff.PublishPollCommand.ExecuteAsync(null);
        Assert.Contains(h.Requests, r => r.Method == "POST" && r.Path.EndsWith("/polls", StringComparison.Ordinal));
        Assert.Contains(staff.Polls, p => p.Question == "Придете?");
        Assert.Contains(staff.Members, m => m.Role is "headman" or "curator");
        Assert.Equal(2, staff.Staff.Count);
    }

    [Fact]
    public async Task Member_publish_forbidden_sets_communityForbidden_without_dropping_the_list()
    {
        using var h = new CommunitiesUiHarness();
        h.Memberships.Add(Membership);
        await h.Vm.LoadAsync();
        var member = Assert.Single(h.Vm.Communities);
        await member.SelectCommand.ExecuteAsync(null);
        h.Force = (403, "forbidden");
        member.DraftTitle = "ДЗ";
        member.DraftBody = "Текст";
        await member.PublishHomeworkCommand.ExecuteAsync(null);
        Assert.True(h.Vm.IsForbidden);
        Assert.Equal("Нет доступа к этому сообществу", h.Vm.Status);
        Assert.Single(h.Vm.Communities);
    }

    [AvaloniaFact]
    public async Task Guest_and_member_views_render_russian_copy_without_binding_errors()
    {
        using var guest = new CommunitiesUiHarness(guest: true);
        await guest.Vm.LoadAsync();
        var guestView = new CommunitiesView { DataContext = guest.Vm };
        var guestWindow = new Window { Width = 1280, Height = 800, Content = guestView };
        guestWindow.Show();
        Pump();
        var need = guestWindow.GetVisualDescendants().OfType<EmptyState>().Single(e => e.IsVisible);
        Assert.Equal("Чтобы вступить в сообщество, войдите в аккаунт", need.Title);
        Assert.Equal("Сообщества", guest.Vm.Title);
        AssertNoBindingErrors();
        guestWindow.Close();

        using var member = new CommunitiesUiHarness();
        member.Memberships.Add(Membership);
        member.Homework.Add(Homework);
        member.Announcements.Add(Announcement);
        member.Polls.Add(Poll);
        await member.Vm.LoadAsync();
        await member.Vm.Communities[0].SelectCommand.ExecuteAsync(null);
        var view = new CommunitiesView { DataContext = member.Vm };
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();
        Pump();
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "О3313");
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ДЗ");
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Общая домашка");
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == UserId.ToString("D") && t.IsVisible);
        AssertNoBindingErrors();
        window.Close();
    }
}
