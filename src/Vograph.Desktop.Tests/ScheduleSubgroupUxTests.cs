using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class ScheduleSubgroupUxTests
{
    private static readonly DateTime Monday = new(2026, 9, 14, 8, 0, 0);

    private static void InsertPair(TestDb db, string group)
    {
        for (var index = 0; index < 2; index++)
            db.Services.Db.InsertLesson(new Lesson { GroupId = group, DayOfWeek = 1, Parity = 0,
                Index = 40 + index, TimeStart = "09:00", TimeEnd = "10:35", SubjectRaw = "пр ИН. ЯЗ.",
                SubjectNormalized = "пр ин. яз.", TeacherRaw = index == 0 ? "Иванов И.И." : "Петров П.П.",
                ClassroomRaw = index == 0 ? "101;" : "202;" });
    }

    [Fact]
    public async Task Old_group_option_cannot_write_same_stream_and_option_in_a_new_group()
    {
        using var db = TestDb.Create();
        db.Services.Db.UpsertGroup(new Group { Id = "3314", Name = "Другая группа" });
        InsertPair(db, TestDb.MyGroupId); InsertPair(db, "3314");
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => Monday);
        await vm.InitializeAsync();
        var oldOption = vm.Lessons.First(row => row.HasSubgroup).SubgroupOptions[0];
        var settings = db.Services.Db.GetSettings(); settings.MyGroupId = "3314"; db.Services.Db.SaveSettings(settings);
        shell.RaiseGroupChanged();
        await oldOption.SelectCommand.ExecuteAsync(null);
        Assert.Empty(db.Services.Db.GetSubgroupChoices("3314"));
        vm.Detach(); shell.Detach();
    }

    [Fact]
    public async Task Undo_restores_none_only_while_subgroup_still_matches_the_last_selection()
    {
        using var db = TestDb.Create();
        InsertPair(db, TestDb.MyGroupId);
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => Monday);
        await vm.InitializeAsync();
        var first = vm.Lessons.First(row => row.HasSubgroup).SubgroupOptions[0];
        await first.SelectCommand.ExecuteAsync(null);
        Assert.True(vm.HasSubgroupUndo);
        Assert.Single(db.Services.Db.GetSubgroupChoices(TestDb.MyGroupId));
        await vm.UndoSubgroupCommand.ExecuteAsync(null);
        Assert.Empty(db.Services.Db.GetSubgroupChoices(TestDb.MyGroupId));
        var current = vm.Lessons.First(row => row.HasSubgroup).SubgroupOptions[0];
        await current.SelectCommand.ExecuteAsync(null);
        var stream = Assert.Single(SubgroupRules.Build(db.Services.Db.GetAllLessonsForGroup(TestDb.MyGroupId)).Streams);
        var other = stream.Options.Where(option => option.Id != current.Id)
            .OrderBy(option => option.Id, StringComparer.Ordinal).First();
        db.Services.Db.ToggleSubgroupChoice(TestDb.MyGroupId, stream.Id, other.Id);
        await vm.UndoSubgroupCommand.ExecuteAsync(null);
        Assert.Equal(other.Id, db.Services.Db.GetSubgroupChoices(TestDb.MyGroupId)[stream.Id]);
        vm.Detach(); shell.Detach();
    }

    [Fact]
    public void Undo_ticket_rejects_changed_scope_state_and_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = new SubgroupChoiceUndo("profile:A", "stream", null, "option", now.AddSeconds(5));
        Assert.True(ticket.Allows("profile:A", "option", now.AddSeconds(4)));
        Assert.False(ticket.Allows("profile:B", "option", now));
        Assert.False(ticket.Allows("profile:A", "other", now));
        Assert.False(ticket.Allows("profile:A", "option", now.AddSeconds(5)));
    }

    [Fact]
    public async Task Overlap_notice_names_both_real_pairs_and_excludes_adjacent_times()
    {
        using var db = TestDb.Create();
        db.Services.Db.InsertLesson(new Lesson { GroupId = TestDb.MyGroupId, DayOfWeek = 1, Parity = 1,
            Index = 50, TimeStart = "10:00", TimeEnd = "11:35", SubjectRaw = "лек ФИЗИКА", SubjectNormalized = "лек физика",
            TeacherRaw = "Петров П.П.", ClassroomRaw = "312;" });
        db.Services.Db.InsertLesson(new Lesson { GroupId = TestDb.MyGroupId, DayOfWeek = 1, Parity = 1,
            Index = 51, TimeStart = "11:35", TimeEnd = "12:30", SubjectRaw = "лек ХИМИЯ", SubjectNormalized = "лек химия",
            TeacherRaw = "Иванов И.И.", ClassroomRaw = "313;" });
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => Monday);
        await vm.InitializeAsync();
        Assert.True(vm.HasOverlaps);
        Assert.Contains(vm.Overlaps, conflict => conflict.Caption.Contains("ФИЗИКА", StringComparison.OrdinalIgnoreCase)
            && conflict.Caption.Contains("Матан", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Overlaps, conflict => conflict.Caption.Contains("ХИМИЯ", StringComparison.OrdinalIgnoreCase));
        vm.Detach(); shell.Detach();
    }
}
