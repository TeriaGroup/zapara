using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkCalendarExportUx300Tests : UiTest
{
    [Fact]
    public async Task Visible_personal_deadline_exports_as_all_day_event()
    {
        using var db = TestDb.Create();
        var due = Assert.Single(db.Services.Homework.GetAll()).DueDateComputed!.Value;
        var dialogs = new FakeFileDialogs { SavePath = Path.Combine(db.Dir, "deadlines.ics") };
        db.Services.FileDialogs = dialogs;
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => new DateTime(2026, 9, 6));
        await vm.LoadAsync();

        await vm.ExportDeadlinesCommand.ExecuteAsync(null);

        var content = await File.ReadAllTextAsync(dialogs.SavePath!);
        Assert.Contains($"DTSTART;VALUE=DATE:{due:yyyyMMdd}", content);
        Assert.Contains("TRANSP:TRANSPARENT", content);
        Assert.EndsWith(".ics", dialogs.LastSuggestedName);
    }
}
