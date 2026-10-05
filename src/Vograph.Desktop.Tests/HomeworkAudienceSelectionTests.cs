using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class HomeworkAudienceSelectionTests
{
    private static readonly Guid Community = Guid.NewGuid(), Role = Guid.NewGuid(), One = Guid.NewGuid(), Two = Guid.NewGuid();
    private static ClassmateResponse[] People => [new(One, "member_one", "Первый", "member", false), new(Two, "member_two", "Второй", "member", false)];
    private static GroupDeskResponse Desk(bool supported = true, bool includeRole = true) => new(false,
        includeRole ? [new(Role, "Подгруппа 1")] : [], includeRole ? [new(Role, One)] : [], [], [], [], new(HomeworkAudience: supported));

    [Fact]
    public void Selected_roles_and_people_form_a_union_without_duplicate_recipients()
    {
        var selection = new HomeworkAudienceSelection();
        selection.Load(Community, "Н162С", Desk(), People);
        selection.Mode = 1;
        selection.Roles[0].Selected = true;
        selection.People.Single(person => person.Id == One).Selected = true;
        Assert.True(selection.Valid);
        Assert.Equal(1, selection.RecipientCount);
        Assert.Equal(new[] { Role }, selection.Payload!.RoleIds);
        Assert.Equal(new[] { One }, selection.Payload.UserIds);
    }

    [Fact]
    public void Empty_selection_and_unsupported_targeting_cannot_fall_back_to_everyone()
    {
        var selection = new HomeworkAudienceSelection();
        selection.Load(Community, "Н162С", Desk(false), People);
        Assert.Null(selection.Payload);
        selection.Mode = 1;
        selection.People[0].Selected = true;
        Assert.False(selection.Valid);
        Assert.Throws<InvalidOperationException>(() => selection.Payload);
        selection.Load(Community, "Н162С", Desk(), People);
        selection.ClearSelectionCommand.Execute(null);
        Assert.False(selection.Valid);
        Assert.Throws<InvalidOperationException>(() => selection.Payload);
    }

    [Fact]
    public void Restored_targeted_task_is_not_widened_when_recipient_metadata_arrives()
    {
        var selection = new HomeworkAudienceSelection();
        selection.Restore(new HomeworkAudience("selected", [Role], [Two]));
        selection.Load(Community, "Н162С", Desk(), People);
        Assert.True(selection.Selecting);
        Assert.True(selection.Valid);
        Assert.Equal(2, selection.RecipientCount);
        Assert.True(selection.Roles[0].Selected);
        Assert.True(selection.People.Single(person => person.Id == Two).Selected);
    }

    [Fact]
    public void Removed_role_invalidates_selection_instead_of_silently_dropping_it()
    {
        var selection = new HomeworkAudienceSelection();
        selection.Load(Community, "Н162С", Desk(), People);
        selection.Mode = 1; selection.Roles[0].Selected = true;
        var snapshot = selection.Snapshot;
        selection.Load(Community, "Н162С", Desk(includeRole: false), People);
        Assert.False(selection.Valid);
        Assert.Equal(snapshot, selection.Snapshot);
    }

    [Fact]
    public void Search_does_not_change_delivery_and_changing_community_clears_old_targets()
    {
        var selection = new HomeworkAudienceSelection();
        selection.Load(Community, "Н162С", Desk(), People);
        selection.Mode = 1; selection.People[0].Selected = true;
        var snapshot = selection.Snapshot;
        selection.Query = "несуществующий";
        Assert.True(selection.NoMatches);
        Assert.Equal(snapshot, selection.Snapshot);
        selection.Load(Guid.NewGuid(), "Другая группа", Desk(), People);
        Assert.True(selection.Selecting);
        Assert.False(selection.Valid);
        Assert.Empty(selection.Snapshot.Users);
    }

    [Fact]
    public async Task A_manager_outside_the_audience_cannot_mark_the_task_complete()
    {
        var calls = 0;
        var item = new GroupHomeworkCopyResponse(Guid.NewGuid(), "Матан", "Решить", 1, false, 0,
            audience: new("selected", [], [One]), canEdit: true, canComplete: false);
        var row = new SpaceHomeworkRow(item, true, _ => { calls++; return Task.CompletedTask; }, _ => { }, true);
        Assert.False(row.Writable);
        Assert.False(row.ToggleCommand.CanExecute(null));
        Assert.True(row.Editable);
        if (row.ToggleCommand.CanExecute(null)) await row.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(0, calls);
    }
}
