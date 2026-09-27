using System.Globalization;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class PlannerCaptionTests
{
    [Theory]
    [InlineData(null, "Нет данных")]
    [InlineData(0, "Без пар")]
    [InlineData(1, "1 пара")]
    [InlineData(2, "2 пары")]
    [InlineData(4, "4 пары")]
    [InlineData(5, "5 пар")]
    [InlineData(11, "11 пар")]
    [InlineData(21, "21 пара")]
    public void Date_choice_distinguishes_unknown_empty_and_declines_lessons(int? count, string expected)
    {
        using var db = TestDb.Create(false);
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var choice = new PlannerDayChoice(new PlannerDate(new DateTime(2026, 9, 14), count), new DateTime(2026, 9, 14), vm);
            Assert.Equal(expected, choice.Workload);
            Assert.Equal($"14 сентября 2026, {expected}", choice.AccessibleName);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            vm.Detach();
            shell.Detach();
        }
    }
}
