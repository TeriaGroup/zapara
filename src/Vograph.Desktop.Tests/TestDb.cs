using Vograph.Core.Models;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Tests;

/// <summary>Fresh SQLite in a temp folder, seeded from TestData/sample-timetable.xml. Never touches %LocalAppData%.</summary>
public sealed class TestDb : IDisposable
{
    public const string MyGroupId = "3313";
    public const string MathSubject = "лек ВЫСШ. МАТЕМАТ"; // FULL Discipline: Core keeps the type token in SubjectRaw and keys overrides/homework by it

    public string Dir { get; }
    private readonly string testRoot;
    public AppServices Services { get; }

    private TestDb(string dir, string testRoot, AppServices services)
    { Dir = dir; this.testRoot = testRoot; Services = services; }

    public static TestDb Create(bool seedPersonalization = true)
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("VOGRAPH_TEST_DATA_ROOT") is { Length: > 0 } configured
            ? configured : Path.Combine(Path.GetTempPath(), "vograph-tests"));
        var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        // motion off in tests: frames are deterministic, MotionTests switch it on themselves
        var services = AppServices.Create(dir, systemAnimations: () => false);
        services.AllowNetwork = false;

        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-timetable.xml"));
        services.Parser.RefreshAsync(xmlOverride: xml).GetAwaiter().GetResult();

        var s = services.Db.GetSettings();
        s.MyGroupId = MyGroupId;
        services.Db.SaveSettings(s);

        if (seedPersonalization)
        {
            services.Overrides.AddOrUpdate(MathSubject, "global", "Матан", "лекции — в 493");
            services.Homework.AddHomework(MathSubject, "§5, задачи 1–12", 1, createdAt: new DateTime(2026, 9, 5, 12, 0, 0));
            services.Db.InsertFriend(new FriendGroup { GroupName = "09С31", ColorHex = "#F2A33C", Enabled = true, MemberNames = "Иван" });
        }
        return new TestDb(dir, root, services);
    }

    public void Dispose()
    {
        Services.Dispose();
        if (!Path.GetFullPath(Dir).StartsWith(Path.TrimEndingDirectorySeparator(testRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test data directory");
        try { Directory.Delete(Dir, recursive: true); } catch (IOException ex) { Console.Error.WriteLine($"TestDb: temp dir left behind ({Dir}): {ex.Message}"); } // SQLite may still hold the file for a moment
    }
}
