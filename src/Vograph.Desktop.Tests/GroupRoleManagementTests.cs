using Avalonia.Headless.XUnit;
using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class GroupRoleManagementTests
{
    private static readonly Guid Actor = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Person = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid High = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Low = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static GroupDeskResponse Desk(bool headman = false, string[]? rolePowers = null, int lowerPosition = 1,
        GroupGrantResponse[]? extra = null) => new(headman,
        [new(High, "Управляющий", 5), new(Low, "Подгруппа", lowerPosition)],
        new[] { new GroupGrantResponse(High, Actor) }.Concat(extra ?? []).ToArray(), [],
        (rolePowers ?? []).Select(power => new GroupPowerResponse(Low, power)).ToArray(), ["read", "post", "roles", "grants"]);

    [Fact]
    public void Delegation_rejects_own_equal_and_unowned_power_roles()
    {
        Assert.Empty(GroupRoleManagement.RoleReason(Desk(), Actor, Low, "grants"));
        Assert.NotEmpty(GroupRoleManagement.RoleReason(Desk(), Actor, High, "grants"));
        Assert.NotEmpty(GroupRoleManagement.RoleReason(Desk(lowerPosition: 5), Actor, Low, "grants"));
        Assert.NotEmpty(GroupRoleManagement.RoleReason(Desk(rolePowers: ["homework"]), Actor, Low, "grants"));
        Assert.NotEmpty(GroupRoleManagement.RoleReason(Desk(), Actor, Low, "roles", newPosition: 5));
        Assert.NotEmpty(GroupRoleManagement.RoleReason(Desk(), Actor, Low, "roles", addedPower: "homework"));
        Assert.Empty(GroupRoleManagement.RoleReason(Desk(headman: true, rolePowers: ["homework"]), Actor, Low, "roles", newPosition: 9));
    }

    [Theory]
    [InlineData("headman", false)]
    [InlineData("curator", false)]
    [InlineData("member", true)]
    public void Protected_official_status_is_checked_before_assignment(string officialRole, bool allowed)
    {
        var member = new ClassmateResponse(Person, "boris", "Борис", officialRole, false);
        Assert.Equal(allowed, GroupRoleManagement.MemberReason(Desk(), Actor, Low, member, removing: false).Length == 0);
        Assert.Empty(GroupRoleManagement.MemberReason(Desk(headman: true), Actor, Low, member, removing: false));
    }

    [Fact]
    public void Assignment_cap_blocks_only_addition_and_self_cannot_be_managed_by_delegate()
    {
        var person = new ClassmateResponse(Person, "boris", "Борис", "member", false);
        var capped = Desk(headman: true, extra: [new(Low, Person), new(Guid.NewGuid(), Person), new(Guid.NewGuid(), Person)]);
        Assert.NotEmpty(GroupRoleManagement.MemberReason(capped, Actor, High, person, removing: false));
        Assert.Empty(GroupRoleManagement.MemberReason(capped, Actor, Low, person, removing: true));
        var self = new ClassmateResponse(Actor, "anya", "Аня", "member", true);
        Assert.NotEmpty(GroupRoleManagement.MemberReason(Desk(), Actor, Low, self, removing: false));
    }

    [AvaloniaFact]
    public async Task Selected_role_members_search_and_preview_busy_gates_prevent_requests()
    {
        using var fixture = new GroupSpaceViewModelTests.Fixture("chat", "chat");
        await fixture.Vm.ActivateAsync();
        Assert.True(fixture.Vm.HasTrustedRole);
        fixture.Vm.RoleMemberSearch = "Борис";
        Assert.Single(fixture.Vm.RoleMembers);
        fixture.Vm.RoleAssignedOnly = true;
        Assert.Empty(fixture.Vm.RoleMembers);
        fixture.Vm.RoleAssignedOnly = false;
        var writes = fixture.Writes;
        fixture.Vm.IsBusy = true;
        Assert.False(fixture.Vm.CanCreateRole); Assert.False(fixture.Vm.CanGrantTrusted);
        Assert.False(Assert.Single(fixture.Vm.RoleMembers).CanAct);
        await fixture.Vm.CreateTrustedRoleCommand.ExecuteAsync(null);
        await Assert.Single(fixture.Vm.RoleMembers).ActionCommand.ExecuteAsync(null);
        Assert.Equal(writes, fixture.Writes);
        fixture.Vm.IsBusy = false; fixture.Vm.PreviewMode = true;
        await fixture.Vm.SaveRoleSettingsCommand.ExecuteAsync(null);
        Assert.Equal(writes, fixture.Writes);
    }
}
