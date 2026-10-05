using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class BallotRulesTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(8, 3)]
    [InlineData(13, 4)]
    [InlineData(20, 5)]
    [InlineData(40, 10)]
    public void Collective_vote_needs_a_quarter_and_at_least_three_in_a_larger_group(int members, int need)
        => Assert.Equal(need, BallotRules.SupportersNeeded(members));

    [Fact]
    public void Huge_group_uses_exact_quarter_without_overflow()
        => Assert.Equal((int)(((long)int.MaxValue + 3) / 4), BallotRules.SupportersNeeded(int.MaxValue));

    [Fact]
    public void Weekly_system_vote_asks_how_the_study_week_felt()
    {
        Assert.Equal("Как прошла учебная неделя?", CommunityValidation.Question(BallotRules.WeekQuestion));
        Assert.Equal(new[] { "Легко", "Обычно", "Тяжело" }, CommunityValidation.Options(BallotRules.WeekOptions.ToArray()));
    }
}
