using Vograph.Core.Services;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Vograph.Timetable;
using Xunit;

namespace Vograph.Desktop.Tests;

public class VoenmehRefreshTests
{
    [Fact]
    public void Needed_names_are_selected_group_and_enabled_friends()
    {
        using var db = TestDb.Create();
        Assert.Equal(new[] { "А863С", "09С31" }, ParserService.NeededGroupNames(db.Services.Db));
    }

    [Fact]
    public void RefreshParsed_remaps_incoming_name_ids_onto_existing_group_ids()
    {
        using var db = TestDb.Create();
        var parsed = VoenmehScheduleParser.Assemble(
            VoenmehScheduleParser.ParseMeta(VoenmehHttp.Meta),
            new[] { ("А863С", VoenmehScheduleParser.ParseLessons(VoenmehHttp.PhilosophyLessons, "А863С")) });

        db.Services.Parser.RefreshParsed(parsed);

        Assert.Equal("3313", db.Services.Db.GetGroupByName("А863С")!.Id);
        Assert.Contains(db.Services.Db.GetAllLessonsForGroup("3313"), l => l.SubjectRaw == "лек ФИЛОСОФИЯ");
        Assert.Contains(db.Services.Db.GetAllLessonsForGroup("3031"), l => l.SubjectRaw.Contains("ФИЗИКА"));
    }

    [Fact]
    public void RefreshParsed_catalog_only_does_not_clear_lessons()
    {
        using var db = TestDb.Create();
        var parsed = VoenmehScheduleParser.Assemble(
            VoenmehScheduleParser.ParseMeta(VoenmehHttp.Meta),
            Array.Empty<(string, List<Vograph.Core.Models.Lesson>)>());

        db.Services.Parser.RefreshParsed(parsed);

        Assert.NotEmpty(db.Services.Db.GetAllLessonsForGroup("3313"));
        Assert.Equal("3313", db.Services.Db.GetSettings().MyGroupId);
    }

    [Fact]
    public void RefreshParsed_keeps_overrides_and_homework_across_json_titles()
    {
        using var db = TestDb.Create();
        var parsed = VoenmehScheduleParser.Assemble(
            VoenmehScheduleParser.ParseMeta(VoenmehHttp.Meta),
            new[] { ("А863С", VoenmehScheduleParser.ParseLessons(VoenmehHttp.MathJsonLessons, "А863С")) });

        db.Services.Parser.RefreshParsed(parsed);

        Assert.Equal("Матан", db.Services.Overrides.GetDisplayName("лек ВЫСШ. МАТ.", 1));
        Assert.Single(db.Services.Homework.GetForSubject("лек ВЫСШ. МАТ."));
        Assert.Equal("3313", Assert.Single(db.Services.Db.GetAllLessonsForGroup("3313")).GroupId);
    }

    [Fact]
    public async Task Picking_a_group_without_lessons_fetches_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        using var services = AppServices.Create(dir);
        services.AllowNetwork = true;
        services.Parser.RefreshParsed(VoenmehScheduleParser.Assemble(
            VoenmehScheduleParser.ParseMeta(VoenmehHttp.Meta),
            Array.Empty<(string, List<Vograph.Core.Models.Lesson>)>()));
        var handler = VoenmehHttp.Handler();
        services.Refresher = new ScheduleRefresher(handler);
        var shell = new ShellViewModel(services);
        await shell.StartAsync(allowNetwork: false);

        var task = shell.OpenGroupPickerCommand.ExecuteAsync(null);
        var dlg = await Waits.ForDialogAsync<GroupPickerDialogViewModel>(shell);
        dlg.Selected = dlg.Filtered.Single(g => g.Name == "А863С");
        dlg.ConfirmCommand.Execute(null);
        await task;

        Assert.Contains(handler.Requests, r => r.RequestUri!.Query.Contains(Uri.EscapeDataString("А863С"), StringComparison.Ordinal));
        var id = services.Db.GetSettings().MyGroupId;
        Assert.Contains(services.Db.GetAllLessonsForGroup(id!), l => l.SubjectRaw == "лек ФИЛОСОФИЯ");
    }
}
