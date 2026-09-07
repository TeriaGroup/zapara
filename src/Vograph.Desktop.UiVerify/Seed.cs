using Vograph.Core.Models;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.UiVerify;

public static class Seed
{
    public const string MyGroupId = "3313";
    public const string MathSubject = "лек ВЫСШ. МАТЕМАТ";

    /// <summary>A fresh data folder with the test fixture: group 3313, the «Матан» rename, one homework due Monday, friend 09С31, dark theme, motion on.</summary>
    public static void Create(string dataDir)
    {
        if (Directory.Exists(dataDir)) Directory.Delete(dataDir, recursive: true);
        Directory.CreateDirectory(dataDir);
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
