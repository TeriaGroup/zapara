using Vograph.Desktop.Features.Summary;
using Vograph.Desktop.Features.Teachers;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SummaryDrilldownUx300Tests : UiTest
{
    [Fact]
    public async Task Subject_and_type_counts_open_exact_contributing_lessons()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new SummaryViewModel(db.Services, shell, () => new DateTime(2026, 9, 14, 8, 0, 0));
        await vm.ReloadAsync();
        var subject = vm.Subjects.Single(row => row.Name == "Матан");
        vm.OpenSummarySubjectCommand.Execute(subject);
        Assert.Equal(subject.Count, vm.SummaryMatches.Count);
        var first = vm.SummaryMatches[0].Entry;
        vm.SummaryMatches[0].OpenCommand.Execute(null);
        Assert.Equal(SectionKey.Schedule, shell.CurrentKey);
        await Waits.Until(() => shell.Section<Vograph.Desktop.Features.Schedule.ScheduleViewModel>(SectionKey.Schedule).Date == first.Date,
            "summary exact lesson date");
        var type = vm.Types.Single(row => row.Name == "Лекция");
        vm.OpenSummaryTypeCommand.Execute(type);
        Assert.Equal(type.Count, vm.SummaryMatches.Count);
    }

    [Fact]
    public async Task Teacher_name_navigation_and_hidden_selection_restore_context()
    {
        using var db = TestDb.Create();
        await db.Services.Lecturers.LoadXmlAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "sample-lecturers.xml")));
        var vm = new TeachersViewModel(db.Services, new ShellViewModel(db.Services),
            () => new DateTime(2026, 9, 14), allowNetwork: false);
        await vm.OpenByNameAsync("Барт Е.Л.");
        Assert.Equal("Барт Е.Л.", vm.Selected?.Info.Name);
        vm.Query = "несуществующий";
        Assert.True(vm.HasHiddenSelection);
        vm.ShowSelectedTeacherCommand.Execute(null);
        Assert.Equal("Барт Е.Л.", vm.Selected?.Info.Name);
    }

    [Fact]
    public void Long_group_list_expands_and_restores_short_form()
    {
        var row = new TeacherRow("09:00", "10:30", "Математика", "лекция", "493",
            "3313, 3314, 3315, 3316 +1", "нечёт", true, FullGroups: "3313, 3314, 3315, 3316, 3317");
        Assert.True(row.HasMoreGroups);
        row.ToggleGroupsCommand.Execute(null);
        Assert.Contains("3317", row.ShownGroups);
        row.ToggleGroupsCommand.Execute(null);
        Assert.DoesNotContain("3317", row.ShownGroups);
    }
}
