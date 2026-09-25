using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class GroupBallotBrowseTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Search_status_and_deadline_order_use_raw_board_fields_without_mutating_input()
    {
        var first = Row("Когда пара?", "open", Base.AddDays(3), [("Завтра", 1), ("Позже", 2)]);
        var second = Row("Куда идём?", "collecting", Base.AddDays(1), [("ПАРК", 0), ("Кафе", 0)]);
        var third = Row("Как прошла учёба?", "closed", Base.AddDays(1), [("Хорошо", 3), ("Сложно", 1)]);
        GroupBallotRow[] source = [first, second, third];

        Assert.Equal(new[] { first, second, third }, GroupBallotBrowse.Filter(source, "", 0, 0));
        Assert.Same(second, Assert.Single(GroupBallotBrowse.Filter(source, "  парк  ", 0, 0)));
        Assert.Same(third, Assert.Single(GroupBallotBrowse.Filter(source, "УЧЁБ", 3, 0)));
        Assert.Equal(new[] { second, third, first }, GroupBallotBrowse.Filter(source, "", 0, 1));
        Assert.Equal(new[] { first, second, third }, GroupBallotBrowse.Filter(source, "", 0, 2));
        Assert.Equal(source, GroupBallotBrowse.Filter(source, "", -1, -1));
        Assert.Equal(new[] { first, second, third }, source);

        var replacement = Row("Новый вопрос", "open", Base.AddDays(2), [("Да", 0), ("Нет", 0)]);
        Assert.Same(replacement, Assert.Single(GroupBallotBrowse.Filter([replacement], "", 2, 1)));
    }

    [Fact]
    public void Percentages_round_half_up_and_zero_does_not_divide()
    {
        Assert.Equal(0, GroupBallotBrowse.Percent(0, 0));
        Assert.Equal(13, GroupBallotBrowse.Percent(1, 8));
        Assert.Equal(67, GroupBallotBrowse.Percent(2, 3));
        Assert.Equal(100, GroupBallotBrowse.Percent(3, 3));
        Assert.Equal(100, GroupBallotBrowse.Percent(12, 8));
        Assert.Equal(100, GroupBallotBrowse.Percent(int.MaxValue, 1));
        Assert.Equal(0, GroupBallotBrowse.Percent(-1, 8));
    }

    [Fact]
    public void Summary_includes_vote_shares_and_support_threshold_without_voter_names()
    {
        var collecting = Row("Где встречаемся?", "collecting", Base.AddDays(2),
            [("Парк", 0), ("Кафе", 0)], supporters: 2, needed: 3);
        var summary = GroupBallotBrowse.Summary(collecting);
        Assert.Contains("Где встречаемся?", summary);
        Assert.Contains("Сбор поддержки", summary);
        Assert.Contains("2 из 3", summary);
        Assert.Contains("2026", summary);
        Assert.Contains("Парк: 0 голосов · 0% голосов", summary);
        Assert.DoesNotContain("Аня", summary);

        var open = Row("Выбор", "open", Base.AddDays(2), [("Да", 1), ("Нет", 7)]);
        Assert.Contains("Да: 1 голос · 13% голосов", GroupBallotBrowse.Summary(open));
    }

    [Fact]
    public void Urgency_is_a_hint_and_never_changes_server_status()
    {
        var soon = Row("Скоро", "open", Base.AddHours(12), [("А", 0), ("Б", 0)]);
        Assert.Equal("Скоро завершится", GroupBallotBrowse.Urgency(soon.StatusCode, soon.DeadlineAt, Base));
        Assert.Equal("open", soon.StatusCode);
        Assert.Equal("", GroupBallotBrowse.Urgency("closed", Base.AddHours(12), Base));
        Assert.Equal("", GroupBallotBrowse.Urgency("unknown", Base.AddHours(12), Base));
        Assert.Equal("unknown", Row("Новый статус", "unknown", Base.AddHours(12),
            [("А", 0), ("Б", 0)]).Status);
        Assert.Equal("Срок истёк, ожидаем обновление", GroupBallotBrowse.Urgency("open", Base.AddHours(-1), Base));
    }

    private static GroupBallotRow Row(string question, string status, DateTimeOffset deadline,
        (string Label, int Votes)[] options, int supporters = 0, int needed = 3)
    {
        var ballot = new BallotResponse(Guid.NewGuid(), question, "headman", status, deadline, supporters,
            needed, false, options.Select(option => new BallotOptionResponse(Guid.NewGuid(), option.Label,
                option.Votes, false)).ToArray(), "", "");
        return new GroupBallotRow(ballot, false, _ => Task.CompletedTask, (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask);
    }
}
