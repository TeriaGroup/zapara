using System.Text;
using Microsoft.Data.Sqlite;
using Vograph.Core.Models;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class ParserCompatibilityTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    [Fact]
    public void TT012_Native_fixture_a_exact_values()
    {
        using var owned = new OwnedDatabases();
        var result = new ParserService(owned.Source).Parse(Fixture("timetable-a.xml"));
        Assert.Equal(new DateTime(2026, 9, 1), result.periodStart);
        Assert.Equal(2, result.weekCount);
        Assert.Equal("ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.", result.periodTitle);
        Assert.Equal(new[] { "3313", "9999" }, result.groups.Select(g => g.Id));
        Assert.Equal(new[] { "А863С", "Е452Б" }, result.groups.Select(g => g.Name));
        Assert.All(result.groups, g => Assert.Equal(ParserService.DefaultUrl, g.Url));
        var lesson = Assert.Single(result.lessons);
        Assert.Equal(("3313", 1, 1, 1, "09:00", "10:35"),
            (lesson.GroupId, lesson.DayOfWeek, lesson.Parity, lesson.Index, lesson.TimeStart, lesson.TimeEnd));
        Assert.Equal(("лек Математика", "лек математика", "лек", "Иванов И.И.", "493;", "493", ""),
            (lesson.SubjectRaw, lesson.SubjectNormalized, lesson.TypeRaw, lesson.TeacherRaw, lesson.ClassroomRaw, lesson.RoomRaw, lesson.BuildingRaw));
        Assert.DoesNotContain(result.lessons, l => l.GroupId == "9999");
    }

    [Fact]
    public void TT012_Native_sample_exact_values()
    {
        using var owned = new OwnedDatabases();
        var result = new ParserService(owned.Source).Parse(Fixture("sample-timetable.xml"));
        Assert.Equal(3, result.groups.Count);
        Assert.Equal(9, result.lessons.Count);
        Assert.Equal(new[] { 1, 2, 1, 1, 1, 1, 1, 1, 2 }, result.lessons.Select(l => l.Index));
        Assert.Equal(new[] { 1, 1, 2, 1, 1, 2, 1, 1, 1 }, result.lessons.Select(l => l.Parity));
        Assert.Equal(new[] { 1, 1, 1, 2, 3, 3, 6, 1, 1 }, result.lessons.Select(l => l.DayOfWeek));
        Assert.Equal("лек ВЫСШ. МАТЕМАТ", result.lessons[0].SubjectRaw);
        Assert.Equal("лек высш. математ", result.lessons[0].SubjectNormalized);
        Assert.Equal("Барт Е.Л.", result.lessons[0].TeacherRaw);
        Assert.Equal(("563*;", "563", "main", "12:40", "14:15"),
            (result.lessons[1].ClassroomRaw, result.lessons[1].RoomRaw, result.lessons[1].BuildingRaw, result.lessons[1].TimeStart, result.lessons[1].TimeEnd));
        Assert.Equal(("280", "ВЦ"), (result.lessons[5].RoomRaw, result.lessons[5].BuildingRaw));
        Assert.Equal(("дистанционно", ""), (result.lessons[6].RoomRaw, result.lessons[6].BuildingRaw));
        Assert.DoesNotContain(result.lessons, l => l.GroupId == "9999");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TT012_Native_decode_encodings(bool unicode, bool bom)
    {
        var xml = Fixture("timetable-a.xml");
        var encoding = unicode ? Encoding.Unicode : Encoding.UTF8;
        var bytes = (bom ? encoding.GetPreamble() : Array.Empty<byte>()).Concat(encoding.GetBytes(xml)).ToArray();
        Assert.Equal(xml, ParserService.DecodeXml(bytes));
    }

    [Fact]
    public async Task TT012_Native_characterization_and_data()
    {
        using var owned = new OwnedDatabases();
        var db = owned.Source;
        var parser = new ParserService(db);
        await parser.RefreshAsync(xmlOverride: Fixture("timetable-a.xml"));
        var settings = db.GetSettings();
        settings.MyGroupId = "3313";
        settings.NotifyTime1 = "20:15";
        settings.NotifyTime2 = "07:25";
        settings.ParityInvert = true;
        db.SaveSettings(settings);
        var created = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var overrideId = db.InsertOverride(new Override
        {
            SubjectRawNormalized = "лек математика", Scope = "weekday:1",
            DisplayName = "Моя математика", Note = "Не потерять заметку", CreatedAt = created
        });
        var hw = new HomeworkService(db);
        var homeworkId = hw.AddHomework("лек Математика", "Задачи 1–3", 3, created);
        hw.MarkDone(homeworkId, true);
        var doneAt = Assert.Single(hw.GetAll()).DoneAt;
        Assert.NotNull(doneAt);
        await parser.RefreshAsync(xmlOverride: Fixture("sample-timetable.xml"));
        Assert.Equal(9, db.GetAllLessonsForGroup("3313").Count + db.GetAllLessonsForGroup("3031").Count);
        Assert.Equal(overrideId, Assert.Single(db.GetOverrides()).Id);
        Assert.Equal(homeworkId, Assert.Single(hw.GetAll()).Id);
        AssertPersonalFields(db, created, doneAt);

        var imported = new SyncService(owned.Target).ImportFromJson(new SyncService(db).ExportToJson());
        Assert.Equal((1, 1, 0), imported);
        AssertPersonalFields(owned.Target, created, doneAt);
        var targetSettings = owned.Target.GetSettings();
        Assert.Equal(("3313", "20:15", "07:25", true),
            (targetSettings.MyGroupId, targetSettings.NotifyTime1, targetSettings.NotifyTime2, targetSettings.ParityInvert));
    }

    private static void AssertPersonalFields(Database db, DateTime created, DateTime? doneAt)
    {
        var ov = Assert.Single(db.GetOverrides());
        Assert.Equal(("лек математика", "weekday:1", "Моя математика", "Не потерять заметку", created),
            (ov.SubjectRawNormalized, ov.Scope, ov.DisplayName, ov.Note, ov.CreatedAt.ToUniversalTime()));
        var hw = Assert.Single(new HomeworkService(db).GetAll());
        Assert.Equal(("лек математика", "Задачи 1–3", created, 3, "done", doneAt),
            (hw.SubjectRawNormalized, hw.Text, hw.CreatedAt.ToUniversalTime(), hw.TargetNthOccurrence, hw.Status, hw.DoneAt));
    }

    private sealed class OwnedDatabases : IDisposable
    {
        private readonly string _root = Environment.GetEnvironmentVariable("VOGRAPH_PARSER_ARTIFACTS")
            ?? Path.Combine(Path.GetTempPath(), "opencode", "zapara-tt-parser-" + Guid.NewGuid().ToString("N"));
        private readonly string _directory;
        public Database Source { get; }
        public Database Target { get; }

        public OwnedDatabases()
        {
            _directory = Path.Combine(_root, "db-" + Guid.NewGuid().ToString("N"));
            Source = new Database(Path.Combine(_directory, "source.db"));
            Target = new Database(Path.Combine(_directory, "target.db"));
        }

        public void Dispose()
        {
            foreach (var db in new[] { Source, Target })
            {
                db.Connection.Close();
                SqliteConnection.ClearPool(db.Connection);
                db.Dispose();
            }
            Directory.Delete(_directory, recursive: true);
            Assert.False(Directory.Exists(_directory));
            File.WriteAllText(Path.Combine(_root, Path.GetFileName(_directory) + "-cleanup.txt"),
                "Closed/disposed source and target, cleared only their SQLite pools; removed owned directory: " + _directory);
        }
    }
}
