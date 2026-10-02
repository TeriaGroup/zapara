using Vograph.Desktop.Features.Schedule;
using Vograph.Core.Models;
using Vograph.Desktop.Features.Teachers;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleContextUx300Tests : UiTest
{
    [Fact]
    public async Task Each_overlap_side_targets_its_own_lesson_even_with_same_subject()
    {
        using var db = TestDb.Create();
        db.Services.Db.InsertLesson(new Lesson { GroupId = TestDb.MyGroupId, DayOfWeek = 1, Parity = 0,
            Index = 99, TimeStart = "09:15", TimeEnd = "10:45", SubjectRaw = "Пр другая пара",
            SubjectNormalized = "другая пара", TeacherRaw = "Петров", ClassroomRaw = "100" });
        var vm = new ScheduleViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 14, 8, 0, 0));
        await vm.InitializeAsync();
        var overlap = Assert.Single(vm.Overlaps, overlap => overlap.First.TimeStart == "09:00" && overlap.Second.TimeStart == "09:15");
        LessonRowViewModel? focused = null; vm.LessonFocusRequested += row => focused = row;
        vm.OpenOverlapSecondCommand.Execute(overlap);
        Assert.Same(overlap.Second, focused);
        vm.OpenOverlapFirstCommand.Execute(overlap);
        Assert.Same(overlap.First, focused);
    }

    [Fact]
    public async Task Unique_directory_teacher_opens_exact_card_from_lesson()
    {
        using var db = TestDb.Create();
        await db.Services.Lecturers.LoadXmlAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "sample-lecturers.xml")));
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => new DateTime(2026, 9, 14, 8, 0, 0));
        await vm.InitializeAsync();
        var row = vm.Lessons.FirstOrDefault(item => item.CanOpenTeacher);
        Assert.NotNull(row);
        await row!.OpenTeacherCommand.ExecuteAsync(null);
        Assert.Equal(SectionKey.Teachers, shell.CurrentKey);
        Assert.NotNull(shell.Section<TeachersViewModel>(SectionKey.Teachers).Selected);
    }
}
