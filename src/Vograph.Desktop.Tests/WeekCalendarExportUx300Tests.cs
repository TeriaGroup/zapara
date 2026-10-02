using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Shell;
using Zapara.Client.Domain;
using Xunit;

namespace Vograph.Desktop.Tests;

public class WeekCalendarExportUx300Tests : UiTest
{
    [Fact]
    public void Projection_uses_actual_date_moscow_offset_and_counts_bad_times()
    {
        WeekDay[] days = [new(1, "Понедельник", new DateTime(2026, 9, 14), false,
        [
            new WeekRow("09:00", "Матан", "Лекция", "493 ГК", "лек ВЫСШ. МАТЕМАТ", "Барт Е.Л.", "10:30"),
            new WeekRow("нет", "Сбой", "", "", TimeEnd: "11:00")
        ])];

        var batch = WeekCalendarEntries.Create(days, "3313");

        var entry = Assert.Single(batch.Entries);
        Assert.Equal(new DateTime(2026, 9, 14, 9, 0, 0), entry.Start.DateTime);
        Assert.Equal(TimeSpan.FromHours(3), entry.Start.Offset);
        Assert.Equal(CalendarExport.CanonicalId("3313", "лек ВЫСШ. МАТЕМАТ", "Барт Е.Л.", ""), entry.Id);
        Assert.Equal(1, batch.SkippedCount);
    }

    [Fact]
    public async Task Export_action_writes_exact_seven_day_snapshot_to_selected_ics_path()
    {
        using var db = TestDb.Create();
        var dialogs = new FakeFileDialogs { SavePath = Path.Combine(db.Dir, "week.ics") };
        db.Services.FileDialogs = dialogs;
        var vm = new WeekViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 14, 8, 0, 0));
        await vm.ReloadAsync();

        await vm.ExportWeekCommand.ExecuteAsync(null);

        Assert.EndsWith(".ics", dialogs.LastSuggestedName);
        var content = await File.ReadAllTextAsync(dialogs.SavePath!);
        Assert.Contains("BEGIN:VCALENDAR", content);
        Assert.Contains("DTSTART:20260914T060000Z", content);
        Assert.Contains("SUMMARY:Матан", content);
    }
}
