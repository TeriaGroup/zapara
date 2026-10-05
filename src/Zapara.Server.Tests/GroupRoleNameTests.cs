using Zapara.Server.Communities;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class GroupRoleNameTests
{
    [Fact]
    public void Headman_role_names_are_short_labels_and_not_official_ranks()
    {
        Assert.Equal("Замстаросты", GroupRoleNames.Clean("  Замстаросты  "));
        Assert.Equal("Домашка", GroupRoleNames.Clean("Домашка"));
        Assert.Null(GroupRoleNames.Clean("Я"));
        Assert.Null(GroupRoleNames.Clean("Староста"));
        Assert.Null(GroupRoleNames.Clean("куратор"));
        Assert.Null(GroupRoleNames.Clean("headman"));
        Assert.Null(GroupRoleNames.Clean("участник"));
    }
}
