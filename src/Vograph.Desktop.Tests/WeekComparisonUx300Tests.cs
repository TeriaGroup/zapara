using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class WeekComparisonUx300Tests
{
    [Fact]
    public void Compare_uses_raw_multiset_and_reports_added_removed_slots()
    {
        var original = new WeekDay(1, "Понедельник", new DateTime(2026, 9, 14), false,
        [
            new("09:00", "Новая вывеска", "Лекция", "Кабинет", "math", "Петров", "10:30", "101", "lecture"),
            new("09:00", "Новая вывеска", "Лекция", "Кабинет", "math", "Петров", "10:30", "101", "lecture")
        ]);
        var compared = original with { Date = original.Date.AddDays(7), Rows =
        [
            new("09:00", "Переименовано", "Лекция", "Кабинет", "math", "Петров", "10:30", "101", "lecture"),
            new("10:40", "Физика", "Практика", "Другая", "physics", "Иванов", "12:10", "202", "practice")
        ] };

        var changes = WeekComparison.Compare([original], [compared]);

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, change => change.Kind == "Убрано" && change.Row.SubjectRaw == "math");
        Assert.Contains(changes, change => change.Kind == "Добавлено" && change.Row.SubjectRaw == "physics");
    }

    [Fact]
    public async Task Selected_actual_week_compares_to_second_actual_week_without_replacing_it()
    {
        using var db = TestDb.Create();
        var vm = new WeekViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 14));
        await vm.ReloadAsync();
        var current = vm.Days[0].Date;
        vm.CompareWeekDate = current.AddDays(7);

        await vm.CompareWeeksCommand.ExecuteAsync(null);

        Assert.Equal(current, vm.Days[0].Date);
        Assert.True(vm.ShowWeekComparison);
        Assert.Contains("Сравнение", vm.ComparisonText);
    }

    [Fact]
    public async Task Preperiod_week_is_unknown_instead_of_reporting_all_lessons_removed()
    {
        using var db = TestDb.Create();
        var vm = new WeekViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 14));
        await vm.ReloadAsync();
        vm.CompareWeekDate = new DateTime(2026, 8, 24);

        await vm.CompareWeeksCommand.ExecuteAsync(null);

        Assert.Contains("неизвестно", vm.ComparisonText);
        Assert.DoesNotContain("убрано", vm.ComparisonText);
    }
}
