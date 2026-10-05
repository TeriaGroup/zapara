using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SchedulePlainTextUx300Tests : UiTest
{
    [Fact]
    public void Week_text_uses_real_dates_and_visible_lesson_details()
    {
        WeekDay[] days = [new(1, "Понедельник", new DateTime(2026, 9, 14), false,
            [new WeekRow("09:00", "Матан", "Лекция", "493 ГК", "лек ВЫСШ. МАТЕМАТ", "Барт Е.Л.", "10:30")])];

        var text = WeekShareText.Format(days);

        Assert.Contains("14.09.2026", text);
        Assert.Contains("09:00–10:30", text);
        Assert.Contains("Матан", text);
        Assert.Contains("493 ГК", text);
        Assert.DoesNotContain("3313", text);
    }

    [Fact]
    public void Day_text_omits_internal_fields()
    {
        var text = ScheduleShareText.Format(new DateTime(2026, 9, 14),
            [("09:00", "10:30", "Матан", "493 ГК", "Барт Е.Л.")]);

        Assert.Contains("14.09.2026", text);
        Assert.Contains("Матан", text);
        Assert.Contains("Барт", text);
    }

    [Fact]
    public async Task Copy_actions_use_loaded_real_week_and_selected_day()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var now = new DateTime(2026, 9, 14, 8, 0, 0);
        var week = new WeekViewModel(db.Services, shell, () => now);
        await week.ReloadAsync();
        string? copied = null;
        week.SetClipboardWriter(value => { copied = value; return Task.CompletedTask; });
        await week.CopyWeekCommand.ExecuteAsync(null);
        Assert.Contains("14.09.2026", copied);
        Assert.Contains("20.09.2026", copied);
        Assert.Contains("Матан", copied);

        var day = new ScheduleViewModel(db.Services, shell, () => now);
        await day.InitializeAsync();
        day.SetClipboardWriter(value => { copied = value; return Task.CompletedTask; });
        await day.CopyDayCommand.ExecuteAsync(null);
        Assert.Contains("Матан", copied);
        Assert.DoesNotContain("20.09.2026", copied);
    }
}
