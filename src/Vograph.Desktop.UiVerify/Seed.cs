using Vograph.Core.Models;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.UiVerify;

public static class Seed
{
    public const string MyGroupId = "3313";
    public const string MathSubject = "лек ВЫСШ. МАТЕМАТ";

    /// <summary>Written into every folder this class creates, and required before it will delete one again.</summary>
    public const string Marker = ".uiverify";

    /// <summary>
    /// Why <see cref="Create"/> must not touch <paramref name="dataDir"/>, or null when it is provably scratch.
    /// One mistyped <c>--data</c> used to mean a recursive delete of whatever it pointed at — the user's own
    /// database, prefs, map cache and lecturer cache included, with no backup anywhere (T12-R2). So: never the
    /// folder this process would itself call home (the VOGRAPH_DATA_DIR override <see cref="AppPaths"/> reads
    /// first), never %LocalAppData%\Vograph however it is spelled, and otherwise only a folder that is absent,
    /// empty, or carries this driver's own marker file. The two rules are independent on purpose: a junction
    /// or a short name that slips past the path comparison is still a folder full of files with no marker.
    /// </summary>
    public static string? RefuseReason(string dataDir)
    {
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDir)); }
        catch (Exception ex) { return $"is not a usable path ({ex.GetType().Name}: {ex.Message})"; }

        foreach (var (label, candidate) in Protected())
        {
            if (candidate is null) continue;
            if (string.Equals(full, Path.TrimEndingDirectorySeparator(candidate), StringComparison.OrdinalIgnoreCase))
                return $"is {label} — the real profile, never a scratch folder";
        }

        if (!Directory.Exists(full)) return null;
        if (File.Exists(Path.Combine(full, Marker))) return null;
        try
        {
            if (!Directory.EnumerateFileSystemEntries(full).Any()) return null;
        }
        catch (Exception ex) { return $"cannot be read ({ex.GetType().Name}: {ex.Message})"; }
        return $"already holds files and carries no «{Marker}» marker, so it was not created by this driver";
    }

    /// <summary>The folders that are somebody's real data, composed the way AppPaths composes them but without
    /// its Directory.CreateDirectory: asking a guard where the profile lives must not create the profile.
    /// Reading either can fail (a redirected shell folder, an unusable VOGRAPH_DATA_DIR) — a folder we cannot
    /// name is one we cannot clear either, so a failure drops out of the comparison and the marker rule below
    /// still has to pass.</summary>
    private static IEnumerable<(string Label, string? Path)> Protected()
    {
        yield return ($"the {AppPaths.DataDirEnv} folder", Try(() => Environment.GetEnvironmentVariable(AppPaths.DataDirEnv) is { } v && !string.IsNullOrWhiteSpace(v) ? v : null));
        yield return (@"%LocalAppData%\Vograph", Try(() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vograph")));
        // Both spellings of the same folder: GetFolderPath asks the shell and ignores the environment, so a
        // LOCALAPPDATA that points somewhere else — a redirected profile, and the only way to exercise this rule
        // without aiming the driver at the real one — would otherwise slip past.
        yield return (@"%LOCALAPPDATA%\Vograph", Try(() => Environment.GetEnvironmentVariable("LOCALAPPDATA") is { } v && !string.IsNullOrWhiteSpace(v) ? Path.Combine(v, "Vograph") : null));

        static string? Try(Func<string?> read)
        {
            try { return read() is { } path ? Path.GetFullPath(path) : null; }
            catch (Exception) { return null; } // deliberately unnamed: an unreadable candidate cannot match
        }
    }

    /// <summary>A fresh data folder with the test fixture: group 3313, the «Матан» rename, one homework due Monday, friend 09С31, dark theme, motion on.</summary>
    /// <exception cref="InvalidOperationException">The folder cannot be proved to be scratch; nothing was deleted.</exception>
    public static void Create(string dataDir)
    {
        if (RefuseReason(dataDir) is { } reason)
            throw new InvalidOperationException($"refusing to seed «{Path.GetFullPath(dataDir)}»: it {reason}. Point --data at an empty folder under your scratch tree.");
        if (Directory.Exists(dataDir)) Directory.Delete(dataDir, recursive: true);
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, Marker), $"Vograph.Desktop.UiVerify scratch data, created {DateTime.Now:O}\n");
        var seedDir = Path.Combine(AppContext.BaseDirectory, "Seed");
        File.Copy(Path.Combine(seedDir, "sample-lecturers.xml"), Path.Combine(dataDir, "TimetableLecturer50.xml")); // Core's lecturer cache path
        using var services = AppServices.Create(dataDir, systemAnimations: () => true);
        services.AllowNetwork = false;
        services.Parser.RefreshAsync(xmlOverride: File.ReadAllText(Path.Combine(seedDir, "sample-timetable.xml"))).GetAwaiter().GetResult();
        var s = services.Db.GetSettings();
        s.MyGroupId = MyGroupId;
        s.NotifyTime1 = "20:00";
        s.NotifyTime2 = "07:30";
        services.Db.SaveSettings(s);
        services.Overrides.AddOrUpdate(MathSubject, "global", "Матан", "лекции — в 493");
        services.Homework.AddHomework(MathSubject, "§5, задачи 1–12", 1, createdAt: DateTime.Now.Date.AddDays(-2));
        services.Db.InsertFriend(new FriendGroup { GroupName = "09С31", ColorHex = "#F2A33C", Enabled = true, MemberNames = "Иван" });
        services.Prefs.Theme = ThemeChoice.Dark;
        services.Prefs.Animations = true;
        services.Prefs.Save();
    }
}
