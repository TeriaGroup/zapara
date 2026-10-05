using Vograph.Core.Services;
using Vograph.Desktop.Features.Teachers;
using Xunit;

namespace Vograph.Desktop.Tests;

public class TeacherDepartmentUx300Tests
{
    [Fact]
    public void Department_filter_intersects_text_and_my_teachers()
    {
        LecturerInfo[] people =
        [
            new() { Id = "a", Name = "Барт Елена", Kafedra = "Физика" },
            new() { Id = "b", Name = "Барт Иван", Kafedra = "Математика" },
            new() { Id = "c", Name = "Орлова Елена", Kafedra = "Физика" }
        ];
        var index = new TeacherIndex(people, []);

        Assert.Equal(["a"], index.Filter("барт", true, new HashSet<string> { "a", "b" }, "Физика").Select(p => p.Id));
        Assert.Equal(["a", "c"], index.Filter("", false, new HashSet<string>(), "Физика").Select(p => p.Id));
    }
}
