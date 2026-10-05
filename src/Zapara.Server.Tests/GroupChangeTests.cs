using Zapara.Server.Communities;
using Xunit;

namespace Zapara.Server.Tests;

public sealed class GroupChangeTests
{
    private static readonly Guid Role = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Person = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Binding_vote_passes_only_when_yes_leads_and_meets_the_threshold()
    {
        Assert.True(GroupChanges.Passes(5, 4, 5));
        Assert.True(GroupChanges.Passes(3, 1, 3));
        Assert.False(GroupChanges.Passes(5, 5, 5));
        Assert.False(GroupChanges.Passes(2, 0, 3));
        Assert.False(GroupChanges.Passes(0, 0, 1));
    }

    [Fact]
    public void Power_change_roundtrips_and_names_the_capability()
    {
        var payload = GroupChanges.Canonical("power", Role, Guid.Empty, "", "joins", true);
        var change = Assert.IsType<GroupChange>(GroupChanges.Read(payload));
        Assert.Equal("Дать роли «Домашка» возможность «Принимать заявки»?", GroupChanges.Question(change, "Домашка", ""));
        Assert.Equal("Забрать у роли «Домашка» возможность «Принимать заявки»?", GroupChanges.Question(GroupChanges.Read(GroupChanges.Canonical("power", Role, Guid.Empty, " ", "joins", false))!, "Домашка", ""));
        Assert.Null(GroupChanges.Canonical("power", Role, Guid.Empty, "", "admin", true));
    }

    [Fact]
    public void Role_and_membership_changes_keep_official_ranks_out()
    {
        var created = GroupChanges.Read(GroupChanges.Canonical("create_role", Guid.Empty, Guid.Empty, " Домашка ", "", false));
        Assert.Equal("Создать роль «Домашка»?", GroupChanges.Question(created!, "", ""));
        Assert.Null(GroupChanges.Canonical("create_role", Guid.Empty, Guid.Empty, "Староста", "", false));
        Assert.Null(GroupChanges.Canonical("rename_role", Role, Guid.Empty, "куратор", "", false));
        Assert.Equal("Исключить Ивана из группы?", GroupChanges.Question(GroupChanges.Read(GroupChanges.Canonical("remove_member", Guid.Empty, Person, "", "", false))!, "", "Ивана"));
        Assert.Null(GroupChanges.Canonical("remove_member", Guid.Empty, Guid.Empty, "", "", false));
        Assert.Null(GroupChanges.Canonical("grant", Role, Guid.Empty, "", "", false));
        Assert.Equal("Назначить Ивана роль «Домашка»?", GroupChanges.Question(GroupChanges.Read(GroupChanges.Canonical("grant", Role, Person, "", "", false))!, "Домашка", "Ивана"));
    }
}
