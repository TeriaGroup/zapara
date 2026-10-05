using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class StudyImpactUx300Tests : UiTest
{
    private static void InsertSplit(TestDb db)
    {
        for (var index = 0; index < 2; index++)
            db.Services.Db.InsertLesson(new Lesson { GroupId = TestDb.MyGroupId, DayOfWeek = 1, Parity = 0,
                Index = 98 + index, TimeStart = "17:00", TimeEnd = "18:30", SubjectRaw = "пр UX300 ПОДГРУППА",
                SubjectNormalized = ParityService.NormalizeSubject("пр UX300 ПОДГРУППА"),
                TeacherRaw = index == 0 ? "Иванов" : "Петров", ClassroomRaw = index == 0 ? "101;" : "102;" });
    }

    [Fact]
    public async Task Preview_is_readonly_and_stale_snapshot_cannot_apply_the_choice()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        InsertSplit(db);
        var shell = new ShellViewModel(db.Services);
        var vm = new SettingsViewModel(db.Services, shell, () => new DateTime(2026, 9, 14));
        await vm.LoadAsync();
        var row = vm.StudySubgroups.Single(item => item.Title.Contains("UX300 ПОДГРУППА"));
        await row.Options[0].PreviewCommand.ExecuteAsync(null);
        Assert.True(vm.ShowStudyImpact);
        Assert.Contains("исчезнет", vm.StudyImpactText);
        Assert.Empty(db.Services.Db.GetSubgroupChoices(TestDb.MyGroupId));

        db.Services.Db.InsertLesson(new Lesson { GroupId = TestDb.MyGroupId, DayOfWeek = 2, Parity = 0,
            Index = 100, TimeStart = "17:00", TimeEnd = "18:30", SubjectRaw = "НОВАЯ ПАРА",
            SubjectNormalized = "новая пара", TeacherRaw = "Петров", ClassroomRaw = "103;" });
        await vm.ApplyStudyImpactCommand.ExecuteAsync(null);
        Assert.Empty(db.Services.Db.GetSubgroupChoices(TestDb.MyGroupId));
        Assert.Contains("изменились", vm.StudyImpactText);

        await row.Options[0].PreviewCommand.ExecuteAsync(null);
        await vm.ApplyStudyImpactCommand.ExecuteAsync(null);
        Assert.Single(db.Services.Db.GetSubgroupChoices(TestDb.MyGroupId));
    }
}
