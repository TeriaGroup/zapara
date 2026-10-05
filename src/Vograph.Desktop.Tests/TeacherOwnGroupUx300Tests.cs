using Vograph.Core.Services;
using Vograph.Desktop.Features.Teachers;
using Xunit;

namespace Vograph.Desktop.Tests;

public class TeacherOwnGroupUx300Tests
{
    [Fact]
    public void Detail_can_limit_lessons_to_own_group_without_losing_all_group_schedule()
    {
        using var db = TestDb.Create();
        var info = new LecturerInfo { Id = "teacher", Name = "Барт Е.Л." };
        LecturerLesson[] lessons =
        [
            new() { LecturerId = "teacher", DayOfWeek = 1, Parity = 0, TimeStart = "09:00", TimeEnd = "10:30", SubjectRaw = "Матан", Groups = [new GroupRef { IdGroup = TestDb.MyGroupId, Number = "3313" }] },
            new() { LecturerId = "teacher", DayOfWeek = 1, Parity = 0, TimeStart = "12:40", TimeEnd = "14:10", SubjectRaw = "Физика", Groups = [new GroupRef { IdGroup = "other", Number = "О311" }] }
        ];
        var detail = new TeacherDetailViewModel(info, lessons, true, TestDb.MyGroupId, "3313", false,
            db.Services.Loc, new DateTime(2026, 9, 14));
        Assert.Equal(2, detail.Days[0].Rows.Count);

        detail.OnlyOwnGroup = true;
        Assert.Single(detail.Days[0].Rows);
        detail.OnlyOwnGroup = false;
        Assert.Equal(2, detail.Days[0].Rows.Count);
    }
}
